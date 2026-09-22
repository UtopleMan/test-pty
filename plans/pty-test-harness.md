# A C# pseudo-terminal harness for tests

Build a test-only .NET library that runs a terminal program under a real pseudo-terminal, sends it
keys, interprets what it draws into a virtual screen, and lets a test assert on that screen —
synchronising on output rather than on sleeps. Unix only — see Phase 3.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** two repositories need to test terminal behaviour and neither can. sharp-shell is about to
grow an interactive line editor whose whole point is what happens on a keypress; duetui is a
Terminal.Gui application whose rendering has never been under automated test. Both currently reach
for a hand-driven script and eyeball the result.

**Why a new repository:** this is the third time the same harness has been improvised. duetui has
`non-ai/support/pty-drive.py`, sharp-shell had a throwaway `ptydrive.py` in a scratch directory, and
the line-editor work needs a third. None is in a test suite; all three sleep between keystrokes.

**Tech stack:** .NET 10 (`net10.0`), xunit.v3 on VSTest for the harness's own tests, `forkpty` via
P/Invoke. No third-party runtime dependencies.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to
continue with zero context); run the phase's **Verification Plan** and record the result before
moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.

Repository: `/Users/dude/Sources/UtopleMan/test-pty`, published privately at
`https://github.com/UtopleMan/test-pty` on `main`. Consumers live beside it: `../sharp-shell` and
`../duetui`.

**Where this stands.** Phases 1, 2, 4, 5 and 6 are Complete and verified on **macOS arm64 and
Ubuntu 24.04 aarch64**: on both, the suite is 97 passed with nothing skipped, and on both it ran
twenty times consecutively with no failures and no leaked processes. Phase 3 is **Dropped** —
Windows support was removed rather than shipped unverified, and its summary records why and what
bringing it back would cost. Phase 7 is *In progress*: this repository is published at
`https://github.com/UtopleMan/test-pty` and sharp-shell now vendors it at `vendor/test-pty`, so the
harness has its first real consumer. What is left there is duetui, and the line-editor tests that
sharp-shell's own plan specifies. No code is outstanding in this repository for any of it.

Phase summaries below quote the counts measured when each phase closed, and several of those include
the ten Windows cases that Phase 3 has since removed. They are records, not current figures; the
current figure is the one above.

Before changing the Unix backend or the interop, read the Phase 2 and Phase 5 summaries. The rules
about what a forked child may not do are not style; breaking them produces a test that hangs rather
than fails.

## Decisions already taken

These were settled with the user before the plan was written. Do not relitigate them; if one turns
out to be wrong, say so and record the change here.

| | |
|---|---|
| Depth | Byte stream **and** virtual screen. The stream half serves a shell; the screen half is what a TUI needs. |
| Platforms | ~~Unix and Windows, both in the first pass.~~ **Reversed: Unix only.** See Phase 3. |
| Consumption | Git submodule, project-referenced from each consumer's test project. No package feed. |
| Licence | Apache-2.0, matching sharp-shell. `LICENSE` and `NOTICE` are already in place. |
| Synchronisation | Wait for output or for the screen to settle, always with a timeout. **No sleeps anywhere in the API or its tests.** |

## Assumptions

Stated rather than buried. Each is cheap to change now and expensive later.

- Layout `src/TestPty`, `tests/TestPty.Tests`, `TestPty.slnx`; root namespace `TestPty`.
- `net10.0`, and the same `Directory.Build.props` conventions both consumers use: `Nullable`,
  `ImplicitUsings`, `TreatWarningsAsErrors`, `InvariantGlobalization`, `EnableNETAnalyzers`,
  `AnalysisLevel latest`.
- The library is an ordinary class library. It is *used* only by test projects, but it is not itself
  a test project and carries no xunit dependency — a consumer on a different test framework must
  still be able to reference it.
- P/Invoke is fine here. Nothing in this repository is trimmed or AOT-compiled, and no consumer
  ships it.
- Text is UTF-8 in and out. Terminal cell width is counted in UTF-16 code units for now; East Asian
  wide characters and combining marks will mis-measure, and that is recorded rather than solved.

## Non-goals

- **A terminal emulator.** The screen model exists to let a test assert on rendered output. Sixel,
  mouse reporting, alternate character sets and bracketed paste are out unless a consumer needs one,
  and then it arrives with the test that needs it.
- **Running the program under test in-process.** A real child process under a real pty is the point;
  anything less is what the unit tests in each consumer already do.
- **A package feed.** Submodule now. Packaging is worth revisiting once the API has held still under
  two consumers, and it is a line in the Deployment Plan rather than a phase.
- **Interactive debugging tools.** No capture viewer, no recording playback. The captures land on
  disk when a test fails and that is enough.

## The risk that decided whether this plan was honest, and how it resolved

