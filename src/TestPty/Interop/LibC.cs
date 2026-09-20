using System.Reflection;
using System.Runtime.InteropServices;

namespace TestPty.Interop;

/// <summary>
/// The libc entry points the Unix backend needs.
/// </summary>
/// <remarks>
/// <para>
/// The calls a forked child makes before it execs carry <see cref="SuppressGCTransitionAttribute"/>.
/// That is not an optimisation, it is the thing that makes the child work at all: a normal P/Invoke
/// leaves cooperative mode on the way out and re-enters it on the way back, and re-entering blocks
/// until any in-flight collection finishes. In a child of <c>fork</c> there is no collector thread
/// left to finish one, so the child hangs holding the pseudo-terminal open and the parent waits for
/// output that never comes. Every entry point the child touches is declared with no transition, and
/// <see cref="BindChildCalls"/> binds each one in the parent.
/// </para>
/// <para>
/// This is also why the child kills itself rather than calling <c>_exit</c>: <c>_exit</c> could only
/// ever be bound by calling it, which the parent cannot do.
/// </para>
/// </remarks>
internal static unsafe partial class LibC
{
    private const string CLibrary = "test-pty-libc";
    private const string PtyLibrary = "test-pty-libutil";

    public const int SignalTerminate = 15;
    public const int SignalKill = 9;
    public const int GetDescriptorFlags = 1;
    public const int SetDescriptorFlags = 2;
    public const int CloseOnExec = 1;

    private static readonly nint CLibraryHandle;
    private static readonly nint PtyLibraryHandle;

    static LibC()
    {
        nint[] libraries = LoadCandidates();
        CLibraryHandle = FirstExporting(libraries, "execve");
        PtyLibraryHandle = FirstExporting(libraries, "forkpty");

        NativeLibrary.SetDllImportResolver(typeof(LibC).Assembly, ResolveLibrary);
        BindChildCalls();
    }

    /// <summary>Whether this is an Apple platform.</summary>
    public static bool IsMacOs { get; } = OperatingSystem.IsMacOS();

    /// <summary>
    /// <c>TIOCSWINSZ</c>, which is a different number on every Unix and is the first thing that
    /// breaks when this code meets a platform it has not seen.
    /// </summary>
    public static nuint SetWindowSizeRequest => IsMacOs ? 0x80087467 : 0x5414;

    [LibraryImport(PtyLibrary, EntryPoint = "forkpty")]
    [SuppressGCTransition]
    public static partial int ForkPty(int* master, byte* name, void* terminalAttributes, WinSize* size);

    [LibraryImport(CLibrary, EntryPoint = "chdir")]
    [SuppressGCTransition]
    public static partial int ChangeDirectory(byte* path);

    [LibraryImport(CLibrary, EntryPoint = "execve")]
    [SuppressGCTransition]
    public static partial int ExecuteImage(byte* path, byte** arguments, byte** environment);

    [LibraryImport(CLibrary, EntryPoint = "write")]
    [SuppressGCTransition]
    public static partial nint Write(int descriptor, void* buffer, nuint count);

    [LibraryImport(CLibrary, EntryPoint = "getpid")]
    [SuppressGCTransition]
    public static partial int GetProcessId();

    [LibraryImport(CLibrary, EntryPoint = "kill")]
    [SuppressGCTransition]
    public static partial int Kill(int processId, int signal);

    [LibraryImport(CLibrary, EntryPoint = "__error")]
    [SuppressGCTransition]
    public static partial int* ErrnoLocationMacOs();

    [LibraryImport(CLibrary, EntryPoint = "__errno_location")]
    [SuppressGCTransition]
    public static partial int* ErrnoLocationLinux();

    [LibraryImport(CLibrary, EntryPoint = "read")]
    public static partial nint Read(int descriptor, void* buffer, nuint count);

    [LibraryImport(CLibrary, EntryPoint = "close")]
    public static partial int Close(int descriptor);

    [LibraryImport(CLibrary, EntryPoint = "pipe")]
    public static partial int Pipe(int* descriptors);

