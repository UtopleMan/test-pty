namespace TestPty;

/// <summary>
/// A pseudo-terminal holding one child process. One interface, one implementation per platform, no
/// platform types in the signatures.
/// </summary>
public interface IPtyBackend : IDisposable
{
    /// <summary>Whether the child has been started and has not yet been observed to exit.</summary>
    bool IsRunning { get; }

    /// <summary>The child's exit status, or <see langword="null"/> until it has been reaped.</summary>
    int? ExitCode { get; }

    /// <summary>Launches the child on a new pseudo-terminal.</summary>
    void Start(PtyOptions options);

    /// <summary>Writes to the terminal, as a keyboard would.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>
    /// Reads what the child has drawn. Returns 0 at end of output. Completes only when there is
    /// something to report, so a caller can pump without polling.
    /// </summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    /// <summary>Tells the child the window changed size.</summary>
    void Resize(int columns, int rows);

    /// <summary>Waits for the child to exit. Returns <see langword="false"/> on timeout.</summary>
    bool WaitForExit(TimeSpan timeout);

    /// <summary>Ends the child, whether or not it wants to end.</summary>
    void Kill();
}