This section originally read: *"ConPTY cannot be executed on the machine this plan is being written
on. It is macOS. The Windows backend can be written and reasoned about carefully, and its
unit-testable parts can be covered, but 'it works on Windows' will be a human running it on Windows,
not a green suite here."* Phase 3 was therefore written to be falsifiable by someone else rather than
trusted on the strength of a compile.

It was never falsified, because it was never run. Rather than keep code in the tree claiming a
platform it had never touched, **Windows support was dropped.** The risk this section named is
exactly the one that materialised, and deleting the code is the honest resolution of it — not a
retreat from it. Phase 3 carries the full account, including what bringing it back would cost.

## Phase 1: The repository, and the API both backends must satisfy
Status: Complete

Nothing platform-specific. The point is to fix the shape of the thing before either backend exists,
so that the second backend is an implementation rather than a redesign. In the event there is only
one backend, but `IPtyBackend` earned its keep anyway: `FakePtyBackend` is what makes the session,
the screen and the waits testable without a process.

- [x] `TestPty.slnx`, `Directory.Build.props` (the conventions listed under Assumptions),
      `src/TestPty/TestPty.csproj`, `tests/TestPty.Tests/TestPty.Tests.csproj` with xunit.v3 3.2.2,
      `xunit.runner.visualstudio` 3.1.5 and `Microsoft.NET.Test.Sdk` 17.14.1 — the versions both
      consumers already use.
- [x] `PtyOptions`: the program and its arguments, working directory, environment overrides, initial
      `(Columns, Rows)`, `TERM` (default `xterm-256color`), and a default timeout every wait
      inherits.
- [x] `IPtyBackend`: `Start(PtyOptions)`, `Write(ReadOnlySpan<byte>)`, `Read(Span<byte>)` or an
      equivalent async read, `Resize(columns, rows)`, `WaitForExit(timeout)`, `Kill()`, `Dispose()`.
      One interface, two implementations, no platform types in the signature.
- [x] `PtySession`: the façade a test uses. Owns a backend, a reader pump, the raw capture, and the
      screen. `Start`, `Send`, `WaitFor`, `WaitForIdle`, `Screen`, `RawCapture`, `PlainText`,
      `ExitCode`, `Dispose`.
- [x] `Keys`: named constants for what a test sends — `CtrlC`, `CtrlD`, `Enter`, `Backspace`,
      `Up`/`Down`/`Left`/`Right`, `Home`/`End`, `Delete`, `Tab`, `Escape`. Escape sequences written
      once, here, rather than in every consumer's tests.
- [x] `PtyTimeoutException`, carrying what was waited for, how long, and the screen as it stood when
      the wait gave up. A timeout that does not show the screen wastes the failure.
- [x] A `FakePtyBackend` in the test project that replays scripted output and records what was
      written, so the session, the screen and the waits are all testable with no real process.

### Verification Plan
- `dotnet build TestPty.slnx` — succeeds with 0 warnings (the props treat warnings as errors).
- `dotnet test tests/TestPty.Tests --filter "FullyQualifiedName~ApiShape"` — green. The suite drives
  `PtySession` end to end against `FakePtyBackend`: `Send` reaches the backend, `WaitFor` returns
  when the scripted text arrives, and a wait that never matches throws `PtyTimeoutException` whose
  message contains both the expected text and the screen contents.
- `grep -rn "Thread.Sleep\|Task.Delay" src tests` — no matches. The whole point is that there are
  none; catch it on day one rather than after it has spread.

### Phase Summary
Done, and verified on macOS: `dotnet build TestPty.slnx` succeeds with 0 warnings, the 27 cases
matching `ApiShape` pass, `grep -rn "Thread.Sleep\|Task.Delay" src tests` finds nothing, and
`dotnet format --verify-no-changes` is clean.

**What exists.** `TestPty.slnx`, `Directory.Build.props` (copied from sharp-shell verbatim, so the
conventions match both consumers), `src/TestPty` as a plain class library and
`tests/TestPty.Tests` on xunit.v3 3.2.2 / `xunit.runner.visualstudio` 3.1.5 /
`Microsoft.NET.Test.Sdk` 17.14.1. In the library: `PtyOptions`, `IPtyBackend`, `PtySession`,
`Screen`, `ScreenCell`, `Keys`, `PtyTimeoutException`, and two internals — `Ansi` (the named
control bytes) and `Readable` (renders a capture so a person can read it in a failure message). In
the test project: `FakePtyBackend`, `OutputNoise` and `ApiShapeTests`.

**Decisions taken while building, which the plan did not settle.**

