namespace TestPty;

/// <summary>
/// Everything needed to launch a program under a pseudo-terminal, and the default timeout every
/// wait on the resulting session inherits.
/// </summary>
public sealed record PtyOptions(string Program, IReadOnlyList<string> Arguments)
{
    public PtyOptions(string program)
        : this(program, [])
    {
    }

    /// <summary>Directory the child starts in. <see langword="null"/> keeps the parent's.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Variables added to, or replacing, the ones the child would otherwise inherit.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The window size the child is told it has.</summary>
    public (int Columns, int Rows) Size { get; init; } = (80, 24);

    /// <summary>The <c>TERM</c> the child sees.</summary>
    public string Term { get; init; } = "xterm-256color";

    /// <summary>The timeout a wait uses when the call site does not give one.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Where a failing wait writes <c>capture.raw</c> and <c>capture.txt</c>. Leave it unset and
    /// nothing is written; set it and a failure on a machine nobody is watching can still be read.
    /// </summary>
    public string? CaptureDirectory { get; init; }
}
