# Native runtime integration

dotnes supports ca65-style native `.s` code alongside C#. Native callback
relocation already uses `nmi_set_callback(&Handler)` and
`irq_set_callback(&Handler)`; no separate registration API or dummy managed
callback is necessary.

## Callback registration

Declare a static `extern void` method with no arguments and pass its **direct**
address to the existing setter. Top-level local extern declarations and static
class members are supported, including calls from helper methods. For example,
with an MMC3 cartridge:

```csharp
using static NES.NESLib;

ppu_use_native_renderer();
poke(PPU_CTRL, 0);          // Quiesce NMI generation before updating its pointer.
sei();                     // Mask CPU IRQs.
poke(MMC3_IRQ_DISABLE, 0);  // Disable/acknowledge this cartridge's IRQ source.
unsafe
{
    nmi_set_callback(&native_nmi);
    irq_set_callback(&native_irq);
}
// Initialize native buffers and hardware before enabling their interrupt sources.
poke(PPU_CTRL, 0x80);
cli();                     // The mapper IRQ source remains disabled until configured.
while (true)
    ppu_wait_nmi();

static extern void native_nmi();
static extern void native_irq();
```

Include the assembly file through the usual `NESAssembly` build input:

```asm
.segment "CODE"
.export _native_nmi, _native_irq

_native_nmi:
    ; Example only: $6000 is application-reserved, cartridge-provided PRG RAM.
    inc $6000
    rts

_native_irq:
    lda #$00
    sta $E000              ; Acknowledge/disable MMC3 IRQ.
    rts
```

The call to `irq_set_callback` retains the IRQ dispatcher and selects it for the
ROM's IRQ vector even when there are no managed callback bodies. Managed
callbacks continue to work with the same setters.

**Callback replacement is not atomic.** The runtime writes a two-byte address.
The caller must quiesce the relevant interrupt source before replacing it.
`sei()` masks IRQs only: it does **not** disable NMI. Disable NMI generation at
PPUCTRL and allow any already-latched NMI to finish before beginning the update.
Keep the relevant sources disabled until the callback and all state it accesses
are ready. For IRQs, mask the CPU and disable/acknowledge the hardware source as
appropriate; merely clearing one pending IRQ is not necessarily sufficient.
Restore the intended PPUCTRL/IRQ-mask state afterward. Do not pass null, runtime
addresses, or a function-pointer variable; install a no-op static handler to
replace a callback with no work.

## Exclusive native renderer ownership

`ppu_use_native_renderer()` is a **compile-time, whole-program directive**, like
the existing music-table directives. Put it in startup code to make the choice
visible. It emits no instructions and is **not a runtime mode switch**.
References in supported helper methods are discovered too; putting the directive
under a runtime condition does not make renderer ownership conditional.

Without the directive, the existing stock renderer, startup and interrupt bytes
are unchanged. With it:

- Reset-time RAM/PPU initialization and video-system detection remain intact.
  Native ownership starts when application code begins, not during reset.
- NMI saves A/X/Y, increments the 8-bit `nesclock()` counter, advances the existing
  modulo-six frame phase, calls the registered NMI callback, restores Y/X/A, and
  returns through RTI. Hardware interrupt entry/RTI preserve processor status
  and the interrupted PC. Both counters advance **before** the callback.
- The dispatcher performs **no PPU/OAM I/O**: no OAM DMA, palette/VRAM upload,
  scroll reset, PPUCTRL/PPUMASK write, or stock graphics-shadow write. It does not
  inspect a private rendering gate. The native renderer owns PPUCTRL, PPUMASK,
  scroll/address latches, OAM DMA, mapper scheduling, and its own shadows.
- IRQ with a registered callback saves/restores A/X/Y around that callback and
  returns with RTI, without advancing frame counters or invoking the NMI callback.
  Without an IRQ callback the native-mode default IRQ handler just returns;
  it does not acknowledge hardware, so leave unused IRQ sources disabled.
- `ppu_wait_nmi`, `ppu_wait_frame`, `delay`, `nesclock`, and `get_frame_count`
  remain available. The waits require NMI generation to be enabled and do not
  publish/flush native graphics work for you.

Stock rendering operations produce `TranspileException` when combined with this
directive, including references in helpers: `ppu_off`, `ppu_on_*`, `ppu_mask`,
palette/OAM-buffer operations (including `OamScope`), `scroll`/`set_scroll_*`/`split`,
`bank_bg`/`bank_spr`, `vram_inc`, `set_ppu_ctrl_var`, fades, and buffered VRAM
operations. This prevents silently using stock shadows or queues that native NMI
does not consume. `get_ppu_ctrl_var` can still read the stock cache, but it is not
an authoritative copy of native PPUCTRL writes. Direct rendering-off transfers
and direct hardware access remain available under the native runtime's scheduling
and synchronization rules.

