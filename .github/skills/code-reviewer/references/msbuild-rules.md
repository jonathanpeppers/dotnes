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

---

## MSBuild Targets (dotnes.targets / dotnes.props)

| Check | What to look for |
|-------|-----------------|
| **Incremental builds (`Inputs`/`Outputs`)** | The `Transpile` target must have `Inputs` and `Outputs` so MSBuild can skip it when nothing changed. Inputs include `$(TargetPath)`, `@(NESAssembly)`, and the properties stamp file. |
| **Properties stamp file** | A `_WriteNESPropertiesStamp` target writes NES property values to a stamp file. This ensures changing a property like `NESBattery` retriggers transpilation. If a new MSBuild property is added, it must be included in this stamp file. |
| **`TranspileDependsOn` extensibility** | The `Transpile` target uses `$(TranspileDependsOn)` for ordering. New dependencies should be added via this property, not `BeforeTargets`/`AfterTargets`. |
| **XML indentation** | MSBuild/XML files use 2 spaces for indentation (per `.editorconfig`), not tabs. |
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
