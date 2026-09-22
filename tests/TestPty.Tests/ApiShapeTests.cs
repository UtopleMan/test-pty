using System.Text;
using Xunit;

namespace TestPty.Tests;

/// <summary>
/// Drives <see cref="PtySession"/> end to end against <see cref="FakePtyBackend"/>. No real process,
/// no sleeps.
/// </summary>
public sealed class ApiShapeTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(5);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static PtyOptions QuickOptions => new("fake")
    {
        Size = (20, 4),
        DefaultTimeout = ShortTimeout,
    };

    [Fact]
    public async Task ClickAt_reports_a_click_where_the_text_was_drawn()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("\r\n  Next");
        await session.WaitFor("Next", GenerousTimeout, TestToken);

        await session.ClickAt("Next", offset: 2, cancellationToken: TestToken);

        Assert.Equal(Mouse.Click(4, 1), backend.WrittenText);
    }

    [Fact]
    public async Task ClickAt_can_wait_for_what_the_click_produced_rather_than_for_quiet()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("Next");
        await session.WaitFor("Next", GenerousTimeout, TestToken);

        Task click = session.ClickAt(
            "Next",
            until: screen => screen.Find("done") is not null,
            cancellationToken: TestToken);

        backend.Emit("\r\ndone");
        await click;

        Assert.Equal(Mouse.Click(0, 0), backend.WrittenText);
    }

    [Fact]
    public async Task ClickAt_says_what_it_could_not_find_rather_than_clicking_nowhere()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("nothing here");
        await session.WaitFor("nothing", GenerousTimeout, TestToken);

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ClickAt("Next", cancellationToken: TestToken));

        Assert.Contains("Next", failure.Message, StringComparison.Ordinal);
        Assert.Empty(backend.WrittenText);
    }

    [Fact]
    public void Start_passes_the_options_to_the_backend()
    {
        var backend = new FakePtyBackend();
        PtyOptions options = QuickOptions;

        using PtySession session = PtySession.Start(backend, options);

        Assert.Same(options, backend.StartedWith);
        Assert.True(backend.IsRunning);
    }

    [Fact]
    public void Send_reaches_the_backend()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        session.Send("echo hi");
        session.Send(Keys.Enter);

        Assert.Equal("echo hi\r", backend.WrittenText);
    }

    [Fact]
    public async Task WaitFor_returns_when_the_scripted_text_arrives()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("welcome\r\n");
        backend.Emit("$ ");

        await session.WaitFor("$ ", GenerousTimeout, TestToken);

        Assert.Contains("welcome", session.PlainText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitFor_matches_text_split_across_reads()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        Task waiting = session.WaitFor("hello", GenerousTimeout, TestToken);

        backend.Emit("hel");
        backend.Emit("lo");

        await waiting;
    }

    [Fact]
    public async Task WaitFor_advances_past_what_it_matched()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("$ ");
        await session.WaitFor("$ ", GenerousTimeout, TestToken);

        Task second = session.WaitFor("$ ", GenerousTimeout, TestToken);
        Assert.False(second.IsCompleted);

        backend.Emit("$ ");
        await second;
    }

    [Fact]
    public async Task WaitFor_that_never_matches_throws_with_the_expectation_and_the_screen()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("ready");
        await session.WaitFor("ready", GenerousTimeout, TestToken);

        PtyTimeoutException failure = await Assert.ThrowsAsync<PtyTimeoutException>(
            () => session.WaitFor("never-arrives", ShortTimeout, TestToken));

        Assert.Contains("never-arrives", failure.Message, StringComparison.Ordinal);
        Assert.Contains("ready", failure.Message, StringComparison.Ordinal);
        Assert.Contains("ready", failure.ScreenText, StringComparison.Ordinal);
        Assert.Equal(ShortTimeout, failure.Timeout);
    }

    [Fact]
    public async Task WaitForIdle_returns_once_the_output_stops()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("drawing");

        await session.WaitForIdle(TimeSpan.FromMilliseconds(30), GenerousTimeout, TestToken);

        Assert.Equal("drawing", session.Screen.Row(0));
    }

    [Fact]
    public async Task WaitForIdle_does_not_return_while_output_keeps_arriving()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);
        using var noise = new OutputNoise(backend, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<PtyTimeoutException>(
            () => session.WaitForIdle(TimeSpan.FromMilliseconds(200), ShortTimeout, TestToken));
    }

    [Fact]
    public async Task RawCapture_keeps_every_byte_the_program_wrote()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        backend.Emit("\u001b[2Jab");
        await session.WaitFor("ab", GenerousTimeout, TestToken);

        Assert.Equal(Encoding.UTF8.GetBytes("\u001b[2Jab"), session.RawCapture);
        Assert.Equal("ab", session.Screen.Row(0));
    }

    [Fact]
    public async Task ExitCode_is_reported_once_the_program_exits()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        Assert.Null(session.ExitCode);

        backend.Emit("bye");
        await session.WaitFor("bye", GenerousTimeout, TestToken);
        backend.Exit(3);

        Assert.Equal(3, await session.WaitForExit(GenerousTimeout, TestToken));
        Assert.Equal(3, session.ExitCode);
    }

    [Fact]
    public async Task WaitForExit_throws_when_the_program_keeps_running()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, QuickOptions);

        PtyTimeoutException failure = await Assert.ThrowsAsync<PtyTimeoutException>(
            () => session.WaitForExit(TimeSpan.FromMilliseconds(100), TestToken));

        Assert.Contains("exit", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispose_is_safe_to_call_twice()
    {
        var backend = new FakePtyBackend();
        PtySession session = PtySession.Start(backend, QuickOptions);

        session.Dispose();
        session.Dispose();

        Assert.Equal(1, backend.DisposeCount);
    }

    [Fact]
    public void Dispose_kills_a_program_that_is_still_running()
    {
        var backend = new FakePtyBackend();
        PtySession session = PtySession.Start(backend, QuickOptions);

        session.Dispose();

        Assert.True(backend.WasKilled);
    }

    [Theory]
    [InlineData(nameof(Keys.CtrlC), "\u0003")]
    [InlineData(nameof(Keys.CtrlD), "\u0004")]
    [InlineData(nameof(Keys.Enter), "\r")]
    [InlineData(nameof(Keys.Backspace), "\u007f")]
    [InlineData(nameof(Keys.Up), "\u001b[A")]
    [InlineData(nameof(Keys.Down), "\u001b[B")]
    [InlineData(nameof(Keys.Right), "\u001b[C")]
    [InlineData(nameof(Keys.Left), "\u001b[D")]
    [InlineData(nameof(Keys.Home), "\u001b[H")]
    [InlineData(nameof(Keys.End), "\u001b[F")]
    [InlineData(nameof(Keys.Delete), "\u001b[3~")]
    [InlineData(nameof(Keys.Tab), "\t")]
    [InlineData(nameof(Keys.Escape), "\u001b")]
    public void Keys_are_the_sequences_a_terminal_sends(string name, string expected)
    {
        string? actual = typeof(Keys).GetField(name)?.GetValue(null) as string;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Ctrl_maps_a_letter_to_its_control_character()
    {
        Assert.Equal(Keys.CtrlC, Keys.Ctrl('c'));
        Assert.Equal(Keys.CtrlA, Keys.Ctrl('A'));
        Assert.Throws<ArgumentOutOfRangeException>(() => Keys.Ctrl('1'));
    }
}
