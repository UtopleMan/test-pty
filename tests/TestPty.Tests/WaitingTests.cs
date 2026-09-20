using System.Text.RegularExpressions;
using Xunit;

namespace TestPty.Tests;

/// <summary>
/// The waits, against a scripted backend. Nothing here sleeps, and nothing here is timing-sensitive
/// beyond the quiet period it is deliberately measuring.
/// </summary>
public sealed class WaitingTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(5);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Text_arriving_in_several_chunks_is_still_matched()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        Task waiting = session.WaitFor("needle", GenerousTimeout, TestToken);

        backend.Emit("nee");
        backend.Emit("d");
        backend.Emit("le");

        await waiting;
    }

    [Fact]
    public async Task A_pattern_spanning_a_chunk_boundary_matches()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        Task waiting = session.WaitFor(new Regex(@"item-\d+ ready"), GenerousTimeout, TestToken);

        backend.Emit("item-");
        backend.Emit("42 re");
        backend.Emit("ady");

        await waiting;
    }

    [Fact]
    public async Task A_pattern_wait_advances_past_what_it_matched()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        backend.Emit("$ ");
        await session.WaitFor(new Regex(@"\$ "), GenerousTimeout, TestToken);

        Task second = session.WaitFor(new Regex(@"\$ "), GenerousTimeout, TestToken);
        Assert.False(second.IsCompleted);

        backend.Emit("$ ");
        await second;
    }

    [Fact]
    public async Task A_wait_that_cannot_be_satisfied_throws_with_the_screen_in_the_message()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        backend.Emit("drawn on the screen");
        await session.WaitFor("screen", GenerousTimeout, TestToken);

        PtyTimeoutException failure = await Assert.ThrowsAsync<PtyTimeoutException>(
            () => session.WaitFor("never", ShortTimeout, TestToken));

        Assert.Contains("drawn on the screen", failure.ScreenText, StringComparison.Ordinal);
        Assert.Contains("drawn on the screen", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_screen_wait_completes_when_the_grid_says_so()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        Task waiting = session.WaitForScreen(
            screen => screen.CursorColumn == 4,
            "the cursor to reach column 4",
            GenerousTimeout,
            TestToken);

        backend.Emit("ab");
        backend.Emit("cd");

        await waiting;
        Assert.Equal(4, session.Screen.CursorColumn);
    }

    [Fact]
    public async Task A_screen_wait_that_already_holds_returns_at_once()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        await session.WaitForScreen(screen => screen.CursorRow == 0, timeout: GenerousTimeout, cancellationToken: TestToken);
    }

    [Fact]
    public async Task A_screen_wait_that_never_holds_names_what_it_waited_for()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        PtyTimeoutException failure = await Assert.ThrowsAsync<PtyTimeoutException>(
            () => session.WaitForScreen(
                screen => screen.CursorRow == 3,
                "the cursor to reach the fourth row",
                ShortTimeout,
                TestToken));

        Assert.Equal("the cursor to reach the fourth row", failure.WaitedFor);
    }

    [Fact]
    public async Task WaitForIdle_returns_after_the_quiet_period_and_not_before()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        using (var noise = new OutputNoise(backend, TimeSpan.FromMilliseconds(20)))
        {
            await Assert.ThrowsAsync<PtyTimeoutException>(
                () => session.WaitForIdle(TimeSpan.FromMilliseconds(200), ShortTimeout, TestToken));
        }

        await session.WaitForIdle(TimeSpan.FromMilliseconds(50), GenerousTimeout, TestToken);
    }

    [Fact]
    public async Task A_wait_honours_the_caller_cancellation_token()
    {
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);
        using var cancellation = new CancellationTokenSource();

        Task waiting = session.WaitFor("never", GenerousTimeout, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public async Task The_default_timeout_comes_from_the_options()
    {
        var backend = new FakePtyBackend();
        using PtySession session = PtySession.Start(backend, new PtyOptions("fake") { DefaultTimeout = ShortTimeout });

        PtyTimeoutException failure = await Assert.ThrowsAsync<PtyTimeoutException>(
            () => session.WaitFor("never", cancellationToken: TestToken));

        Assert.Equal(ShortTimeout, failure.Timeout);
    }

    [Fact]
    public async Task A_failure_writes_the_capture_where_the_test_asked_for_it()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"test-pty-capture-{Guid.NewGuid():N}");
        var backend = new FakePtyBackend();

        using PtySession session = PtySession.Start(
            backend,
            new PtyOptions("fake") { DefaultTimeout = ShortTimeout, CaptureDirectory = directory });

        try
        {
            backend.Emit("some output");
            await session.WaitFor("some output", GenerousTimeout, TestToken);

            PtyTimeoutException failure = await Assert.ThrowsAsync<PtyTimeoutException>(
                () => session.WaitFor("never", ShortTimeout, TestToken));

            Assert.Equal(directory, failure.CapturePath);
            Assert.Equal("some output", await File.ReadAllTextAsync(Path.Combine(directory, "capture.raw"), TestToken));

            string readable = await File.ReadAllTextAsync(Path.Combine(directory, "capture.txt"), TestToken);
            Assert.Contains("--- screen ---", readable, StringComparison.Ordinal);
            Assert.Contains("--- stream ---", readable, StringComparison.Ordinal);
            Assert.Contains("some output", readable, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveCapture_writes_both_files_on_demand()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"test-pty-capture-{Guid.NewGuid():N}");
        var backend = new FakePtyBackend();
        using PtySession session = Start(backend);

        try
        {
            session.SaveCapture(directory);

            Assert.True(File.Exists(Path.Combine(directory, "capture.raw")));
            Assert.True(File.Exists(Path.Combine(directory, "capture.txt")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PtySession Start(FakePtyBackend backend) =>
        PtySession.Start(backend, new PtyOptions("fake") { Size = (20, 4), DefaultTimeout = ShortTimeout });
}
