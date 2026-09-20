using System.Collections;
using System.Globalization;
using System.Runtime;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using TestPty.Interop;

namespace TestPty;

/// <summary>
/// A pseudo-terminal from <c>forkpty</c>. The program runs as a real child with a real controlling
/// terminal, which is the whole point of the harness.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed unsafe class UnixPtyBackend : IPtyBackend
{
    private const long ForkNoGCBudgetBytes = 1 << 20;

    private static readonly TimeSpan TerminateGrace = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReapGrace = TimeSpan.FromSeconds(2);
    private static readonly Lock ForkGate = new();

    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();

    private FileStream? master;
    private Thread? reaper;
    private int masterDescriptor = -1;
    private int childProcessId = -1;
    private int? exitCode;
    private bool isStarted;
    private bool isDisposed;

    public bool IsRunning => isStarted && !exited.Task.IsCompleted;

    public int? ExitCode
    {
        get
        {
            lock (gate)
            {
                return exitCode;
            }
        }
    }

    public void Start(PtyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (isStarted)
        {
            throw new InvalidOperationException("This backend has already started a program.");
        }

        string image = ProgramResolver.Resolve(options.Program);

        using var strings = new NativeStrings();
        byte* imagePath = strings.Allocate(image);
        byte** argumentVector = strings.AllocateArray([options.Program, .. options.Arguments]);
        byte** environmentVector = strings.AllocateArray(BuildEnvironment(options));
        byte* workingDirectory = options.WorkingDirectory is null ? null : strings.Allocate(options.WorkingDirectory);

        (int readEnd, int writeEnd) = CreateExecFailurePipe();

        // Every call the child makes is reached through a local function pointer taken here, in the
        // parent. Calling LibC.Something directly would be a static call on a type with a static
        // constructor, and the class-initialisation helper behind it takes a runtime lock that no
        // thread survives the fork to release.
        delegate* managed<byte*, int> changeDirectory = &LibC.ChangeDirectory;
        delegate* managed<byte*, byte**, byte**, int> executeImage = &LibC.ExecuteImage;
        delegate* managed<int, void*, nuint, nint> write = &LibC.Write;
        delegate* managed<int, int, int> kill = &LibC.Kill;
        delegate* managed<int> getProcessId = &LibC.GetProcessId;
        delegate* managed<int*> errnoLocation = LibC.IsMacOs ? &LibC.ErrnoLocationMacOs : &LibC.ErrnoLocationLinux;
        int killSignal = LibC.SignalKill;

        WinSize size = WinSize.From(options.Size.Columns, options.Size.Rows);
        int descriptor;
        int processId;

        // Forking is serialised, and collection is held off across the fork. A collection that
        // begins while this thread is on its way into forkpty leaves the child with the runtime's
        // "stop for a collection" flag set and no collector to clear it.
        lock (ForkGate)
        {
            bool isNoGCRegionStarted = TryHoldOffCollection();

            try
            {
                processId = LibC.ForkPty(&descriptor, null, null, &size);

                if (processId == 0)
                {
                    // Nothing here may call a managed method, read a static, or allocate. Locals and
                    // libc, then die. See the remarks on LibC.
                    int failure = 0;

                    if (workingDirectory is not null && changeDirectory(workingDirectory) != 0)
                    {
                        failure = *errnoLocation();
                    }

                    if (failure == 0)
                    {
                        executeImage(imagePath, argumentVector, environmentVector);
                        failure = *errnoLocation();
                    }

                    write(writeEnd, &failure, (nuint)sizeof(int));
                    kill(getProcessId(), killSignal);
                }
            }
            finally
            {
                if (isNoGCRegionStarted)
                {
                    ResumeCollection();
                }
            }
        }

        if (processId < 0)
        {
            int errno = LibC.Errno();
            LibC.Close(readEnd);
            LibC.Close(writeEnd);
            throw new PtyStartException($"forkpty failed with errno {errno}.");
        }

        LibC.Close(writeEnd);
        int execFailure = ReadExecFailure(readEnd);

        if (execFailure != 0)
        {
            LibC.Close(descriptor);
            ReapAbandonedChild(processId);
            throw new PtyStartException($"Could not start '{options.Program}': errno {execFailure}.");
        }

        childProcessId = processId;
        masterDescriptor = descriptor;
        master = new FileStream(new SafeFileHandle(descriptor, ownsHandle: true), FileAccess.ReadWrite, bufferSize: 0);
        isStarted = true;
        StartReaper();
    }

    public void Write(ReadOnlySpan<byte> data) => Stream.Write(data);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        Stream.ReadAsync(buffer, cancellationToken);

    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        WinSize size = WinSize.From(columns, rows);

        if (LibC.SetWindowSize(RunningDescriptor, &size) != 0)
        {
            throw new IOException($"ioctl(TIOCSWINSZ) failed with errno {LibC.Errno()}.");
        }
    }

    public bool WaitForExit(TimeSpan timeout)
    {
        if (!isStarted)
        {
            throw new InvalidOperationException("No program has been started.");
        }

        return exited.Task.Wait(timeout);
    }

    public void Kill()
    {
        if (!isStarted || exited.Task.IsCompleted)
        {
            return;
        }

        LibC.Kill(childProcessId, LibC.SignalTerminate);

        if (exited.Task.Wait(TerminateGrace))
        {
            return;
        }

        LibC.Kill(childProcessId, LibC.SignalKill);
        exited.Task.Wait(ReapGrace);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        if (isStarted && !exited.Task.IsCompleted)
        {
            LibC.Kill(childProcessId, LibC.SignalKill);
        }

        reaper?.Join(ReapGrace);

        master?.Dispose();
        master = null;
    }

    /// <summary>
    /// Best effort. If a collection cannot be held off — because one is already being held off
    /// elsewhere, or the budget cannot be reserved — the fork still goes ahead; the window it closes
    /// is narrow, and failing to start a program because of it would be worse.
    /// </summary>
    private static bool TryHoldOffCollection()
    {
        try
        {
            return GCSettings.LatencyMode != GCLatencyMode.NoGCRegion
                && GC.TryStartNoGCRegion(ForkNoGCBudgetBytes);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static void ResumeCollection()
    {
        if (GCSettings.LatencyMode != GCLatencyMode.NoGCRegion)
        {
            return;
        }

        GC.EndNoGCRegion();
    }

    private FileStream Stream =>
        master ?? throw new InvalidOperationException("No program has been started.");

    private int RunningDescriptor =>
        masterDescriptor >= 0 ? masterDescriptor : throw new InvalidOperationException("No program has been started.");

    private static List<string> BuildEnvironment(PtyOptions options)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            variables[(string)entry.Key] = entry.Value as string ?? string.Empty;
        }

        variables["TERM"] = options.Term;
        variables["COLUMNS"] = options.Size.Columns.ToString(CultureInfo.InvariantCulture);
        variables["LINES"] = options.Size.Rows.ToString(CultureInfo.InvariantCulture);

        foreach ((string name, string value) in options.Environment)
        {
            variables[name] = value;
        }

        return [.. variables.Select(variable => $"{variable.Key}={variable.Value}")];
    }

    private static (int ReadEnd, int WriteEnd) CreateExecFailurePipe()
    {
        int* descriptors = stackalloc int[2];

        if (LibC.Pipe(descriptors) != 0)
        {
            throw new PtyStartException($"pipe failed with errno {LibC.Errno()}.");
        }

        SetCloseOnExec(descriptors[1]);
        return (descriptors[0], descriptors[1]);
    }

    /// <summary>
    /// The write end must close when the child execs, because that end-of-file is how the parent
    /// learns the exec succeeded. If the flag will not take, say so rather than hang on the read.
    /// </summary>
    private static void SetCloseOnExec(int descriptor)
    {
        if (LibC.Fcntl(descriptor, LibC.SetDescriptorFlags, LibC.CloseOnExec) != 0)
        {
            throw new PtyStartException($"fcntl(F_SETFD) failed with errno {LibC.Errno()}.");
        }

        if ((LibC.Fcntl(descriptor, LibC.GetDescriptorFlags, 0) & LibC.CloseOnExec) == 0)
        {
            throw new PlatformNotSupportedException(
                "fcntl(F_SETFD, FD_CLOEXEC) did not take effect on this platform, so an exec failure " +
                "could not be reported without hanging.");
        }
    }

    private static int ReadExecFailure(int readEnd)
    {
        int failure = 0;
        byte* cursor = (byte*)&failure;
        nuint remaining = (nuint)sizeof(int);

        while (remaining > 0)
        {
            nint count = LibC.Read(readEnd, cursor, remaining);

            if (count <= 0)
            {
                break;
            }

            cursor += count;
            remaining -= (nuint)count;
        }

        LibC.Close(readEnd);
        return remaining == 0 ? failure : 0;
    }

    private static void ReapAbandonedChild(int processId)
    {
        int status = 0;

        while (LibC.WaitPid(processId, &status, 0) < 0 && LibC.Errno() == Errno.Interrupted)
        {
            continue;
        }
    }

    private void StartReaper()
    {
        reaper = new Thread(ReapChild)
        {
            IsBackground = true,
            Name = "test-pty-reaper",
        };

        reaper.Start();
    }

    private void ReapChild()
    {
        int status = 0;
        int result;

        while ((result = LibC.WaitPid(childProcessId, &status, 0)) < 0 && LibC.Errno() == Errno.Interrupted)
        {
            continue;
        }

        lock (gate)
        {
            exitCode = result < 0 ? -1 : LibC.DecodeWaitStatus(status);
        }

        exited.TrySetResult();
    }
}
