namespace TestPty.Tests;

/// <summary>
/// Conditions the suite skips on. Named rather than inline so a skipped case says which platform it
/// needed.
/// </summary>
public static class TestConditions
{
    public const string UnixOnly = "This case drives forkpty, which only exists on Unix.";

    public static bool IsUnix => !OperatingSystem.IsWindows();
}
