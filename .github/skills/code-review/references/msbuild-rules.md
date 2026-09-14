# MSBuild Review Rules

MSBuild task and target guidance for the dotnes build infrastructure.

---

## MSBuild Task (TranspileToNES.cs)

| Check | What to look for |
|-------|-----------------|
| **Return `!Log.HasLoggedErrors`** | `Execute()` must return `!Log.HasLoggedErrors`. Do not return `true`/`false` directly — it bypasses the centralized error-tracking mechanism. |
| **`[Required]` properties need defaults** | `[Required]` properties must have a default value: `public string Foo { get; set; } = "";` or `public string[] Bar { get; set; } = [];`. Non-`[Required]` properties should be nullable where appropriate. |
| **Dispose resources** | The transpiler creates `PEReader`, `StreamReader`, and other disposable objects. Verify `using` statements are present. Leaked file handles prevent rebuilds on Windows. |
| **Logger null-object pattern** | `Logger ??= DiagnosticLogging ? new MSBuildLogger(Log) : null;` — the logger is null when diagnostic logging is off. Code that uses `Logger` must handle the null case (the `ILogger?` interface is nullable). |
| **Drain redirected process output** | A child process with redirected stdout or stderr must drain both streams, preferably asynchronously. Otherwise a full OS pipe can deadlock the build. Include captured output in failure diagnostics. |

---

## MSBuild Targets (dotnes.targets / dotnes.props)

| Check | What to look for |
|-------|-----------------|
| **Incremental builds (`Inputs`/`Outputs`)** | Targets that write files need accurate `Inputs` and `Outputs` so MSBuild can skip them when nothing changed. The `Transpile` target inputs include `$(TargetPath)`, `@(NESAssembly)`, and the properties stamp file. Targets that only read or populate items do not need fake outputs. |
| **Properties stamp file** | A `_WriteNESPropertiesStamp` target writes NES property values to a stamp file. This ensures changing a property like `NESBattery` retriggers transpilation. If a new MSBuild property is added, it must be included in this stamp file. |
| **Track intermediate files in `FileWrites`** | Generated intermediate outputs must be included in `@(FileWrites)` so incremental clean handles them without deleting files still needed by a skipped target. Populate the item even when the producing task is skipped. |
| **`TranspileDependsOn` extensibility** | The `Transpile` target uses `$(TranspileDependsOn)` for ordering. New dependencies should be added via this property, not `BeforeTargets`/`AfterTargets`. |
| **Use item counts for empty checks** | Prefer `'@(Items->Count())' != '0'` over joining `'@(Items)' != ''`; the latter can allocate and emit very large strings. |
| **XML indentation** | MSBuild/XML files use 2 spaces for indentation (per `.editorconfig`), not tabs. |
| **Put `Condition` first** | On targets and tasks, keep `Condition` first when the surrounding file follows that convention. It is the most important attribute while debugging evaluation. |
| **`NoStdLib=true`** | `dotnes.props` sets `NoStdLib=true` to prevent BCL references. If a change removes or weakens this, NES programs could accidentally reference BCL types that the transpiler can't handle. |
| **`Optimize=true`** | `dotnes.props` forces Release-mode IL optimization. The transpiler expects optimized IL patterns. Debug IL has different instruction sequences (extra `nop`, different branch patterns) that may not be handled. |

---

## Adding New MSBuild Properties

| Check | What to look for |
|-------|-----------------|
| **Document in `docs/msbuild-properties.md`** | Every new public MSBuild property must be documented with: property name, type, default value, description, and an XML example. |
| **Add to properties stamp** | If the property affects transpilation output, add it to the `_WriteNESPropertiesStamp` target so incremental builds correctly retrigger. |
| **Wire through TranspileToNES.cs** | The MSBuild property must be passed to the task as a property, and the task must forward it to the `Transpiler` constructor. |
| **Test both values** | If the property is boolean, test with both `true` and `false`. If it has enumerated values (like `NESMirroring`), test all valid values. |