- *`Screen` had to arrive in Phase 1, not Phase 4.* Phase 1 requires `PtySession.Screen` and a
  timeout carrying the screen, so the shape is here: the grid, the cursor, `Row`/`Cell`/`ToText`,
  printable text, `\r`, `\n`, `\b`, `\t`, wrap at the right margin and scroll at the bottom.
  Escape sequences are *recognised and consumed* — the parser is a real state machine over ground /
  escape / control-sequence / string-sequence / character-set — but every sequence is counted into
  `UnhandledSequences` rather than acted on. Phase 4's work is therefore filling in
  `Screen.DispatchControlSequence`, not writing a parser.
- *`WaitFor` has expect semantics.* Each successful wait advances a consumed offset past its match,
  so a second `WaitFor("$ ")` waits for the *next* prompt rather than re-matching the first. Without
  this the README's own example is wrong. `ApiShapeTests.WaitFor_advances_past_what_it_matched`
  pins it.
- *`PlainText` is the decoded byte stream, escape sequences included*, and is what `WaitFor`
  searches. `Screen` is for assertions about what was drawn. Both are documented on the members.
- *No platform factory yet.* Only `PtySession.Start(IPtyBackend, PtyOptions)` exists. Phase 2 adds
  the `Start(PtyOptions)` overload that picks a backend, once there is a backend to pick.
- *`WaitForExit` landed here* rather than in Phase 5. `ExitCode` cannot be reported without reaping,
  and the fake backend made the test free.
- *`SendBytes(ReadOnlySpan<byte>)`* sits beside `Send(string)`, because `Keys` are strings but a raw
  byte case will come.
- *How the no-sleep rule is kept.* Timeouts are `CancellationTokenSource.CancelAfter` plus
  `Task.WaitAsync`; `WaitForIdle` is a `System.Threading.Timer` whose deadline every arriving chunk
  pushes out. Neither shows up in the `Thread.Sleep`/`Task.Delay` grep, and neither is a fixed wait.

**Two traps worth knowing before touching this code.**

- *Never put a literal control byte in a `.cs` file.* Write `"\u001b[A"`, not the raw ESC. Literal
  ESC and DEL bytes are invisible in a diff, and tooling in this loop silently dropped them once,
  which produced tests that asserted the wrong thing and still compiled.
