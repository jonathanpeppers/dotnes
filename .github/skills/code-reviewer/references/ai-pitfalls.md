# AI Code Generation Pitfalls

Patterns that AI-generated code consistently gets wrong in this repository.
Always loaded during reviews.

---

## Common AI Mistakes

| Pattern | What to watch for |
|---------|------------------|
| **Reinventing the wheel** | AI creates new infrastructure instead of using existing utilities. ALWAYS check if a similar utility exists before accepting new wrapper code. This is the most expensive AI pattern — hundreds of lines of plausible code that duplicates what's already there. |
| **Over-engineering** | Speculative helper classes, unused overloads, "for testability" abstractions nobody asked for. If no caller needs it today, remove it. |
| **Swallowed errors** | AI catch blocks love to eat exceptions silently. Check EVERY catch block. Also check that the MSBuild task returns `!Log.HasLoggedErrors`. |
| **Null-forgiving operator (`!`)** | The postfix `!` null-forgiving operator (e.g., `foo!.Bar`) should be avoided. If the value can be null, add a proper null check. If it can't be null, make the type non-nullable. AI frequently sprinkles `!` to silence the compiler. Note: this rule is about the postfix `!` operator, not the logical negation `!` (e.g., `if (!someBool)`). Exception: `NESLib.cs` methods use `throw null!` by design — that's the reference assembly pattern. |
| **`string.Empty` and `Array.Empty<T>()`** | AI defaults to these. Use `""` and `[]` instead. The `.editorconfig` enforces `IDE0300` as an error for collection expressions. |
| **Sloppy structure** | Multiple types in one file, block-scoped namespaces, `#region` directives (except in `BuiltInSubroutines.cs`), classes where records would do. New helpers marked `public` when `internal` suffices. |
| **Wrong NES assumptions** | AI confidently generates NES code using BCL types (`string`, `List<T>`, LINQ), classes/objects, or heap allocation — none of which work on a 6502. NES programs are pure procedural code with fixed-size byte arrays and zero-page variables only. |
| **Redefining neslib constants** | AI creates local `const byte PAD_A = 0x01` or similar redefinitions of enums/constants already provided by neslib (`PAD`, `MASK`, PPU registers, APU constants). Use the built-in definitions directly — redefinitions lead to bugs when values are wrong. |
| **Adding implementations to NESLib.cs** | AI tries to add real method bodies to `NESLib.cs`. Every method there must be `=> throw null!` — the transpiler maps method names to 6502 subroutines, it never runs the C# body. |
| **Unused parameters** | AI adds `CancellationToken` or other parameters but never observes them. If a parameter is accepted, it must be used. |
| **Confidently wrong 6502 facts** | AI makes authoritative claims about 6502 behavior (addressing modes, flag effects, cycle counts) that are wrong. Always verify 6502 claims against the [instruction set reference](https://www.masswerk.at/6502/6502_instruction_set.html). |
| **Docs describe intent not reality** | AI doc comments often describe what the code *should* do, not what it *actually* does. Review doc comments against the implementation. |
| **Filler words in docs** | "So" at the start of a sentence adds nothing. "Basically" and "essentially" are padding. Be direct. |
| **`git commit --amend`** | AI uses `--amend` on commits. Always create new commits — the maintainer will squash as needed. |
| **Modifying `.verified.bin` files** | AI updates snapshot files to make tests pass after a code change. The `.verified.bin` files are the source of truth — if a code change produces different bytes for an unchanged sample, the code is wrong, not the snapshot. Only update snapshots when the sample's `Program.cs` itself changed. |
