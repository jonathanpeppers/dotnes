# C# Review Rules

General C# guidance applicable across this repository.

---

## Nullable Reference Types

| Check | What to look for |
|-------|-----------------|
| **Nullable is project-level** | Nullable is enabled via `<Nullable>enable</Nullable>` in project files. Per-file `#nullable enable` is unnecessary. |
| **Avoid `!` (null-forgiving operator)** | The postfix `!` null-forgiving operator should be avoided. Add proper null checks or make the type non-nullable. Exception: `NESLib.cs` uses `throw null!` by design. |
| **`ArgumentNullException.ThrowIfNull`** | Use `ArgumentNullException.ThrowIfNull(param)` for parameter validation — but only in `net10.0`-targeting projects. The `dotnes.tasks` project targets `netstandard2.0` where this API is unavailable; use explicit `if (x is null) throw new ArgumentNullException(nameof(x));` there instead. |

---

## Error Handling

| Check | What to look for |
|-------|-----------------|
| **No empty catch blocks** | Every `catch` must capture the `Exception` and log it (or rethrow). No silent swallowing. |
| **Fail fast on critical ops** | If a critical operation fails (file not found, invalid IL), throw immediately. Silently continuing leads to confusing downstream failures or broken ROMs. |
| **Include actionable details in exceptions** | Use `nameof` for parameter names. Include the unsupported value or unexpected type. Never throw empty exceptions. |
| **Challenge exception swallowing** | When a PR adds `catch { continue; }` or `catch { return null; }`, question whether the exception is truly expected or masking a deeper problem. |

---

## Performance

| Check | What to look for |
|-------|-----------------|
| **Avoid unnecessary allocations** | Don't create intermediate collections when LINQ chaining or a single pass would do. Char arrays for `string.Split()` should be `static readonly` fields. |
| **`HashSet.Add()` already handles duplicates** | Calling `.Contains()` before `.Add()` does the hash lookup twice. Just call `.Add()`. |
| **Don't wrap a value in an interpolated string** | `$"{someString}"` creates an unnecessary `string.Format` call when `someString` is already a string. |
| **Pre-allocate collections when size is known** | Use `new List<T>(capacity)` or `new Dictionary<TK, TV>(count)` when the size is known or estimable. |
| **Avoid closures in hot paths** | Lambdas that capture local variables allocate a closure object on every call. In the transpiler's main loop or frequently-called emit methods, extract the lambda to a static method or cache the delegate. |
| **Place cheap checks before expensive ones** | In validation chains, test simple conditions (null checks, boolean flags) before allocating strings or doing I/O. Short-circuit with `&&`/`||`. |
| **Watch for O(n²)** | Nested loops over the same or related collections, repeated `.Contains()` on a `List<T>`, or LINQ `.Where()` inside a loop are O(n²). Switch to `HashSet<T>` or `Dictionary<TK, TV>` for lookups. |
| **Use `.Ordinal` for identifier comparisons** | `.Ordinal` is faster than `.OrdinalIgnoreCase`. Use `.OrdinalIgnoreCase` only for filesystem paths. IL method names, opcode strings, and label names should use `.Ordinal`. |

---

## Code Organization

| Check | What to look for |
|-------|-----------------|
| **One type per file** | Each public class, struct, enum, or interface must be in its own `.cs` file named after the type. Partial classes are fine (e.g., `Transpiler` is `partial`). |
| **Use `record` for data types** | Immutable data-carrier types should be `record` types — they get value equality, `ToString()`, and deconstruction for free. |
| **Remove unused code** | Dead methods, speculative helpers, and code "for later" should be removed. Ship only what's needed. No commented-out code — Git has history. |
| **New helpers default to `internal`** | New utility methods should be `internal` unless a confirmed external consumer needs them. Use `InternalsVisibleTo` for test access (already configured in this project). |
| **Reduce indentation with early returns** | Invert conditions and `return`/`continue` early so the main logic has less nesting. |
| **Don't initialize fields to default values** | `bool flag = false;` and `int count = 0;` are noise. The CLR zero-initializes all fields. Only assign when the initial value is non-default. |
| **Well-named constants over magic numbers** | `if (address > 0xFFFF)` is fine for 6502 address space (well-known boundary). But buffer sizes, retry counts, and obscure thresholds should be named constants. |