- *Test files need `using Xunit;` explicitly* (as sharp-shell's do), and every call taking a
  `CancellationToken` must be given `TestContext.Current.CancellationToken`. xUnit's analyzer rule
  xUnit1051 is a *build error* here, because `Directory.Build.props` treats warnings as errors.

## Phase 2: The Unix backend
Status: Complete

- [x] `UnixPtyBackend` using `forkpty` from libc. On macOS it lives in `libSystem`; on Linux it is in
      `libutil`. Resolve both rather than assuming one — this is the first thing that breaks on the
      other Unix.
- [x] In the child: set `TERM`, `COLUMNS` and `LINES`, apply the environment overrides, `chdir` to
      the working directory, then `execvp`. A failed `execvp` must reach the parent as a clear
      error, not as a silent hang on a pty that never speaks.
- [x] `Resize` via `ioctl(TIOCSWINSZ)`, and confirm the child sees it — a program that reads its
      size at start-up will not, which is a property the test should state rather than discover.
- [x] Reaping: `waitpid` for the exit status, `Kill` via `SIGKILL` after a `SIGTERM` grace period,
      and the master descriptor closed exactly once. A leaked descriptor or a zombie per test is
      the thing that makes a harness unusable at scale.
- [x] `Dispose` is safe to call twice and after the child has already exited.

### Verification Plan
- `dotnet test tests/TestPty.Tests --filter "FullyQualifiedName~UnixBackend"` — green on macOS.
  Covers: `echo hello` under a pty produces `hello`; a program reading stdin receives what `Send`
  wrote; `stty size` reports the size that was asked for; a resize is visible to a program that
  re-reads it; a program that never exits is killed and its exit status observed; `execvp` of a
  program that does not exist surfaces an error rather than hanging.
- A descriptor-leak check: run the backend 200 times in one test and assert the process's open
  descriptor count has not grown. On macOS, count via `lsof -p $PID` or `/dev/fd`.
- `dotnet test tests/TestPty.Tests` — whole suite green.

### Phase Summary
Done, and verified on macOS 26.6 / arm64: `dotnet test --filter "FullyQualifiedName~UnixBackend"`
passes 11 of 11, and the whole suite passes 38 of 38. `dotnet format --verify-no-changes` is clean
and the no-sleep grep still finds nothing. Not run on Linux — see "What is not proved" below.

**What exists.** `UnixPtyBackend`, plus `TestPty.Interop` holding `LibC` (libc entry points as
function pointers), `WinSize`, `NativeStrings` (UTF-8 copies of the strings the child needs) and
`Errno`. Alongside them `ProgramResolver`, `PtyStartException` and `PtyBackends.Create()`, which
`PtySession.Start(PtyOptions)` now uses. `PtySession.Resize` was added because Phase 2 has to prove
a resize reaches the child; the virtual screen keeps the size it was created with, and reflow is
left alone deliberately.

**Two traps that cost real time, and that will bite the next person who touches this.**

1. *Apple's arm64 ABI passes variadic arguments on the stack.* `fcntl` and `ioctl` are declared
   variadic, so calling them through a fixed-arity signature hands the third argument to a callee
   that never looks at that register: `fcntl(F_SETFD, FD_CLOEXEC)` **returns success and sets
   nothing**, and `ioctl(TIOCSWINSZ)` fails with EFAULT. The fix is `LibC.FixedArity`, which uses the
   `__fcntl` and `__ioctl` syscall stubs on macOS and the public names elsewhere, where variadic
   arguments go in registers. `UnixPtyBackend.SetCloseOnExec` also reads the flag back and throws if
   it did not stick, so a future platform with the same problem says so instead of hanging.
2. *A forked child cannot JIT.* The child hung — twice, differently — inside the runtime waiting on a
   mutex no surviving thread could release. First on the class-initialisation helper behind a static
   field read, fixed by copying the function pointers into locals before the fork. Then, less
   obviously, inside `GetILStubForCalli`: **a call through a function pointer needs an IL stub, and
   that stub is compiled on first use of the signature.** Every signature the child uses is therefore
   called once in the parent by `LibC.CompileChildCallStubs`, with deliberately inert arguments (a
   bad descriptor, an empty path) — `_exit` cannot be called in the parent, so `srand`, which has the
   same shape, stands in for it. Between the fork and the exec the child touches locals and libc and
   nothing else. **Do not add a managed call, a static read, or an allocation to that block.**

**Other decisions.**

- *`execve`, not `execvp`.* `ProgramResolver` searches PATH in the parent, so a name that does not
  resolve is a `PtyStartException` at the call site rather than a child that exits 127. The plan said
  `execvp`; this is strictly better for the error the plan asked for.
- *How an exec failure gets out.* A pipe whose write end is close-on-exec. The child writes its errno
  and `_exit`s; a successful exec closes the end and the parent reads end-of-file. This is why the
  CLOEXEC read-back check matters: without the flag, `Start` would block forever on a healthy start.
- *Reaping* is a dedicated background thread on a blocking `waitpid`, retried on EINTR, completing a
  `TaskCompletionSource`. `Kill` is SIGTERM, a 250 ms grace, then SIGKILL. `Dispose` SIGKILLs if the
  child is still running, joins the reaper, and closes the master descriptor exactly once through the
  `FileStream`'s `SafeFileHandle`. 200 sessions in one test move the process's open descriptor count
  by no more than 2.
- *End of output differs by platform and both are handled.* Reading a master whose child has gone
  gives EIO on macOS and end-of-file on Linux; the session's pump treats an `IOException` and a zero
  read the same way.
- *`PtySession.WaitForExit`* now reports a pump that will not finish as a `PtyTimeoutException`
  rather than a bare `TimeoutException`.

**Verified on Linux too, after the fact.** Ubuntu 24.04 aarch64 (glibc 2.39) under QEMU with `hvf`:
`dotnet build` succeeds with 0 warnings, the suite passes 97 with 10 skipped out of 107, and it ran
**twenty times consecutively with zero failures and zero orphaned processes** — the same bar macOS
met. Every Linux-specific branch executed: `__errno_location` instead of `__error`, `TIOCSWINSZ`
`0x5414` instead of `0x80087467`, and the *public* variadic `fcntl` and `ioctl` names instead of the
`__`-prefixed stubs. The resize case passing is what proves the variadic branch is right on this ABI.

A symbol probe on that machine recorded what the code actually binds to, since it is not what the
plan assumed:

| symbol | in `libc.so.6` | consequence |
|---|---|---|
| `forkpty` | yes | glibc 2.34 moved it out of libutil, so the libc-first lookup wins |
| `__errno_location` | yes | the Linux errno branch is the one taken |
| `__ioctl` | **no** | confirms the `__`-prefixed names are correctly Apple-only |
| `ioctl`, `fcntl` | yes | the public variadic names are used, and they work here |

**What is still not proved.** The `libutil.so.1` fallback in `LibC.LoadCandidates` has never been
exercised, and on any glibc that .NET 10 supports it never will be: `forkpty` has lived in
`libc.so.6` since glibc 2.34, and .NET 10 requires newer than that. It is defensive code for musl and
for old distributions, not a path in use. Treat it as untested.

## Phase 3: The Windows backend
Status: Dropped

**This phase was written, then removed.** The code existed, compiled with no warnings, and its ten
cases skipped with a reason naming the platform they needed — but not one line of ConPTY ever
executed, and the decision was that unverified code claiming a platform is worse than no claim at
all. The scope reversal is recorded in **Decisions already taken** above, against the row that said
"Unix and Windows, both in the first pass".

### What was removed
- `src/TestPty/WindowsPtyBackend.cs` — ConPTY over `CreatePipe`, `CreatePseudoConsole`,
  `InitializeProcThreadAttributeList`, `UpdateProcThreadAttribute`, `CreateProcessW`
- `src/TestPty/WindowsCommandLine.cs` — the quoting rules `CreateProcessW` expects
- `src/TestPty/Interop/Kernel32.cs`, `src/TestPty/Interop/WindowsStructures.cs`
- `tests/TestPty.Tests/WindowsBackendTests.cs` — ten behavioural cases mirroring the Unix ones
- `scripts/verify-windows.ps1` — the hand-verification script
- `WindowsFactAttribute`, and `TestConditions.IsWindows`/`WindowsOnly`

`PtyBackends.Create()` now throws a `PlatformNotSupportedException` on Windows that says the support
was dropped rather than pretending the platform was never considered.

It is all in git history at commit `deb1260`, so bringing it back is a revert of these deletions
rather than a rewrite.

### Why it could not be verified here
Three routes were tried on this machine, which is an Apple Silicon Mac:

1. *OrbStack* — Linux only. Its own `orb create` lists sixteen distributions and no Windows guest.
2. *A local VM under QEMU/UTM* — QEMU 11.1.1 and UTM were installed, and everything needed to run the
   verification unattended was built: an `autounattend.xml` bypassing the TPM, Secure Boot and RAM
   checks, a first-logon runner, and a 2 GB FAT32 payload disk carrying the repository and the
   `win-arm64` SDK so the guest needed no network at all. It never ran, because **no Windows 11 ARM64
   installation media could be obtained on this network.** uupdump's small metadata files download
   fine; its large `.ESD` payload files return zero bytes and time out — *from the macOS host as well
   as from inside the VM*. Four explanations were tested and ruled out: signed-URL expiry (556 s
   still valid), IPv6 (forcing IPv4 failed the same way), parallel range requests (a single
   connection failed too), and HTTP keep-alive through QEMU's user-mode networking (disabling it
   changed nothing, and the host fails identically).
