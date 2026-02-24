// fami.c port — Demonstrates FamiTone2 library for music and sound effects
// Press controller buttons to hear sound effects.

using System.Runtime.InteropServices;

// Link FamiTone2 functions (from famitone2.s)
[DllImport("ext")] static extern void music_play(byte song);
[DllImport("ext")] static extern void sfx_play(byte sound, byte channel);

pal_col(1, 0x04);
pal_col(2, 0x20);
pal_col(3, 0x30);
vram_adr(NTADR_A(2, 2));
vram_write("FAMITONE2 DEMO");

// Initialize music system (string arg = data label in linked .s file)
famitone_init("danger_streets_music_data");
sfx_init("demo_sounds");

// Set music callback function for NMI
nmi_set_callback("famitone_update");

// Play music
music_play(0);

// Enable rendering
ppu_on_all();

// Repeat forever
while (true)
{
    ppu_wait_nmi();

    // Poll controller 0
    PAD pad = pad_poll(0);

    // Play sounds when buttons pushed
    if ((pad & PAD.A) != 0)
        sfx_play(0, 0);
    if ((pad & PAD.B) != 0)
        sfx_play(1, 1);
    if ((pad & PAD.LEFT) != 0)
        sfx_play(2, 2);
    if ((pad & PAD.RIGHT) != 0)
        sfx_play(3, 3);
}
