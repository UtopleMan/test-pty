namespace TestPty;

/// <summary>Where something is on the screen, so a test need not hard-code a row that will move.</summary>
public readonly record struct ScreenPosition(int Row, int Column);

/// <summary>A rectangle on the screen, measured from the glyphs that drew it.</summary>
public readonly record struct ScreenRegion(int Row, int Column, int Width, int Height);

/// <summary>
/// The four corners a box is drawn with. A caller says which style it expects rather than scanning
/// for corner characters itself.
/// </summary>
public readonly record struct BoxGlyphs(char TopLeft, char TopRight, char BottomLeft, char BottomRight)
{
    /// <summary>The rounded corners a duetui dialog draws.</summary>
    public static BoxGlyphs Rounded { get; } = new('╭', '╮', '╰', '╯');

    /// <summary>The square corners Terminal.Gui draws by default.</summary>
    public static BoxGlyphs Single { get; } = new('┌', '┐', '└', '┘');

    public static BoxGlyphs Double { get; } = new('╔', '╗', '╚', '╝');
}
