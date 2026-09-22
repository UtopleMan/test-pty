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

Unix only. The child runs under `forkpty`, with a real controlling terminal, so a `Ctrl-C` you send
arrives as a signal rather than as a byte nobody acts on.

Test-only. Nothing here ships inside a product.

## What you get

| | |
|---|---|
| `PtySession` | The façade. `Send`, `WaitFor`, `WaitForScreen`, `WaitForIdle`, `WaitForExit`, `ClickAt`, `Resize`, `Screen`, `PlainText`, `RawCapture`, `SaveCapture`. |
| `Screen` | A grid of cells with a cursor. `Row`, `Cell`, `ToText`, `CursorRow`, `CursorColumn`, plus `IsCursorVisible`, `IsAlternateScreenActive` and `UnhandledSequences`. `Find`, `FindAll` and `FindBox` answer *where* something is, so a test need not hard-code a row the layout will move. |
| `Keys` | `CtrlC`, `Enter`, `Up`, `Home`, `Delete` and the rest, written once rather than in every consumer's tests. |
| `Mouse` | SGR (1006) reports: `Press`, `Release`, `Click`, `Wheel`, `Move`, in zero-based screen coordinates. |
| `CellColor` | The colour a cell carries, as the terminal expressed it — named, 256-indexed or 24-bit — with `ToRgb()` so an index and a truecolor value can be compared. |
| `ScreenPosition`, `ScreenRegion`, `BoxGlyphs` | Where something is, how big a box is, and which corners drew it (`Rounded`, `Single`, `Double`). |
| `PtyOptions` | Program and arguments, working directory, environment, size, `TERM`, the default timeout, and where to write a capture when a wait fails. |
| `PtyTimeoutException` | What was waited for, for how long, and the screen as it stood. A timeout that does not show the screen wastes the failure. |

Both waits and the screen are deliberately shallow: `WaitFor` searches the decoded byte stream, and
`Screen` answers what was drawn. Use the stream for a shell, the screen for a TUI.

`CellAttributes` carries both vocabularies. `ForegroundColor` and `BackgroundColor` are `CellColor`
and answer every case; `Foreground` and `Background` are the same colours seen through the sixteen a
terminal names, and read `TerminalColor.Default` for a cell painted with an index or a truecolor
value, which is not one of those sixteen.

## Status

Working and under test on **macOS arm64** and **Ubuntu 24.04 aarch64**: 119 cases, nothing skipped.

Until the fork/JIT fix described here, that claim was only ever true of Linux. On macOS the suite did not
fail, it **hung**: `LibC.Errno` resolved both `__error` and `__errno_location` while its one body was
compiled, which throws on whichever platform lacks one; and `BindChildCalls` bound each entry point
by calling it *directly*, which compiles the call into `BindChildCalls` and leaves the callee's
standalone stub — the one the forked child reaches through a function pointer — unbuilt, so the
child was the first to need it and deadlocked on the JIT's lock. Both are fixed, and the fix is why
the platform claim above is now worth making.

**There is no Windows support.** A ConPTY backend was written and then removed rather than shipped
without ever having been executed; `PtyBackends.Create()` says so if you try. The reasoning, and what
bringing it back would cost, is in Phase 3 of `plans/pty-test-harness.md` — which remains the source
of truth for what is done and what is not.

## Used by

- [sharp-shell](https://github.com/UtopleMan/sharp-shell) — the interactive line editor
- duetui — the Terminal.Gui front end, replacing `non-ai/support/pty-drive.py`