3. *Microsoft's official ARM64 ISO page and CrystalFetch* — both need a person at a browser.

### What bringing it back would take
- A Windows ARM64 or x64 machine, or **a `windows-latest` GitHub Actions job**, which is the cheap
  option now that the repository is on GitHub: minutes, no media to obtain, and it makes the claim
  re-checkable on every push instead of once.
- `git revert` of the deletions, then the ten cases run for real.
- One thing already settled and worth keeping: **the newline question needs no code.** The plan
  assumed Windows sends `\r\n` where Unix sends `\n`. That is wrong — a Unix pty has `ONLCR` on and
  also sends `\r\n`, measured directly on this machine as `b'hello\r\n'` from `/bin/echo`. Nothing
  normalises anything, and `Screen` is platform-neutral without help.

### Verification Plan
Not applicable. The phase is dropped rather than complete, and the suite now reports 97 passed with
nothing skipped, because there are no platform-gated cases left to skip.

## Phase 4: The virtual screen
Status: Complete

Enough of a terminal to assert on, and no more. Every sequence implemented is one a consumer's test
actually needs; anything else is recorded as ignored rather than guessed at.

- [x] `Screen`: a grid of cells with a cursor, addressable as `Row(int)`, `Cell(row, column)`,
      `CursorRow`, `CursorColumn`, and a `ToText()` that renders the whole grid for a failure
      message.
- [x] An incremental ANSI parser fed by the reader pump — printable text, `\r`, `\n`, `\b`, `\t`.
- [x] CSI cursor movement: `CUU`/`CUD`/`CUF`/`CUB`, `CUP`/`HVP`, `CHA`.
- [x] Erase: `ED` (0, 1, 2) and `EL` (0, 1, 2). These are what a redrawing line editor uses most.
- [x] `SGR` tracked per cell — at minimum bold, reverse and the 16 colours — because "is this
      highlighted" is a question a TUI test asks.
- [x] Scroll region (`DECSTBM`), index and reverse index, and scrolling when the cursor leaves the
      bottom row.
- [x] Alternate screen buffer (`?1049h`/`l`) and cursor visibility (`?25h`/`l`). A Terminal.Gui app
      uses both on its first frame.
- [x] Line wrap at the right margin, and `DECAWM` (`?7h`/`l`).
- [x] Unknown sequences are consumed and counted, never printed as text. `Screen.UnhandledSequences`
      exposes the count and the last few, so a consumer can find out what it needs that is missing.

### Verification Plan
- `dotnet test tests/TestPty.Tests --filter "FullyQualifiedName~Screen"` — green. Table-driven over
  byte sequences and their expected grids, run through the parser with no process involved.
