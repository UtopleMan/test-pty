using System.Runtime.Versioning;
using Xunit;

namespace TestPty.Tests;

/// <summary>
/// The harness against real programs with known output, before either consumer depends on it.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class RealProgramTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [UnixFact]
    public async Task A_shell_prompt_can_be_waited_for_and_answered()
    {
        using PtySession session = PtySession.Start(Options("sh", ["-i"]));

        await session.WaitFor("READY> ", Timeout, TestToken);

        session.Send("echo from-the-shell" + Keys.Enter);
        await session.WaitFor("from-the-shell", Timeout, TestToken);

        await session.WaitFor("READY> ", Timeout, TestToken);

        Assert.Contains("from-the-shell", session.Screen.ToText(), StringComparison.Ordinal);
    }

    [UnixFact]
    public async Task A_program_that_redraws_in_place_is_judged_by_the_grid_and_not_the_stream()
    {
        PtyOptions options = Options(
            "sh",
            ["-c", @"printf 'Loading    '; printf '\rLoading .  '; printf '\rLoading ...'; printf '\r\nend'"]);

        using PtySession session = PtySession.Start(options with { Size = (40, 4) });

        await session.WaitFor("end", Timeout, TestToken);
        await session.WaitForIdle(TimeSpan.FromMilliseconds(50), Timeout, TestToken);

        Assert.Equal("Loading ...", session.Screen.Row(0));
        Assert.Equal("end", session.Screen.Row(1));

        string stream = System.Text.Encoding.UTF8.GetString(session.RawCapture);
        Assert.Contains("Loading    ", stream, StringComparison.Ordinal);
        Assert.Contains("Loading .  ", stream, StringComparison.Ordinal);
    }

    [UnixFact]
    public async Task A_control_character_arrives_as_the_signal_it_stands_for()
    {
        PtyOptions options = Options(
            "sh",
            ["-c", "trap 'echo GOT-SIGINT' INT; echo ARMED; read ignored"]);

        using PtySession session = PtySession.Start(options);

        await session.WaitFor("ARMED", Timeout, TestToken);

        session.Send(Keys.CtrlC);

        await session.WaitFor("GOT-SIGINT", Timeout, TestToken);
    }

    [UnixFact]
    public async Task A_screen_wait_can_follow_the_cursor_of_a_real_program()
    {
        PtyOptions options = Options("sh", ["-c", @"printf 'abcd'; printf '\033[1;3H'; read ignored"]);

        using PtySession session = PtySession.Start(options with { Size = (20, 4) });

        await session.WaitForScreen(
            screen => screen.CursorColumn == 2,
            "the cursor to move back to column 2",
            Timeout,
            TestToken);

        Assert.Equal("abcd", session.Screen.Row(0));
    }

    /// <summary>
    /// The example in README.md, compiled and run, so the documentation cannot rot. Change one and
    /// change the other.
    /// </summary>
    [UnixFact]
    public async Task The_readme_example_works()
    {
        using PtySession pty = PtySession.Start(new PtyOptions("sh", ["-i"])
        {
            Size = (80, 24),
            Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["PS1"] = "$ " },
        });

        await pty.WaitFor("$ ");

        pty.Send("printf 'working'" + Keys.Enter);
        await pty.WaitFor("working");

        pty.Send("printf '\\rdone   '" + Keys.Enter);
        await pty.WaitForIdle();

        Assert.Contains("done", pty.Screen.ToText(), StringComparison.Ordinal);
    }

    private static PtyOptions Options(string program, IReadOnlyList<string> arguments) =>
        new(program, arguments)
        {
            DefaultTimeout = Timeout,
            Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["PS1"] = "READY> " },
        };
}
