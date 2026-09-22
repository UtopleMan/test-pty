namespace TestPty;

/// <summary>Which button a mouse report is about.</summary>
public enum MouseButton
{
    Left,
    Middle,
    Right,
}

/// <summary>Which way a wheel turned.</summary>
public enum WheelDirection
{
    Up,
    Down,
}

/// <summary>
/// What a test clicks with. The reports are SGR (1006) — the extended encoding, which is the only
/// one that can name a column past 223 and the one every terminal written this century sends. A
/// program that has not asked for mouse reporting ignores these, so a click that does nothing is a
/// sign the program never enabled it.
/// </summary>
/// <remarks>
/// Every coordinate here is a zero-based screen position, matching <see cref="Screen.Cell"/> and
/// <see cref="ScreenPosition"/>. The one-based numbers the protocol carries are an encoding detail
/// and do not reach a caller.
/// </remarks>
public static class Mouse
{
    private const int WheelUpCode = 64;
    private const int WheelDownCode = 65;
    private const int MotionCode = 35;
    private const char PressTerminator = 'M';
    private const char ReleaseTerminator = 'm';

    /// <summary>A button going down.</summary>
    public static string Press(int column, int row, MouseButton button = MouseButton.Left) =>
        Report(CodeOf(button), column, row, PressTerminator);

    /// <summary>A button coming back up.</summary>
    public static string Release(int column, int row, MouseButton button = MouseButton.Left) =>
        Report(CodeOf(button), column, row, ReleaseTerminator);

    /// <summary>A press and the release that follows it, which is what a click is.</summary>
    public static string Click(int column, int row, MouseButton button = MouseButton.Left) =>
        Press(column, row, button) + Release(column, row, button);

    /// <summary>One notch of the wheel. A terminal reports a wheel as a press with no release.</summary>
    public static string Wheel(int column, int row, WheelDirection direction) =>
        Report(direction is WheelDirection.Up ? WheelUpCode : WheelDownCode, column, row, PressTerminator);

    /// <summary>The pointer moving with no button held.</summary>
    public static string Move(int column, int row) => Report(MotionCode, column, row, PressTerminator);

    private static int CodeOf(MouseButton button) =>
        button switch
        {
            MouseButton.Left => 0,
            MouseButton.Middle => 1,
            MouseButton.Right => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "No such mouse button."),
        };

    private static string Report(int code, int column, int row, char terminator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfNegative(row);

        return $"{Ansi.Escape}[<{code};{column + 1};{row + 1}{terminator}";
    }
}