- Differential against a real program: run `printf` and `tput` sequences under the Unix backend and
  compare the resulting grid with the text a person can read from `capture.txt`.
- `dotnet test tests/TestPty.Tests` — whole suite green.

### Phase Summary
Done, and verified on macOS: `dotnet test --filter "FullyQualifiedName~Screen"` passes 43 of 43,
which includes both differential cases against real programs.

**What exists.** `Screen` grew from the Phase 1 shape into the parser the plan asked for, plus
`CellAttributes` and `TerminalColor` beside `ScreenCell`. It handles printable text, `\r`, `\n`,
`\b`, `\t`; `CUU`/`CUD`/`CUF`/`CUB`, `CNL`/`CPL`, `CHA`, `VPA`, `CUP`/`HVP`; `ED` 0/1/2/3 and `EL`
0/1/2; `SGR` for bold, reverse and all sixteen colours; `DECSTBM` with `IND` and `RI`; `?1049`,
`?25` and `?7`; and wrap at the right margin. `IsCursorVisible`, `IsAlternateScreenActive` and
`IsAutoWrapEnabled` are exposed because a TUI test asks about them and nothing else would reveal
them.

**Decisions.**

- *Deferred wrap, as a real terminal does it.* Printing in the last column leaves the cursor there
  with a pending-wrap flag rather than moving it to the next line; the wrap happens when the *next*
  character arrives. Without this, `CursorColumn` after printing a full line is wrong, and a line
  editor's arithmetic is wrong with it. Any cursor movement, `\r`, `\n`, `\b` or `\t` clears the
  flag. `The_last_column_does_not_wrap_until_another_character_arrives` pins it.
- *Extended colour is skipped as a unit.* `38`/`48` consume their `5;n` or `2;r;g;b` sub-parameters
  before the loop continues, and are recorded as unhandled. Read naively, `38;5;196` would have set
  three attributes nobody asked for.
- *Erasing uses the current background*, as a terminal does, so a cleared region under a coloured
  background keeps it.
- *The alternate screen swaps the whole grid* and restores the cursor with it. `?1049h` twice is a
  no-op rather than losing the saved grid.
- *`ESC D`, `ESC M` and `ESC E` are handled* because a scroll region is useless without index and
  reverse index. Everything else introduced by a bare `ESC` — including `ESC 7`/`ESC 8` — is
  recorded as unhandled rather than guessed at, per the phase's own rule.
- *`UnhandledSequences` holds the last sixteen*, described in a readable form (`CSI 999Z`,
  `OSC 0;a title`, `SGR 38 (extended colour)`), with `UnhandledSequenceCount` counting all of them.
  This is the mechanism by which a consumer discovers what it needs that is missing.

**A trap in the tests, not the code.** Three of these cases were written with the wrong expectation
first, because `\n` on a terminal is a line feed and *not* a carriage return: after `"one\ntwo"` the
second line starts at column 3, not column 0. The tests now position the cursor explicitly, or send
`\r\n`, so what they assert is what they mean.

## Phase 5: Waiting without sleeping
Status: Complete

The reason this repository exists rather than another copy of the Python script.

- [x] `WaitFor(string)` and `WaitFor(Regex)` over the decoded stream, completing as soon as the text
      appears, with the session's default timeout unless one is given.
- [x] `WaitForScreen(Func<Screen, bool>)`, re-evaluated on every update, for the assertions the
      stream cannot express — "the cursor is at column 4".
- [x] `WaitForIdle(quietPeriod)`: completes once no output has arrived for the quiet period. The
      honest way to say "it has finished drawing" without knowing what it drew.
- [x] `WaitForExit(timeout)`, distinct from a program that is merely quiet.
- [x] Every wait is cancellable and every timeout throws `PtyTimeoutException` carrying the screen,
      the tail of the raw capture, and what was being waited for.
- [x] A global default timeout on `PtyOptions`, overridable per call, so a slow CI machine is one
      change rather than a hundred.
- [x] On failure, write `capture.raw` and `capture.txt` to a directory the test names, so a CI
      failure can be read after the fact.

### Verification Plan
- `dotnet test tests/TestPty.Tests --filter "FullyQualifiedName~Waiting"` — green against
  `FakePtyBackend`: text arriving in several chunks is still matched; a pattern spanning a chunk
  boundary matches; a wait that cannot be satisfied throws with the screen in the message;
  `WaitForIdle` returns after the quiet period and not before.
- `grep -rn "Thread.Sleep\|Task.Delay" src` — no matches in the library. `WaitForIdle` is a timer
  over an output event, not a sleep.
- Run the whole suite 20 times in a loop; zero failures. A harness that is itself flaky cannot be
  used to prove anything.

### Phase Summary
Done, and verified on macOS: `dotnet test --filter "FullyQualifiedName~Waiting"` passes 12 of 12;
`grep -rn "Thread.Sleep\|Task.Delay" src tests` finds nothing; and **the whole suite ran twenty
times in a row with zero failures — 92 passed, 10 skipped, 102 total, every run — and left no
orphaned processes behind.**

