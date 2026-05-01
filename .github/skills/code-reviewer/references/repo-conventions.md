# Repo Conventions

Patterns, naming, and style rules specific to the dotnes repository. Always
loaded during reviews.

---

## Formatting & Style

This project uses standard C# formatting with spaces (not Mono style). The
`.editorconfig` defines the conventions.

| Check | What to look for |
|-------|-----------------|
| **Spaces, not tabs** | Indentation uses spaces (4 spaces for C#, 2 spaces for XML/MSBuild). |
| **Standard C# spacing** | No space before `(` in method calls: `Foo()`, not `Foo ()`. No space before `[` in array access: `array[0]`, not `array [0]`. |
| **Allman braces** | Opening braces go on their own line (`csharp_new_line_before_open_brace = all`). |
| **File-scoped namespaces** | Use `namespace Foo;` not `namespace Foo { }`. |
| **No `#region`/`#endregion`** | Region directives hide code and make reviews harder. Remove them. Exception: `BuiltInSubroutines.cs` uses `#region` to group large blocks of related subroutines — this is acceptable there. |
| **Minimal diffs** | Don't leave random empty lines. Preserve existing formatting and comments in files you didn't write. |
| **`[]` not `Array.Empty<T>()`** | Use collection expressions (`[]`) for empty arrays. The `.editorconfig` enforces `IDE0300` as an error. |
| **Separate import groups** | `using` directives are grouped: system namespaces first, then others, with blank lines between groups (`dotnet_separate_import_directive_groups = true`). |
| **`readonly` fields** | Fields that are only assigned in the constructor should be `readonly` (enforced as warning by `.editorconfig`). |
| **Private field naming** | Private fields use `_camelCase` prefix. Private static fields use `s_camelCase` prefix. Constants use `PascalCase`. |

---

## Architecture Awareness

Understanding the transpilation pipeline is essential for reviewing changes.

| Check | What to look for |
|-------|-----------------|
| **Reference assembly pattern** | `neslib` methods have **no implementations** — they all `throw null!`. The transpiler looks up method names to emit 6502 subroutine calls. If someone adds real logic to `NESLib.cs`, that's wrong — it should stay as stubs. |
| **Transpiler is single-pass** | `Transpiler.cs` reads IL in a single forward pass. Changes that assume random access or multiple passes over the IL stream will break. |
| **Block-based assembly** | `BuiltInSubroutines.cs` creates `Block` objects with label-based forward references. `Program6502.cs` resolves addresses. Changes here affect ROM layout for ALL samples. |
| **IL2NESWriter emits 6502** | This is the core IL → 6502 mapping. Every `case` in the instruction switch must emit correct 6502 bytes. A wrong opcode here silently produces a broken ROM. |

---

## Patterns & Conventions

| Check | What to look for |
|-------|-----------------|
| **Use existing utilities** | Check `Transpiler`, `NESWriter`, `IL2NESWriter`, `AssemblyReader`, `NESConstants` before writing new helpers. |
| **Return `!Log.HasLoggedErrors`** | The MSBuild task `TranspileToNES.Execute()` must return `!Log.HasLoggedErrors`, not `true`/`false` directly. |
| **`ILogger` for diagnostics** | Use `ILogger` (the project's own interface), not `Console.WriteLine` or `Debug.WriteLine`. Diagnostic output is controlled by `NESDiagnosticLogging`. |
| **Comments explain "why", not "what"** | `// increment i` adds nothing. `// skip the NES header — 16 bytes` explains intent. |
| **Track TODOs as issues** | A `// TODO` hidden in code will be forgotten. File an issue and reference it in the comment. |
| **Remove stale comments** | If the code changed, update the comment. Comments describing old behavior are misleading. |
| **Update `docs/msbuild-properties.md`** | When adding new public MSBuild properties, always add documentation with property name, type, default value, description, and an XML example. |
| **Don't commit to `main`** | All changes must go through pull requests — no direct pushes to `main`. |

---

## Code Review Anti-Patterns

These are specific patterns that have been flagged in past reviews. **Do NOT suggest these changes:**

| Anti-pattern | Why it's wrong |
|-------------|---------------|
| **Using `.Where()` when the loop body needs `out` parameters from `TryResolve`** | The `Where()` clause calls `TryResolve` twice (slower) and can't capture the `out` parameter. Use a `foreach` with `if (TryResolve(..., out var x))` instead. |
| **Renaming parameters to avoid shadowing field names** | A parameter `local` in `WriteStloc(Local local)` is fine even though there's a field `readonly ushort local = 0x325`. The types are different and context is clear. Don't create noise renames. |
| **"Simplifying" conditional logic without understanding semantic coupling** | `if (needsDecsp4 && usedMethods.Contains("pad_poll"))` adds `PadTrigger`/`PadState` blocks intentionally — `pad_trigger` and `pad_state` are internal implementation dependencies of `pad_poll`, not independently user-facing. Don't suggest checking them individually. |
