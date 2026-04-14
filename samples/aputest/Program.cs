// APU Test - demonstrates apu_play_tone helper and direct register access
// Simplified port from 8bitworkshop aputest.c

// Set up palette
pal_col(1, DarkMagenta);
pal_col(2, LightGray);
pal_col(3, White);

// Display channel labels
vram_adr(NTADR_A(2, 2));
vram_write("APU TEST");

vram_adr(NTADR_A(2, 5));
vram_write("PULSE 1 HELPER");

vram_adr(NTADR_A(2, 8));
vram_write("PULSE 2 HELPER");

vram_adr(NTADR_A(2, 11));
vram_write("TRIANGLE");

vram_adr(NTADR_A(2, 14));
vram_write("NOISE");

// Enable rendering
ppu_on_all();

// Enable all sound channels (pulse1, pulse2, triangle, noise)
poke(APU_STATUS, 0x0F);

// Pulse 1 via helper: 50% duty, volume 15, period ~440Hz (0x00FD)
apu_play_tone(0, 0x00FD, 2, 15);

// Pulse 2 via helper: 25% duty, volume 10, period ~262Hz (0x01A9)
apu_play_tone(1, 0x01A9, 1, 10);

// Triangle: linear counter max, period ~835Hz (direct register access)
poke(APU_TRIANGLE_CTRL, 0xFF);
poke(APU_TRIANGLE_TIMER_LO, 0x42);
poke(APU_TRIANGLE_TIMER_HI, 0x00);

// Noise: constant volume 15, period 6 (direct register access)
poke(APU_NOISE_CTRL, 0x3F);
poke(APU_NOISE_PERIOD, 0x06);
poke(APU_NOISE_LENGTH, 0x18);

while (true) ;
