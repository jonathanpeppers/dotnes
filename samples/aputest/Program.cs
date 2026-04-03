using static NES.NESColor;

// APU Test - demonstrates direct APU register manipulation
// Simplified port from 8bitworkshop aputest.c

// Set up palette
pal_col(1, DarkMagenta);
pal_col(2, LightGray);
pal_col(3, White);

// Display channel labels
vram_adr(NTADR_A(2, 2));
vram_write("APU TEST");

vram_adr(NTADR_A(2, 5));
vram_write("PULSE 1");

vram_adr(NTADR_A(2, 8));
vram_write("PULSE 2");

vram_adr(NTADR_A(2, 11));
vram_write("TRIANGLE");

vram_adr(NTADR_A(2, 14));
vram_write("NOISE");

// Enable rendering
ppu_on_all();

// Enable all sound channels (pulse1, pulse2, triangle, noise)
poke(APU_STATUS, 0x0F);

// Pulse 1: 50% duty, constant volume 15, period ~440Hz
poke(APU_PULSE1_CTRL, 0xBF);
poke(APU_PULSE1_SWEEP, 0x00);
poke(APU_PULSE1_TIMER_LO, 0xFD);
poke(APU_PULSE1_TIMER_HI, 0x00);

// Pulse 2: 25% duty, constant volume 10, period ~262Hz
poke(APU_PULSE2_CTRL, 0x7A);
poke(APU_PULSE2_SWEEP, 0x00);
poke(APU_PULSE2_TIMER_LO, 0xA9);
poke(APU_PULSE2_TIMER_HI, 0x01);

// Triangle: linear counter max, period ~835Hz
poke(APU_TRIANGLE_CTRL, 0xFF);
poke(APU_TRIANGLE_TIMER_LO, 0x42);
poke(APU_TRIANGLE_TIMER_HI, 0x00);

// Noise: constant volume 15, period 6
poke(APU_NOISE_CTRL, 0x3F);
poke(APU_NOISE_PERIOD, 0x06);
poke(APU_NOISE_LENGTH, 0x18);

while (true) ;
