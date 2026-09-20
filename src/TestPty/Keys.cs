namespace TestPty;

/// <summary>
/// What a test sends. The escape sequences are written once, here, rather than in every consumer's
/// tests.
/// </summary>
public static class Keys
{
    public const string CtrlA = "\u0001";
    public const string CtrlB = "\u0002";
    public const string CtrlC = "\u0003";
    public const string CtrlD = "\u0004";
    public const string CtrlE = "\u0005";
    public const string CtrlK = "\u000b";
    public const string CtrlL = "\u000c";
    public const string CtrlU = "\u0015";
    public const string CtrlW = "\u0017";

    public const string Enter = "\r";
    public const string Tab = "\t";
    public const string Escape = "\u001b";

    /// <summary>DEL, which is what a terminal's Backspace key actually sends.</summary>
    public const string Backspace = "\u007f";

    public const string Up = "\u001b[A";
    public const string Down = "\u001b[B";
    public const string Right = "\u001b[C";
    public const string Left = "\u001b[D";

    public const string Home = "\u001b[H";
    public const string End = "\u001b[F";
    public const string Delete = "\u001b[3~";
    public const string PageUp = "\u001b[5~";
    public const string PageDown = "\u001b[6~";

    /// <summary>The control character a given letter produces when Ctrl is held.</summary>
    public static string Ctrl(char letter)
    {
        char upper = char.ToUpperInvariant(letter);

        if (upper is < 'A' or > 'Z')
        {
            throw new ArgumentOutOfRangeException(nameof(letter), letter, "Ctrl applies to letters A to Z.");
        }

        return ((char)(upper - 'A' + 1)).ToString();
    }
}
