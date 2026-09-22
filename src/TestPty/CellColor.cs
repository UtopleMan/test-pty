namespace TestPty;

/// <summary>Which of the terminal's colour vocabularies a cell's colour was written in.</summary>
public enum ColorKind
{
    Default,
    Named,
    Indexed,
    Rgb,
}

/// <summary>
/// A colour as the terminal expressed it. <see cref="Value"/> means whatever <see cref="Kind"/> says
/// it means — a <see cref="TerminalColor"/>, a palette index, or packed <c>0xRRGGBB</c> — so an
/// indexed colour and a truecolor one can share a cell without either being flattened into the other.
/// </summary>
public readonly record struct CellColor(ColorKind Kind, int Value)
{
    /// <summary>Whatever the terminal's own default is, which no palette can answer for.</summary>
    public static CellColor Default { get; }

    /// <summary>The named colour this is, or <see cref="TerminalColor.Default"/> when it is not one.</summary>
    public TerminalColor Name => Kind is ColorKind.Named ? (TerminalColor)Value : TerminalColor.Default;

    /// <summary>One of the sixteen colours a terminal names, from SGR 30–37, 40–47, 90–97 or 100–107.</summary>
    public static CellColor Named(TerminalColor name) =>
        name is TerminalColor.Default ? Default : new(ColorKind.Named, (int)name);

    /// <summary>A colour from the 256-entry palette, from SGR <c>38;5;n</c> or <c>48;5;n</c>.</summary>
    public static CellColor Indexed(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 255);

        return new(ColorKind.Indexed, index);
    }

    /// <summary>A 24-bit colour as <c>0xRRGGBB</c>, from SGR <c>38;2;r;g;b</c> or <c>48;2;r;g;b</c>.</summary>
    public static CellColor Rgb(int redGreenBlue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(redGreenBlue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(redGreenBlue, 0xffffff);

        return new(ColorKind.Rgb, redGreenBlue);
    }

    /// <summary>
    /// This colour as 24-bit <c>0xRRGGBB</c>, so a palette index and a truecolor value can be
    /// compared. <see langword="null"/> for the terminal's default, which is whatever the user's
    /// terminal decided and is not ours to invent.
    /// </summary>
    public int? ToRgb() =>
        Kind switch
        {
            ColorKind.Rgb => Value,
            ColorKind.Indexed => XtermPalette.Rgb(Value),
            ColorKind.Named => XtermPalette.Rgb(Value - 1),
            _ => null,
        };
}

/// <summary>
/// The colours xterm gives the 256 palette entries: sixteen it names, a 6×6×6 cube, and a grey ramp.
/// A terminal may be configured to disagree about the first sixteen, which is why a test that cares
/// about an exact colour should assert on a truecolor cell rather than a named one.
/// </summary>
internal static class XtermPalette
{
    private const int CubeOrigin = 16;
    private const int GreyOrigin = 232;

    private static readonly int[] NamedColors =
    [
        0x000000, 0xcd0000, 0x00cd00, 0xcdcd00, 0x0000ee, 0xcd00cd, 0x00cdcd, 0xe5e5e5,
        0x7f7f7f, 0xff0000, 0x00ff00, 0xffff00, 0x5c5cff, 0xff00ff, 0x00ffff, 0xffffff,
    ];

    private static readonly int[] CubeLevels = [0, 95, 135, 175, 215, 255];

    public static int Rgb(int index) =>
        index switch
        {
            < 0 or > 255 => 0,
            < CubeOrigin => NamedColors[index],
            < GreyOrigin => CubeColor(index - CubeOrigin),
            _ => GreyColor(index - GreyOrigin),
        };

    private static int CubeColor(int offset) =>
        Pack(CubeLevels[offset / 36], CubeLevels[offset / 6 % 6], CubeLevels[offset % 6]);

    private static int GreyColor(int step)
    {
        int level = 8 + (step * 10);

        return Pack(level, level, level);
    }

    private static int Pack(int red, int green, int blue) => (red << 16) | (green << 8) | blue;
}