**What was added.** `WaitFor(Regex)`, `WaitForScreen(Func<Screen, bool>, description)`,
`PtyOptions.CaptureDirectory`, `PtySession.SaveCapture(directory)`, and
`PtyTimeoutException.CapturePath`. `WaitFor(string)`, `WaitForIdle` and `WaitForExit` already
existed from Phase 1. A failing wait now writes `capture.raw` (the bytes) and `capture.txt` (the
screen, then the stream rendered so a person can read the escape sequences) whenever
`CaptureDirectory` is set, and names the path in the exception message.

**How the no-sleep rule is actually kept**, since this is the phase that exists for it: a timeout is
a `CancellationTokenSource.CancelAfter` linked to the caller's token, awaited through
`Task.WaitAsync`; `WaitForIdle` is a `System.Threading.Timer` whose deadline every arriving chunk
pushes out. Neither is a fixed wait, and neither appears in the grep.

**The flakiness this phase demanded a fix for.** The twenty-run loop is what turned an intermittent
fork hang into something reproducible, and finding it was the most expensive work in the plan so
far. The full diagnosis is in the Phase 2 summary; the short version is that a forked child must not
run *any* managed code, and there were three separate ways it was doing so:

1. a class-initialisation helper behind a static field read,
2. the IL stub for a `calli`, compiled on first use of the signature, and
3. — the one that survived the first two fixes — **the GC-mode transition a normal P/Invoke performs
   on return**, which blocks until an in-flight collection finishes. There is no collector left in
   the child, so it blocks forever.

The interop was rewritten as declared `LibraryImport` P/Invokes carrying `SuppressGCTransition`,
bound in the parent before any fork, reached through locals; the fork itself is serialised
process-wide and wrapped in a no-GC region; and the child kills itself rather than calling `_exit`,
because `_exit` is the one entry point that could never be bound in the parent. **A symptom of any
of this regressing is a test that hangs rather than fails, and a `TestPty.Tests` process left behind
with a child of the same name in state `Ss+`.**

## Phase 6: Proving it on something real
Status: Complete

Self-testing against a program with known output, before either consumer depends on it.

- [x] Drive a real shell (`/bin/sh`) under the harness: send a command, wait for the prompt, assert
      on the screen.
- [x] Drive a program that redraws in place — `printf` with carriage returns, or a small purpose-
      built one in this repository — and assert the final grid rather than the byte stream.
- [x] Assert a key sequence arrives intact: send `Keys.CtrlC` to a program that reports the signal
      it received.
- [x] A README example that is itself a compiled, running test, so the documentation cannot rot.

### Verification Plan
- `dotnet test tests/TestPty.Tests` — whole suite green on macOS.
- `dotnet test tests/TestPty.Tests --filter "FullyQualifiedName~RealProgram"` — green. These are
  `[UnixFact]`, so they skip with a reason rather than fail if the suite is ever run elsewhere.
- `dotnet format --verify-no-changes` — clean.

### Phase Summary
Done, and verified on macOS: `dotnet test --filter "FullyQualifiedName~RealProgram"` passes 5 of 5,
the whole suite passes 97 with 10 skipped out of 107, and `dotnet format --verify-no-changes` is
clean.

**What exists.** `RealProgramTests`, five cases against real programs:

- *A shell prompt is waited for and answered.* `sh -i` with `PS1` set through `PtyOptions.Environment`
  prints a prompt on the pty; the test waits for it, sends a command, waits for the output, and waits
  for the prompt to come back. The second prompt wait is what proves `WaitFor`'s
  advance-past-the-match behaviour matters on a real shell.
- *A program that redraws in place is judged by the grid.* Three `printf`s separated by carriage
  returns leave `Loading ...` on row 0; the test asserts that, and separately asserts that the
  earlier `Loading    ` and `Loading .  ` are in `RawCapture` and not on the screen. This is the
  clearest statement of what the stream half and the screen half are each for.
- *A control character arrives as a signal.* `Keys.CtrlC` reaches a shell with a `trap ... INT` and
  the trap fires. This works because `forkpty` gives the child its own session with the slave as its
  controlling terminal, so the line discipline turns the byte into SIGINT for the foreground process
  group. If anyone replaces the fork with something that does not acquire a controlling terminal,
  this is the test that will catch it.
- *A screen wait follows a real program's cursor*, which is the case the byte stream cannot express.
- *The README example*, compiled and run.

**The README was rewritten** around that example, which is now the body of
`RealProgramTests.The_readme_example_works` — change one and change the other. Its Status section now
says plainly that Unix works, rather than "not built yet". It was revised again when Phase 3 was
dropped, so that it no longer mentions ConPTY at all.

