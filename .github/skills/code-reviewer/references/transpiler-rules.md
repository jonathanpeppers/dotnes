# Transpiler Review Rules

Rules specific to the transpilation pipeline: `Transpiler.cs`,
`IL2NESWriter.cs`, `NESWriter.cs`, `BuiltInSubroutines.cs`, and
`Program6502.cs`. These are the most critical files in the project — bugs here
produce silently broken ROMs.

---

## IL Reading (Transpiler.cs)

| Check | What to look for |
|-------|-----------------|
| **Single-pass constraint** | The transpiler reads IL in a single forward pass. Changes that assume random access to the IL stream, or that require re-reading previously consumed instructions, will break the pipeline. |
| **UsedMethods tracking** | The transpiler tracks which NESLib methods are used (`UsedMethods` set) to decide which built-in subroutines to include in the ROM. If a new NESLib method is added but never registered in `UsedMethods`, the subroutine won't be emitted and the ROM will contain a `JSR` to an undefined address. |
| **UserMethods vs built-in methods** | User-defined methods (from the sample's `Program.cs`) go into `UserMethods`. NESLib API calls go into `UsedMethods`. Confusing these produces wrong code — user methods get inlined 6502, while NESLib calls become `JSR` to built-in subroutines. |
| **ExternMethods** | Methods declared as `static extern` are resolved to labels in external `.s` assembly files using cc65 convention (`_name`). Verify the label naming is correct. |
| **Method metadata** | `UserMethodMetadata` tracks arg count, return value, and parameter types. If this metadata is wrong, the caller/callee will disagree on stack layout — corrupting zero-page variables. |
| **ReadStaticVoidMain completeness** | When adding support for new IL patterns (e.g., new opcodes, new calling conventions), verify that `ReadStaticVoidMain` correctly populates all the data structures downstream consumers need. |

---

## 6502 Emission (IL2NESWriter.cs)

| Check | What to look for |
|-------|-----------------|
| **Correct opcode bytes** | Every 6502 instruction has a specific opcode byte for each addressing mode. `LDA immediate` is `$A9`, `LDA zero-page` is `$A5`, `LDA absolute` is `$AD`. Using the wrong addressing mode produces the wrong opcode byte — the ROM will execute the wrong instruction. Verify against the [6502 reference](https://www.masswerk.at/6502/6502_instruction_set.html). |
| **Addressing mode matches operand** | Immediate operands are 1 byte (0-255). Zero-page addresses are 1 byte ($00-$FF). Absolute addresses are 2 bytes (lo, hi). If an address is > $FF but emitted as zero-page, the high byte is silently lost. |
| **Branch offset calculation** | Relative branches (`BEQ`, `BNE`, `BPL`, `BMI`, `BCC`, `BCS`) use signed 8-bit offsets from the *next* instruction. An off-by-one in the offset jumps to the wrong location. Verify branch targets carefully. |
| **Label resolution** | `EmitWithLabel` defers address resolution to `Program6502`. If a label name is misspelled or doesn't match the `Block` name, the address won't resolve and the ROM will have garbage bytes at that location. |
| **Stack discipline** | The 6502 stack is only 256 bytes ($0100-$01FF). Unbalanced `PHA`/`PLA` or `JSR`/`RTS` will corrupt the stack. Every `JSR` must have a corresponding `RTS`. Every `PHA` must be balanced with `PLA` on all code paths. |
| **Zero-page variable allocation** | Local variables are allocated starting at `$0325` (the `local` field). Each new local gets a zero-page slot. If two variables share a slot when they shouldn't, they'll overwrite each other. Verify allocation doesn't overlap with NESLib's zero-page usage ($00-$1F). |
| **Flag side effects** | Many 6502 instructions implicitly modify the N (negative) and Z (zero) flags. Code that depends on flags from a previous instruction must not have intervening instructions that clobber those flags. Common trap: `LDA` sets N/Z, but a `STA` between the `LDA` and the `BEQ` doesn't change flags (safe). `LDX` between them would clobber flags (unsafe). |

---

## cc65 Software Stack (pusha/popa)

The transpiler uses a *software stack* (cc65's `pusha`/`popa` subroutines) on
top of the hardware 6502 stack. This is the most common source of subtle bugs.

| Check | What to look for |
|-------|-----------------|
| **pusha/popa balance** | Every `pusha` (push accumulator to cc65 stack) must be paired with a `popa` on all code paths. If an intrinsic sets `argsAlreadyPopped = true` but conditionally pops (e.g., `if (Stack.Count > 0) Stack.Pop()`), the call-site stack can become imbalanced. Verify both the normal and edge-case paths balance. |
| **Single-slot state variables** | The transpiler uses single-slot fields like `_pendingLeaveTarget`, `_savedConstantViaPusha`, and `_runtimeValueInA` to track state across IL instructions. A single slot can only hold one value — if multiple IL sequences need the same slot concurrently (e.g., nested `leave` targets, overlapping pusha chains), the second write silently overwrites the first. When adding or modifying these, consider whether the slot can be entered from multiple paths. |
| **State flag interactions** | Flags like `_savedConstantViaPusha` and `_runtimeValueInA` interact with each other. Widening one flag's scope (e.g., making popa trigger in more cases) can conflict with other paths that use the same cc65 stack slot for different purposes (function-call arguments vs arithmetic operands). Trace through all paths that read the flag. |
| **Backward instruction scans** | The transpiler sometimes scans backward through previously emitted bytes to find/modify/remove instructions (e.g., removing a preceding `LDA` when inlining an intrinsic, or patching branch offsets). These scans are inherently fragile — they assume a specific instruction sequence (e.g., "5 consecutive `ldc` instructions") that may break if the C# compiler changes its IL emission patterns. When reviewing backward scans: (1) check that the scan has a failure mode (throws, not silently produces wrong code), (2) document the assumed IL pattern. |
| **Conditional operation tracking** | Patterns like `hasOr`/`orMask`, `hasAnd`/`andMask` pair a boolean flag with a value. The flag may be set in cases where the value isn't populated (e.g., non-immediate operands), leading to operations applied with a stale or default mask. Verify that the flag and value are always set together. |

---

## Built-in Subroutines (BuiltInSubroutines.cs)

| Check | What to look for |
|-------|-----------------|
| **Match the cc65 reference** | Every subroutine should produce byte-identical output to the cc65 reference implementation where possible. Use `python scripts/compare_rom.py` to verify. |
| **Block naming** | Block names must match the label names used in `EmitWithLabel` calls. `nameof(_exit)` produces `"_exit"` — make sure the C# field/method name matches the assembly label convention. |
| **Emit chain ordering** | `.Emit()` calls are chained and produce bytes in order. A misplaced `.Emit()` shifts all subsequent bytes, breaking the entire subroutine. |
| **Label references within blocks** | Internal labels (like `"@1"`, `"@2"`) are local to a block. Branch targets referencing these labels must have correct offsets. |

---

## ROM Layout (Program6502.cs / NESWriter.cs)

| Check | What to look for |
|-------|-----------------|
| **Block ordering matters** | The order blocks are added to `Program6502` determines their position in the ROM. Music subroutines go before `main()` to match cc65 layout. Reordering blocks shifts all addresses downstream. |
| **iNES header correctness** | The 16-byte iNES header must correctly encode mapper number, PRG/CHR bank counts, mirroring mode, and battery flag. A wrong header means the emulator misinterprets the ROM format. |
| **Interrupt vectors** | The last 6 bytes of PRG ROM ($FFFA-$FFFF) contain NMI, RESET, and IRQ vectors. These must point to the correct subroutine addresses. Missing or wrong vectors = the NES doesn't boot. |
| **CHR ROM alignment** | CHR data must be exactly 8192 bytes per bank. Short or misaligned CHR data produces garbled graphics. |
| **Address space boundaries** | NES PRG ROM maps to $8000-$FFFF (32KB for NROM). Subroutines that exceed this space won't fit. Watch for programs that grow too large and silently wrap or truncate. |
