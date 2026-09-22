namespace TestPty;

/// <summary>How a cell is drawn. Enough to answer "is this highlighted", which is what a TUI test asks.</summary>
/// <remarks>
/// <see cref="ForegroundColor"/> and <see cref="BackgroundColor"/> answer every colour the terminal
/// can express. <see cref="Foreground"/> and <see cref="Background"/> are the same colours seen
/// through the sixteen a terminal names, and read <see cref="TerminalColor.Default"/> for an indexed
/// or truecolor cell, which is not one of those sixteen.
/// </remarks>
public readonly record struct CellAttributes(
    bool IsBold,
    bool IsReverse,
    CellColor ForegroundColor,
    CellColor BackgroundColor)
{
    public static CellAttributes Default { get; } = new(false, false, CellColor.Default, CellColor.Default);

    public TerminalColor Foreground => ForegroundColor.Name;

    public TerminalColor Background => BackgroundColor.Name;
}

/// <summary>One character position on the virtual screen.</summary>
public readonly record struct ScreenCell(char Character, CellAttributes Attributes)
{
    /// <summary>An unwritten cell.</summary>
    public static ScreenCell Blank { get; } = new(' ', CellAttributes.Default);
}
