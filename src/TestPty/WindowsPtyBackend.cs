using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TestPty.Interop;

namespace TestPty;

/// <summary>
/// A pseudo-terminal from ConPTY. The program runs as a real child attached to a pseudo console,
/// and the parent talks to it through a pair of pipes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPtyBackend : IPtyBackend
{
    private static readonly TimeSpan ReapGrace = TimeSpan.FromSeconds(2);

    private readonly TaskCompletionSource exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock gate = new();

    private FileStream? input;
    private FileStream? output;
    private Thread? reaper;
    private nint pseudoConsole;
    private nint process;
    private nint attributeList;
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

        CreatePipes(out SafeFileHandle inputRead, out SafeFileHandle inputWrite);
        CreatePipes(out SafeFileHandle outputRead, out SafeFileHandle outputWrite);

        OpenPseudoConsole(options, inputRead, outputWrite);

        // ConPTY duplicates its ends into the console host, so the copies here are no longer needed.
        inputRead.Dispose();
        outputWrite.Dispose();

        StartChild(options);

        input = new FileStream(inputWrite, FileAccess.Write, bufferSize: 0);
        output = new FileStream(outputRead, FileAccess.Read, bufferSize: 0);
        isStarted = true;
        StartReaper();
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        FileStream stream = input ?? throw new InvalidOperationException("No program has been started.");
        stream.Write(data);
        stream.Flush();
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        FileStream stream = output ?? throw new InvalidOperationException("No program has been started.");
        return stream.ReadAsync(buffer, cancellationToken);
    }

    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        if (pseudoConsole == 0)
        {
            throw new InvalidOperationException("No program has been started.");
        }

        int result = Kernel32.ResizePseudoConsole(pseudoConsole, Coord.From(columns, rows));

        if (result != 0)
        {
            throw new IOException($"ResizePseudoConsole failed with HRESULT 0x{result:x8}.");
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

        Kernel32.TerminateProcess(process, 1);
        exited.Task.Wait(ReapGrace);
    }

    /// <summary>
    /// Teardown in the order ConPTY requires. Out of order this deadlocks, and it deadlocks
    /// sometimes rather than always, which is worse. The session's reader is still draining the
    /// output pipe while <c>ClosePseudoConsole</c> runs, which is what stops it blocking.
    /// </summary>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;

        input?.Dispose();
        input = null;

        if (pseudoConsole != 0)
        {
            Kernel32.ClosePseudoConsole(pseudoConsole);
            pseudoConsole = 0;
        }

        reaper?.Join(ReapGrace);

        output?.Dispose();
        output = null;

        if (attributeList != 0)
        {
            Kernel32.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = 0;
        }

        if (process != 0)
        {
            Kernel32.CloseHandle(process);
            process = 0;
        }
    }

    private static void CreatePipes(out SafeFileHandle readSide, out SafeFileHandle writeSide)
    {
        if (!Kernel32.CreatePipe(out readSide, out writeSide, 0, 0))
        {
            throw new PtyStartException($"CreatePipe failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }
    }

    private void OpenPseudoConsole(PtyOptions options, SafeFileHandle inputRead, SafeFileHandle outputWrite)
    {
        int result = Kernel32.CreatePseudoConsole(
            Coord.From(options.Size.Columns, options.Size.Rows),
            inputRead,
            outputWrite,
            0,
            out pseudoConsole);

        if (result != 0)
        {
            throw new PtyStartException($"CreatePseudoConsole failed with HRESULT 0x{result:x8}.");
        }
    }

    private void StartChild(PtyOptions options)
    {
        StartupInfoEx startupInfo = BuildStartupInfo();

        nint commandLine = Marshal.StringToHGlobalUni(WindowsCommandLine.Build(options.Program, options.Arguments));
        nint environment = Marshal.StringToHGlobalUni(BuildEnvironmentBlock(options));
        nint workingDirectory = options.WorkingDirectory is null
            ? 0
            : Marshal.StringToHGlobalUni(options.WorkingDirectory);

        try
        {
            bool started = Kernel32.CreateProcess(
                0,
                commandLine,
                0,
                0,
                inheritHandles: false,
                Kernel32.ExtendedStartupInfoPresent | Kernel32.CreateUnicodeEnvironment,
                environment,
                workingDirectory,
                in startupInfo,
                out ProcessInformation information);

            if (!started)
            {
                throw new PtyStartException(
                    $"Could not start '{options.Program}': {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            }

            process = information.Process;
            Kernel32.CloseHandle(information.Thread);
        }
        finally
        {
            Marshal.FreeHGlobal(commandLine);
            Marshal.FreeHGlobal(environment);

            if (workingDirectory != 0)
            {
                Marshal.FreeHGlobal(workingDirectory);
            }
        }
    }

    private StartupInfoEx BuildStartupInfo()
    {
        nuint size = 0;
        Kernel32.InitializeProcThreadAttributeList(0, 1, 0, ref size);

        if (size == 0)
        {
            throw new PtyStartException("InitializeProcThreadAttributeList did not report a size.");
        }

        attributeList = Marshal.AllocHGlobal((int)size);

        if (!Kernel32.InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
        {
            throw new PtyStartException(
                $"InitializeProcThreadAttributeList failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        bool updated = Kernel32.UpdateProcThreadAttribute(
            attributeList,
            0,
            Kernel32.ProcThreadAttributePseudoConsole,
            pseudoConsole,
            (nuint)nint.Size,
            0,
            0);

        if (!updated)
        {
            throw new PtyStartException(
                $"UpdateProcThreadAttribute failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        return new StartupInfoEx
        {
            StartupInfo = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() },
            AttributeList = attributeList,
        };
    }

    private static string BuildEnvironmentBlock(PtyOptions options)
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);

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

        var builder = new StringBuilder();

        foreach (string name in variables.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(name).Append('=').Append(variables[name]).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
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
        Kernel32.WaitForSingleObject(process, Kernel32.Infinite);

        lock (gate)
        {
            exitCode = Kernel32.GetExitCodeProcess(process, out uint status) ? (int)status : -1;
        }

        exited.TrySetResult();
    }
}
