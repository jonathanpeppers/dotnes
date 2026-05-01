# Security Review Rules

Security checklist for code reviews. Applicable when any code files change.

---

## File & Path Safety

| Check | What to look for |
|-------|-----------------|
| **Path traversal** | `StartsWith()` checks on paths must normalize with `Path.GetFullPath()` first. A path like `C:\safe\..\evil` bypasses naive prefix checks. |
| **File overwrite protection** | The transpiler writes `.nes` files to `$(OutputPath)`. Verify the output path is validated and doesn't allow writing outside the expected build directory. |

---

## Process & Command Safety

| Check | What to look for |
|-------|-----------------|
| **No command injection** | If any code spawns processes (e.g., running Mesen for testing), arguments must not be interpolated from user input. Use `ArgumentList` or separate argument arrays. |

---

## Supply Chain

| Check | What to look for |
|-------|-----------------|
| **Review NuGet dependency changes** | If `PackageReference` versions change, verify the update is intentional and from a trusted source. Check for known vulnerabilities. |
| **Assembly file integrity** | `.s` assembly files are included in the ROM verbatim. A malicious `.s` file could contain arbitrary 6502 code. Verify that new or modified `.s` files contain expected content. |

---

## Data Integrity

| Check | What to look for |
|-------|-----------------|
| **ROM output determinism** | The same source code must always produce the same ROM bytes. Non-deterministic output (timestamps, random seeds, GUID-based ordering) in the transpiler would break snapshot tests and make builds unreproducible. |
| **No secrets in ROM** | The ROM is a binary artifact that can be freely distributed. Verify no API keys, tokens, file paths, or other sensitive data are baked into the ROM output. |