    [LibraryImport(CLibrary, EntryPoint = "waitpid")]
    public static partial int WaitPid(int processId, int* status, int options);

    [LibraryImport(CLibrary, EntryPoint = "fcntl")]
    private static partial int FcntlVariadic(int descriptor, int command, int argument);

    [LibraryImport(CLibrary, EntryPoint = "__fcntl")]
    private static partial int FcntlFixedArity(int descriptor, int command, int argument);

    [LibraryImport(CLibrary, EntryPoint = "ioctl")]
    private static partial int IoctlVariadic(int descriptor, nuint request, WinSize* size);

    [LibraryImport(CLibrary, EntryPoint = "__ioctl")]
    private static partial int IoctlFixedArity(int descriptor, nuint request, WinSize* size);

    /// <summary>
    /// On Apple platforms a variadic callee reads its anonymous arguments from the stack, so calling
    /// the public <c>fcntl</c> through a fixed signature passes the third argument in a register the
    /// callee never looks at: it reports success and changes nothing. The <c>__</c>-prefixed syscall
    /// stub takes three real arguments. Elsewhere the ABI passes them in registers either way.
    /// </summary>
    public static int Fcntl(int descriptor, int command, int argument) =>
        IsMacOs ? FcntlFixedArity(descriptor, command, argument) : FcntlVariadic(descriptor, command, argument);

    /// <summary>The same Apple variadic problem as <see cref="Fcntl"/>, where it shows up as EFAULT.</summary>
    public static int SetWindowSize(int descriptor, WinSize* size) =>
        IsMacOs
            ? IoctlFixedArity(descriptor, SetWindowSizeRequest, size)
            : IoctlVariadic(descriptor, SetWindowSizeRequest, size);

    public static int Errno() => IsMacOs ? *ErrnoLocationMacOs() : *ErrnoLocationLinux();

    /// <summary>The exit status a wait reported, as a shell would report it.</summary>
    public static int DecodeWaitStatus(int status) =>
        (status & 0x7f) == 0 ? (status >> 8) & 0xff : 128 + (status & 0x7f);

    /// <summary>
    /// Binds every entry point the forked child uses, here in the parent. Binding a P/Invoke runs
    /// managed code and takes loader locks; in a child of fork that is a hang. Each call is
    /// deliberately inert — a bad descriptor, an empty path, signal zero.
    /// </summary>
    private static void BindChildCalls()
    {
        byte* emptyPath = stackalloc byte[1];
        emptyPath[0] = 0;

        byte** emptyVector = stackalloc byte*[1];
        emptyVector[0] = null;

        _ = IsMacOs ? ErrnoLocationMacOs() : ErrnoLocationLinux();
        ChangeDirectory(emptyPath);
        ExecuteImage(emptyPath, emptyVector, emptyVector);
        Write(-1, emptyPath, 0);
        Kill(GetProcessId(), 0);
    }

    private static nint ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        libraryName switch
        {
            CLibrary => CLibraryHandle,
            PtyLibrary => PtyLibraryHandle,
            _ => 0,
        };

    private static nint[] LoadCandidates()
    {
        string[] candidates =
        [
            "libc",
            "libc.so.6",
            "libSystem.dylib",
            "libutil.so.1",
            "libutil.so",
        ];

        List<nint> loaded = [];

        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out nint handle))
            {
                loaded.Add(handle);
            }
        }

        if (loaded.Count == 0)
        {
            throw new PlatformNotSupportedException("No C library could be loaded, so no pseudo-terminal can be opened.");
        }

        return [.. loaded];
    }

    private static nint FirstExporting(nint[] libraries, string symbol)
    {
        foreach (nint library in libraries)
        {
            if (NativeLibrary.TryGetExport(library, symbol, out _))
            {
                return library;
            }
        }

        throw new PlatformNotSupportedException(
            $"The C library on this system does not export '{symbol}', so no pseudo-terminal can be opened.");
    }
}