## Native calling and interrupt responsibilities

A native callback is a **subroutine**: return with **RTS, never RTI**. The
dispatcher owns the interrupt frame and restores A/X/Y, flags, and PC. Callbacks
may clobber A/X/Y and arithmetic flags, but must balance their hardware-stack
usage and preserve every compiler/runtime RAM location they borrow, including
zero-page pointers, temporary values, software-stack pointer/content, locals and
static storage. There is no general-purpose callback scratch allocation.
Reserve application-owned memory on the chosen cartridge instead of assuming
private zero-page slots are free.

NMI may interrupt foreground code or an IRQ callback. `sei()` does not prevent
that nesting. Use distinct native scratch for concurrent contexts or a protocol
that preserves it. The dispatcher does not make managed callbacks, library
calls, or shared buffers reentrant. In particular, do not call foreground helpers
from callbacks when they share compiler-allocated locals or scratch. Do not
re-enable maskable interrupts inside a callback unless your runtime explicitly
handles that nesting.

The IRQ callback must acknowledge its hardware source. Both callbacks must fit
their hardware timing budget, avoid waiting for themselves, and avoid switching
out the ROM bank containing active code. With MMC3 banked layout, keep interrupt
code and its required data in the fixed region.

Ordinary foreground externs use the existing 6502 calling convention, **not a
CLR/native platform ABI**. For a single byte parameter its value is in A; byte
results are returned in A with X cleared. Earlier arguments use the software
stack and require the existing callee-cleanup convention. Preserve live compiler
state and any borrowed scratch; a simple byte setter can preserve X/Y and return
with RTS. This change adds no new marshalling, overload, object, or delegate
support.

## Extern symbol compatibility

The canonical case-sensitive symbol for `static extern void Foo()` is `_Foo`.
This applies to top-level local functions and static class members, whether
called from main, from a helper, or used as a callback address. Compiler-generated
local-function names are reduced to the declared method name; containing C#
types do not introduce a native namespace. Avoid same-name declarations with
different signatures.

For compatibility with older helper-method emission, bare `Foo` is accepted
when `_Foo` is absent. Existing assembly exporting both spellings continues to
work if they resolve to the same address. If both are defined at different
addresses, compilation/address resolution reports a `TranspileException` naming
the conflict rather than choosing silently. Only names registered as extern
methods receive this compatibility treatment.

The same rules apply to `Program6502.DefineExternalLabel` bindings. Aliases are
recomputed during address resolution, including relocation and branch
relaxation. Missing code symbols still fail with `UnresolvedLabelException` when
emitting bytes; an in-memory model may be bound before that step. An unresolved
native symbol is not a reason to replace the real compiler with a stub.

## Transfer inventory

No game-specific transfer protocol or new bulk-transfer API is introduced.

| Need | Existing boundary |
| --- | --- |
| Set a PPU address or write one byte | `vram_adr`, `vram_put`, or direct `poke` to hardware registers |
| Rendering-off contiguous data upload | `vram_write(byte[])` or its explicit-size overload |
| Rendering-off repeated byte fill | `vram_fill(value, length)` |
| Rendering-off readback | `vram_read` with an appropriately sized destination |
| CPU-addressed native ROM/PRG-RAM access | Existing `peek`/`poke` and native assembly |
| Native OAM DMA | Native writes to OAMADDR/OAMDMA using an application-owned, page-aligned buffer |
| Stock queued nametable/VRAM updates | Existing `set_vram_update`, `vrambuf_*`, and nesdoug helpers in stock mode, not a second native queue |

The caller must establish rendering-off ownership, a valid mapped source and
destination, a count within the buffer bounds, and any required PPU latch/control
state. Use positive lengths for `vram_write`/`vram_read`; skip zero-length copies
at the call site. These are immediate transfers, not interrupt-safe transactions
or an unbounded VBlank budget. Native callbacks must neither borrow their scratch
nor disturb the active PPU transfer while it runs. A native-owned PPUCTRL shadow
must be written directly; `vram_inc` uses the stock shadow and is not compatible
with exclusive native mode. Packet layouts, transfer admission, publish/consume
protocols, scanline schedules, and scene policy remain application responsibilities.
