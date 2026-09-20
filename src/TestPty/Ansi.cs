namespace TestPty;

/// <summary>The handful of literal bytes the parser reasons about, named once.</summary>
internal static class Ansi
{
    public const char Escape = '\u001b';
    public const char Bell = '\u0007';

    /// <summary>Whether a byte ends a control sequence.</summary>
    public static bool IsFinalByte(char character) => character is >= '@' and <= '~';
}
