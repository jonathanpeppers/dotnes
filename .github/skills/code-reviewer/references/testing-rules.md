# Testing Review Rules

Testing conventions for the dotnes test suite (`src/dotnes.tests/`).

---

## Testing Framework

This project uses **xUnit** with **Verify** snapshots. Tests are run with
`dotnet test` (which always rebuilds — never use `--no-build`).

---

## Snapshot Testing

| Check | What to look for |
|-------|-----------------|
| **`.verified.bin` files are the source of truth** | For existing samples, the `.verified.bin` files contain the expected ROM output byte-for-byte. Any code change that causes `TranspilerTests.Write` to produce different bytes for an **unchanged** sample is WRONG — fix the code, not the snapshot. |
| **Only update snapshots when the sample changes** | When a sample's `Program.cs` is modified (new features, bug fixes), rebuild its test DLLs and update the `.verified.bin` to match the new expected output. Document why the snapshot changed in the commit message. |
| **New samples need snapshots** | Every new sample added to `samples/` must have a corresponding `[InlineData]` entry in `TranspilerTests.Write` and a `.verified.bin` file. Missing snapshots mean the sample can silently break. |
| **Per-sample CHR ROM** | Tests look for `chr_{name}.s` in the `Data/` folder first, falling back to `chr_generic.s`. The music sample uses an empty CHR. If a new sample has custom graphics, add its CHR file to `Data/`. |

---

## RoslynTests (Preferred for New Features)

| Check | What to look for |
|-------|-----------------|
| **Prefer RoslynTests over new samples** | When adding or testing new IL opcode support, write `RoslynTests` instead of creating new `samples/` directories and pre-compiled DLLs. RoslynTests are self-contained (C# source + assertions in one method) and easier to maintain. |
| **Use `GetProgramBytes(source)`** | `GetProgramBytes` compiles C# via Roslyn and transpiles to 6502 bytes. Assert on the hex output with `Assert.Contains`. This tests the full pipeline end-to-end. |
| **Use `AssertProgram(source, expectedAssembly)`** | `AssertProgram` checks the exact 6502 assembly output of the main block. Use this for precise byte-level verification of new opcode support. |
| **C# source includes implicit usings** | `RoslynTests` automatically prepends `using NES;using static NES.NESLib;`. Don't include these in the test source strings. |

---

## TranspilerTests

| Check | What to look for |
|-------|-----------------|
| **Debug and Release DLLs** | `TranspilerTests.Write` tests both Debug and Release builds via `[InlineData("name", true)]` and `[InlineData("name", false)]`. Both configurations must produce the same ROM output. |
| **`ReadStaticVoidMain` tests** | These verify IL parsing produces the expected instruction sequence. They use `.verified.txt` files (text, not binary). |
| **Test data DLLs in `Data/`** | Pre-compiled DLLs live in `src/dotnes.tests/Data/`. When adding new test cases (legacy approach), compile the sample, copy the `.dll` to `Data/Debug/` and `Data/Release/`, and add `[InlineData]` entries. |

---

## General Testing Patterns

| Check | What to look for |
|-------|-----------------|
| **Bug fixes need regression tests** | Every PR that fixes a bug should include a test that fails without the fix and passes with it. If the PR says "fixes #N" but adds no test, ask for one. |
| **Test assertions must be specific** | `Assert.NotNull(result)` doesn't tell you what went wrong. Prefer `Assert.Equal(expected, actual)` or `Assert.Contains(expectedHex, actualHex)` for richer failure messages. |
| **Prefer byte-for-byte equality over substring checks** | When testing that two code paths produce the same 6502 output, use `Assert.Equal(expectedBytes, actualBytes)` — not `Assert.Contains` on individual byte patterns. Substring/pattern checks can pass even when the outputs differ in critical ways (wrong ordering, extra instructions, missing instructions). |
| **Bound-check before indexing emitted bytes** | Tests that index into emitted byte arrays (e.g., `bytes[clcIndex + 2]`) should verify the index is in bounds first. A regression that removes instructions can turn a meaningful assertion failure into a confusing `IndexOutOfRangeException`. |
| **Assert locality, not just existence** | When checking that instruction X appears after instruction Y, don't just search the entire byte array. Constrain the search window (e.g., "within 3 instructions after Y") so the test catches cases where X exists but in the wrong position — like a JMP that's 50 bytes away instead of immediately after the target. |
| **Test edge cases** | Empty byte arrays, single-instruction programs, maximum-size ROMs, boundary values for addressing modes (0, 255, 256), and programs with many local variables should all be considered. |
| **Deterministic test data** | Tests should not depend on system locale, timezone, or current date. ROM output must be fully deterministic. |
| **xUnit conventions** | Use `[Fact]` for single-case tests, `[Theory]` with `[InlineData]` for parameterized tests. Use constructor injection for `ITestOutputHelper`. |
