# test-pty

Drive a terminal program from a C# test: give it a pseudo-terminal, send it keys, and assert on
what it drew.

Two things separate this from the shell-script and Python drivers it replaces. It **synchronises on
output rather than on sleeps** — every wait is for text to appear or the screen to settle, with a
timeout — so a test cannot pass or fail because a machine was busy. And it **interprets the escape
sequences into a virtual screen**, so a test asserts that a cell says what it should rather than
matching a byte stream with cursor moves embedded in it.

```csharp
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
```

That example is not illustrative: it is the body of `RealProgramTests.The_readme_example_works`, so
it is compiled and run by the suite. If you change one, change the other.

Unix uses `forkpty`; Windows uses ConPTY. The same API either way.

Test-only. Nothing here ships inside a product.

## What you get

| | |
|---|---|
| `PtySession` | The façade. `Send`, `WaitFor`, `WaitForScreen`, `WaitForIdle`, `WaitForExit`, `Resize`, `Screen`, `PlainText`, `RawCapture`, `SaveCapture`. |
| `Screen` | A grid of cells with a cursor. `Row`, `Cell`, `ToText`, `CursorRow`, `CursorColumn`, plus `IsCursorVisible`, `IsAlternateScreenActive` and `UnhandledSequences`. |
| `Keys` | `CtrlC`, `Enter`, `Up`, `Home`, `Delete` and the rest, written once rather than in every consumer's tests. |
| `PtyOptions` | Program and arguments, working directory, environment, size, `TERM`, the default timeout, and where to write a capture when a wait fails. |
| `PtyTimeoutException` | What was waited for, for how long, and the screen as it stood. A timeout that does not show the screen wastes the failure. |

Both waits and the screen are deliberately shallow: `WaitFor` searches the decoded byte stream, and
`Screen` answers what was drawn. Use the stream for a shell, the screen for a TUI.

## Status

Unix works and is under test. The Windows backend is written but **has never been executed** — see
`plans/pty-test-harness.md`, which is the source of truth for what is done and what is not, and
`scripts/verify-windows.ps1`, which is how someone on Windows finds out.

## Used by

- [sharp-shell](https://github.com/UtopleMan/sharp-shell) — the interactive line editor
- duetui — the Terminal.Gui front end, replacing `non-ai/support/pty-drive.py`
