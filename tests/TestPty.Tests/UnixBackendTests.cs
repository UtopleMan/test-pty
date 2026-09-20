using System.Runtime.Versioning;
using Xunit;

namespace TestPty.Tests;

/// <summary>
/// Drives real programs under a real pseudo-terminal. Nothing here is mocked; if forkpty, the exec
/// handover or the reaping is wrong, these fail.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class UnixBackendTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [UnixFact]
    public async Task A_program_that_writes_is_read_back()
    {
        using PtySession session = PtySession.Start(Options("echo", ["hello"]));

        await session.WaitFor("hello", Timeout, TestToken);

        Assert.Equal(0, await session.WaitForExit(Timeout, TestToken));
    }

    [UnixFact]
    public async Task A_program_reading_stdin_receives_what_Send_wrote()
    {
        using PtySession session = PtySession.Start(Options("cat", []));

        session.Send("ping" + Keys.Enter);
        await session.WaitFor("ping", Timeout, TestToken);

        session.Send(Keys.CtrlD);

        Assert.Equal(0, await session.WaitForExit(Timeout, TestToken));
    }

    [UnixFact]
    public async Task The_child_is_told_the_size_it_was_started_with()
    {
        using PtySession session = PtySession.Start(Options("stty", ["size"]) with { Size = (100, 30) });

        await session.WaitFor("30 100", Timeout, TestToken);
    }

    [UnixFact]
    public async Task A_resize_is_visible_to_a_program_that_reads_the_size_again()
    {
        using PtySession session = PtySession.Start(Options("sh", []) with { Size = (100, 30) });

        session.Send("stty size" + Keys.Enter);
        await session.WaitFor("30 100", Timeout, TestToken);

        session.Resize(90, 20);

        session.Send("stty size" + Keys.Enter);
        await session.WaitFor("20 90", Timeout, TestToken);
    }

    [UnixFact]
    public async Task TERM_and_the_environment_overrides_reach_the_child()
    {
        PtyOptions options = Options("sh", ["-c", "printenv TERM; printenv HARNESS_MARKER"]) with
        {
            Environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["HARNESS_MARKER"] = "present" },
        };

        using PtySession session = PtySession.Start(options);

        await session.WaitFor("xterm-256color", Timeout, TestToken);
        await session.WaitFor("present", Timeout, TestToken);
    }

    [UnixFact]
    public async Task The_working_directory_reaches_the_child()
    {
        string directory = Directory.CreateTempSubdirectory("test-pty-cwd").FullName;

        try
        {
            using PtySession session = PtySession.Start(Options("pwd", []) with { WorkingDirectory = directory });

            await session.WaitFor(Path.GetFileName(directory), Timeout, TestToken);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [UnixFact]
    public void A_program_that_does_not_exist_is_an_error_rather_than_a_hang()
    {
        PtyStartException failure = Assert.Throws<PtyStartException>(
            () => PtySession.Start(Options("no-such-program-cf18", [])));

        Assert.Contains("no-such-program-cf18", failure.Message, StringComparison.Ordinal);
    }

    [UnixFact]
    public void A_file_that_is_not_an_executable_image_is_an_error_rather_than_a_hang()
    {
        string path = Path.Combine(Path.GetTempPath(), $"test-pty-not-an-image-{Guid.NewGuid():N}");
        File.WriteAllText(path, "this is not a program\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            PtyStartException failure = Assert.Throws<PtyStartException>(() => PtySession.Start(Options(path, [])));

            Assert.Contains("errno", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [UnixFact]
    public void A_program_that_never_exits_is_killed_and_its_status_observed()
    {
        using var backend = new UnixPtyBackend();
        backend.Start(Options("cat", []));

        Assert.True(backend.IsRunning);

        backend.Kill();

        Assert.True(backend.WaitForExit(Timeout));
        Assert.False(backend.IsRunning);
        Assert.Equal(128 + 15, backend.ExitCode);
    }

    [UnixFact]
    public void Dispose_is_safe_twice_and_after_the_child_has_exited()
    {
        var backend = new UnixPtyBackend();
        backend.Start(Options("true", []));

        Assert.True(backend.WaitForExit(Timeout));

        backend.Dispose();
        backend.Dispose();
    }

    [UnixFact]
    public void Two_hundred_sessions_do_not_leak_descriptors()
    {
        const int runs = 200;

        RunAndDiscard();
        int before = OpenDescriptorCount();

        for (int run = 0; run < runs; run++)
        {
            RunAndDiscard();
        }

        int after = OpenDescriptorCount();

        Assert.True(
            after - before <= 2,
            $"Open descriptors grew from {before} to {after} over {runs} sessions.");
    }

    private static void RunAndDiscard()
    {
        using var backend = new UnixPtyBackend();
        backend.Start(Options("true", []));
        backend.WaitForExit(Timeout);
    }

    private static int OpenDescriptorCount() => Directory.GetFileSystemEntries("/dev/fd").Length;

    private static PtyOptions Options(string program, IReadOnlyList<string> arguments) =>
        new(program, arguments) { DefaultTimeout = Timeout };
}
