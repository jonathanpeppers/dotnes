/*
 * MMC3 Scanline IRQ Demo
 * Port of 8bitworkshop irq.c - demonstrates split-screen scrolling
 * using the MMC3 mapper's scanline IRQ counter.
 *
 * The IRQ fires every 8 scanlines, changing the X scroll register
 * to create a split-screen effect. Each horizontal band scrolls
 * at a different offset, creating a wavy visual.
 *
 * Shared state between main loop and IRQ callback is stored at
 * a fixed RAM address (0x0309) to avoid closure capture.
 */

// IRQ handler - called when MMC3 scanline counter fires
// Uses RAM address 0x0309 as a shared IRQ counter
static void irq_handler()
{
    // Read current IRQ scroll counter from shared RAM
    byte count = peek(0x0309);
    // Change X scroll based on current IRQ count
    poke(PPU_SCROLL, count);
    poke(PPU_SCROLL, 0);
    // Advance counter for next IRQ in this frame
    // NOTE: compute into a local first — poke() can't handle inline expressions
    byte next = (byte)(count + 1);
    poke(0x0309, next);
    // Acknowledge and re-enable MMC3 IRQ
    poke(MMC3_IRQ_DISABLE, 0);
    poke(MMC3_IRQ_ENABLE, 0);
}

// More accurate NES emulators simulate the mapper's
// monitoring of the A12 line, so the background and
// sprite pattern tables must be different.
set_ppu_ctrl_var((byte)(get_ppu_ctrl_var() | 0x08));

// Enable Work RAM
poke(MMC3_WRAM_ENABLE, 0x80);

// Mirroring - horizontal
poke(MMC3_MIRRORING, 0x01);

// Set up MMC3 IRQs every 8 scanlines
poke(MMC3_IRQ_LATCH, 7);
poke(MMC3_IRQ_RELOAD, 0);
poke(MMC3_IRQ_ENABLE, 0);

// Enable CPU IRQ
cli();

// Set IRQ callback
unsafe { irq_set_callback(&irq_handler); }

// Set palette colors
pal_col(1, 0x04);
pal_col(2, 0x20);
pal_col(3, 0x30);

// Fill screen with 'A' characters
vram_adr(NTADR_A(0, 0));
vram_fill(0x41, 32 * 28);

// Enable PPU rendering
ppu_on_all();

// Main loop - update scroll effect each frame
while (true)
{
    ppu_wait_frame();
    // Reset IRQ counter for next frame
    poke(0x0309, 0);
    // Reload MMC3 scanline counter
    poke(MMC3_IRQ_RELOAD, 0);
}
