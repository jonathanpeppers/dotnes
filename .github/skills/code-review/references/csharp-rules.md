# C# Review Rules

General C# guidance applicable across this repository.

---

## Target Framework Compatibility

| Check | What to look for |
|-------|-----------------|
| **Oldest TFM must compile** | Check every API and overload against the changed project's oldest target framework. In particular, `dotnes.tasks` targets `netstandard2.0`, so newer BCL APIs may require an existing compatibility helper or an explicit fallback. |
| **Prefer existing compatibility helpers** | Search the repository before adding wrappers or direct modern-BCL calls. Existing helpers may encode `netstandard2.0`, platform, logging, or process behavior that a new implementation would lose. |

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
| **Preserve cancellation** | A catch-all must not swallow `OperationCanceledException`. Rethrow cancellation or use a filter that excludes it. |
| **Check process exit codes consistently** | If one external-tool invocation checks its exit code, equivalent invocations must do the same. Include captured output in failures when it is available. |
| **Initialize `out` parameters on every path** | Methods with `out` parameters must assign them on success and failure paths before returning. |

---

## Async, Cancellation & Thread Safety

| Check | What to look for |
|-------|-----------------|
| **Propagate `CancellationToken`** | Every async method that accepts a token must pass it to downstream async calls and observe it in loops or long-running work. An unused token is a broken contract. |
| **Protect shared mutable state** | Static caches and state reachable from concurrent tasks require `ConcurrentDictionary`, `Interlocked`, or a documented lock. A `Dictionary<TKey, TValue>` cannot be read safely while another thread writes. |
| **Publish fully initialized objects** | Complete construction and setup before assigning a shared singleton or cache entry. Another thread must not observe a partially initialized instance. Prefer `Lazy<T>` or `LazyInitializer` over hand-written double-checked locking. |

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
| **Cache repeated expensive accessors** | If a property or metadata lookup is used repeatedly in a block, store it in a local when evaluation is non-trivial. Do not obscure simple field access. |

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
| **Use the simple dispose pattern for sealed types** | A sealed type without a finalizer can implement `IDisposable.Dispose()` directly. The full `Dispose(bool)` and `GC.SuppressFinalize` pattern is for inheritance or finalization scenarios. |
