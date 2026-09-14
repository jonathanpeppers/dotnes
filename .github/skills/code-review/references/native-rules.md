# Native C and cc65 Review Rules

Rules for C reference implementations under `samples/` and any code compiled
with cc65. These sources are behavioral references for dotnes output, not
ordinary desktop C programs.

---

## Reference Integrity

| Check | What to look for |
|-------|-----------------|
| **Preserve behavioral parity** | When a C reference and C# sample represent the same program, compare control flow, NESLib calls, data layout, and frame timing. Cosmetic source similarity is not enough; the resulting ROM behavior must match. |
| **Do not modernize away cc65 constraints** | cc65 targets a 6502 with a small software stack and limited C support. Avoid desktop-only libraries, dynamic allocation, recursion, large stack locals, and language features unsupported by the configured cc65 version. |
| **Treat imported C as traceable source** | Keep upstream URLs, commit hashes, or provenance comments for ported reference code so behavior and licensing can be rechecked. |

---

## Memory and Ownership

| Check | What to look for |
|-------|-----------------|
| **Quantify memory use** | NES RAM and stack are scarce. Account for static buffers, local arrays, and per-frame state; a small desktop allocation can be a critical NES regression. |
| **Balance ownership** | Every allocation or acquired resource needs a clear owner and cleanup path. Prefer static storage for fixed NES data and avoid heap allocation in samples. |
| **Bound array access** | Validate loop limits and index arithmetic against fixed buffer, OAM, palette, and VRAM sizes. Off-by-one writes can corrupt unrelated NES state. |

---

## C Correctness

| Check | What to look for |
|-------|-----------------|
| **Use exact-width types where layout matters** | ROM tables, register values, and serialized data should use types whose width matches the NES contract. Check signedness in shifts, comparisons, and promotion-heavy expressions. |
| **Avoid magic byte counts** | Use `sizeof` or a named NES constant when the value represents a type or buffer size. Literal hardware addresses are acceptable when they are established register addresses and remain clear in context. |
| **Check external API contracts** | Verify return values, ownership, and side effects for cc65 and NES library calls rather than assuming desktop C semantics. |
| **Keep reference code warning-clean** | New warnings often indicate truncation, signedness, or unsupported constructs that can change generated 6502 code. Do not dismiss them as host-compiler noise. |
