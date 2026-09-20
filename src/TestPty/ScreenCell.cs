namespace TestPty;

/// <summary>How a cell is drawn. Enough to answer "is this highlighted", which is what a TUI test asks.</summary>
public readonly record struct CellAttributes(
    bool IsBold,
    bool IsReverse,
    TerminalColor Foreground,
    TerminalColor Background)
{
    public static CellAttributes Default { get; } = new(false, false, TerminalColor.Default, TerminalColor.Default);
}

/// <summary>One character position on the virtual screen.</summary>
public readonly record struct ScreenCell(char Character, CellAttributes Attributes)
{
    /// <summary>An unwritten cell.</summary>
    public static ScreenCell Blank { get; } = new(' ', CellAttributes.Default);
}