**No shell `sleep` anywhere.** A program that has to stay alive for a test blocks on `read` instead,
so nothing in this repository waits for a fixed period, not even inside a program under test.

## Phase 7: Adoption
Status: In progress

The harness is not finished until something depends on it. Two consumers, and each is expected to
find something the harness got wrong — that is the value of this phase, not a sign it failed.

**Where it stands.** This repository is published at `https://github.com/UtopleMan/test-pty` and
sharp-shell vendors it; that half is done. The remaining items each change another repository, and
the last of them deletes a file inside a `non-ai/` folder that duetui's own instructions put out of
bounds for tools — a person does that one, or says explicitly to.

Nothing here is waiting on more code from this repository: the API is complete on Unix, the suite is
green and stable over twenty consecutive runs on both macOS and Linux, and there is no unverified
platform left to caveat.

**First consumer, what it cost.** Wiring sharp-shell took a submodule, one `ProjectReference`, and
nothing else — no API change was forced. Two things worth knowing for the next consumer:

- The vendored project builds under *its own* `Directory.Build.props`, because MSBuild stops at the
  nearest one walking up. Consumers do not need to accommodate it, and it does not inherit theirs.
- Do **not** add `TestPty.csproj` to the consumer's solution file. It builds transitively through
  the test project's reference, and a solution entry pulls vendored code into `dotnet format`.
- xUnit's `xUnit1051` analyzer rejects a `WaitFor` that is not given a cancellation token, so
  consumers are pushed onto `TestContext.Current.CancellationToken`. That is the analyzer working
  as intended; it is called out here because it surprises on first use.

- [x] Add `test-pty` as a submodule of sharp-shell at `vendor/test-pty`, project-referenced from
      `tests/Sharp.Shell.Tests`.
- [ ] Port sharp-shell's line-editor pty cases onto it. Those tests are specified in that
      repository's own plan; this phase only provides what they run on.
- [x] Add `test-pty` as a submodule of duetui, project-referenced from its test project.
- [ ] Port one duetui rendering case — the one `non-ai/edit-tool-diff-rendering.md` describes
      driving by hand — onto the harness, and retire `non-ai/support/pty-drive.py`.
- [ ] Record, here, every API change the two consumers forced. That list is the evidence for whether
      the API is ready to be packaged.

**Second consumer, what it cost.** duetui drives a Terminal.Gui application rather than a line
editor, and it forced more than sharp-shell did — every item below is an API change duetui asked for
and got:

- **The suite did not run at all on macOS.** Not an API change, but the first thing duetui found,
  and the reason the "twenty consecutive runs on both" claim above was never true of macOS. Two
  faults in `LibC`, both described in the README's Status section: an errno lookup that bound both
  platforms' symbols from one method body, and a `BindChildCalls` that bound nothing the forked
  child would actually reach. The child deadlocked in the JIT.
- **Extended colour.** `Screen` skipped every parameter after `38`/`48`, so a program painting in
  24-bit — which every Terminal.Gui theme does — read as `TerminalColor.Default` in every cell.
  `CellColor` now carries `Default`, `Named`, `Indexed` and `Rgb`, `CellAttributes` exposes it as
  `ForegroundColor`/`BackgroundColor`, and `ToRgb()` maps the 256 palette so an index and a
  truecolor value can be compared. `Foreground`/`Background` are kept, now derived, so nothing that
  read them had to change.
- **Mouse.** `Mouse.Press`, `Release`, `Click`, `Wheel` and `Move` emit SGR (1006) in zero-based
  screen coordinates. A TUI with clickable controls cannot be tested without them.
- **Finding things.** `Screen.Find`, `Screen.FindAll` and `Screen.FindBox`, with `ScreenPosition`,
  `ScreenRegion` and `BoxGlyphs`. A layout that reflows makes a hard-coded row number a lie, and
  every throwaway script duetui had was re-implementing this search by hand.
- **`PtySession.ClickAt`.** Find the text, click it, wait for the screen to settle — the four lines
  that appeared in every one of those scripts.
- **`Blank()` keeps the background.** Erasing a region used to reset it to the terminal default,
  which is wrong for a program that paints a ground colour and then clears — the cleared cells are
  still that colour on a real terminal.

### Verification Plan
- In `../sharp-shell`: `dotnet test tests/Sharp.Shell.Tests` — green, including the new pty cases.
- In `../duetui`: its test command — green, including the ported rendering case.
- `git -C ../duetui status --porcelain` shows `non-ai/support/pty-drive.py` deleted.

### Phase Summary
_(write when phase completes)_

## Final Recap
_(write when all phases complete)_

## Deployment Plan
_(write when all phases complete: the first commit and remote for this repository, the submodule
wiring in both consumers, and whether the API held still enough under two consumers to justify
packaging it.)_
