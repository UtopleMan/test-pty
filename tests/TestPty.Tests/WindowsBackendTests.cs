using System.Runtime.Versioning;
using Xunit;

namespace TestPty.Tests;

/// <summary>
/// The same behaviours <see cref="UnixBackendTests"/> proves, over ConPTY. Every case skips on any
/// other platform with a reason that names the one it needs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsBackendTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [WindowsFact]
    public async Task A_program_that_writes_is_read_back()
    {
        using PtySession session = PtySession.Start(Options("cmd.exe", ["/c", "echo hello"]));

        await session.WaitFor("hello", Timeout, TestToken);

        Assert.Equal(0, await session.WaitForExit(Timeout, TestToken));
    }

    [WindowsFact]
    public async Task A_program_reading_stdin_receives_what_Send_wrote()
    {
        using PtySession session = PtySession.Start(Options("cmd.exe", []));

        session.Send("echo ping" + Keys.Enter);
        await session.WaitFor("ping", Timeout, TestToken);

        session.Send("exit" + Keys.Enter);

        Assert.Equal(0, await session.WaitForExit(Timeout, TestToken));
    }

    [WindowsFact]
    public async Task The_child_is_told_the_size_it_was_started_with()
    {
        using PtySession session = PtySession.Start(
            Options("powershell.exe", ["-NoProfile", "-Command", "$Host.UI.RawUI.WindowSize.Width"]) with
            {
                Size = (100, 30),
            });

        await session.WaitFor("100", Timeout, TestToken);
    }

    [WindowsFact]
    public async Task A_resize_is_visible_to_a_program_that_reads_the_size_again()
    {
        using PtySession session = PtySession.Start(
            Options("powershell.exe", ["-NoProfile", "-NoLogo"]) with { Size = (100, 30) });

        session.Send("$Host.UI.RawUI.WindowSize.Width" + Keys.Enter);
        await session.WaitFor("100", Timeout, TestToken);

        session.Resize(90, 20);

        session.Send("$Host.UI.RawUI.WindowSize.Width" + Keys.Enter);
        await session.WaitFor("90", Timeout, TestToken);
    }

    [WindowsFact]
    public async Task The_environment_overrides_reach_the_child()
    {
        PtyOptions options = Options("cmd.exe", ["/c", "echo %HARNESS_MARKER%"]) with
        {
            Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["HARNESS_MARKER"] = "present" },
        };

        using PtySession session = PtySession.Start(options);

        await session.WaitFor("present", Timeout, TestToken);
    }

    [WindowsFact]
    public async Task The_working_directory_reaches_the_child()
    {
        string directory = Directory.CreateTempSubdirectory("test-pty-cwd").FullName;

        try
        {
            using PtySession session = PtySession.Start(
                Options("cmd.exe", ["/c", "cd"]) with { WorkingDirectory = directory });

            await session.WaitFor(Path.GetFileName(directory), Timeout, TestToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [WindowsFact]
    public void A_program_that_does_not_exist_is_an_error_rather_than_a_hang()
    {
        PtyStartException failure = Assert.Throws<PtyStartException>(
            () => PtySession.Start(Options("no-such-program-cf18.exe", [])));

        Assert.Contains("no-such-program-cf18.exe", failure.Message, StringComparison.Ordinal);
    }

    [WindowsFact]
    public void A_program_that_never_exits_is_killed_and_its_status_observed()
    {
        using var backend = new WindowsPtyBackend();
        backend.Start(Options("cmd.exe", []));

        Assert.True(backend.IsRunning);

        backend.Kill();

        Assert.True(backend.WaitForExit(Timeout));
        Assert.False(backend.IsRunning);
        Assert.NotNull(backend.ExitCode);
    }

    [WindowsFact]
    public void Dispose_is_safe_twice_and_after_the_child_has_exited()
    {
        var backend = new WindowsPtyBackend();
        backend.Start(Options("cmd.exe", ["/c", "exit 0"]));

        Assert.True(backend.WaitForExit(Timeout));

        backend.Dispose();
        backend.Dispose();
    }

    [WindowsFact]
    public void Two_hundred_sessions_do_not_leak_handles()
    {
        const int runs = 200;

        RunAndDiscard();
        long before = OpenHandleCount();

        for (int run = 0; run < runs; run++)
        {
            RunAndDiscard();
        }

        long after = OpenHandleCount();

        Assert.True(
            after - before <= 50,
            $"Open handles grew from {before} to {after} over {runs} sessions.");
    }

    private static void RunAndDiscard()
    {
        using var backend = new WindowsPtyBackend();
        backend.Start(Options("cmd.exe", ["/c", "exit 0"]));
        backend.WaitForExit(Timeout);
    }

    private static long OpenHandleCount() => System.Diagnostics.Process.GetCurrentProcess().HandleCount;

    private static PtyOptions Options(string program, IReadOnlyList<string> arguments) =>
        new(program, arguments) { DefaultTimeout = Timeout };
}
