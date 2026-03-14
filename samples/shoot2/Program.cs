// shoot2 — Vertical scrolling shooter
// Demonstrates: UxROM mapper 2 + CHR RAM, poke() APU sound effects,
// user-defined functions, structure-of-arrays, collision detection, scoring.
// Based on concepts from: https://8bitworkshop.com/v3.10.0/?platform=nes&file=shoot2.c

#pragma warning disable CS0649, CS8321, CS0219

// --- Game constants ---
const byte MAX_BULLETS = 4;
const byte MAX_ENEMIES = 6;
const byte MAX_STARS = 8;
const byte MAX_EXPLOSIONS = 3;
const byte PLAYER_SPEED = 2;
const byte BULLET_SPEED = 4;
const byte PLAYER_START_X = 120;
const byte PLAYER_START_Y = 200;
const byte SPAWN_INTERVAL = 45;
const byte FIRE_COOLDOWN = 8;
const byte HIT_DISTANCE = 12;

// Sprite tile indices (pattern table 1, uploaded to $1000)
const byte SPR_PLAYER = 0x00;
const byte SPR_BULLET = 0x01;
const byte SPR_ENEMY = 0x02;
const byte SPR_EXPLODE1 = 0x03;
const byte SPR_EXPLODE2 = 0x04;
const byte SPR_STAR = 0x05;

// BG tile indices (pattern table 0, uploaded to $0000)
// 0x00 = blank, 0x01-0x0A = digits '0'-'9', 0x0B-0x0F = S,C,O,R,E

// === Tile data ===

// Sprite tiles: 6 tiles x 16 bytes = 96 bytes
byte[] SPRITE_TILES = [
    // Tile 0x00: Player ship
    0x18, 0x3C, 0x7E, 0xFF, 0xDB, 0x42, 0x42, 0x00,
    0x18, 0x3C, 0x7E, 0xFF, 0xDB, 0x42, 0x42, 0x00,
    // Tile 0x01: Bullet
    0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10,
    0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10,
    // Tile 0x02: Enemy
    0x24, 0x18, 0x7E, 0xDB, 0xFF, 0x5A, 0x42, 0x81,
    0x00, 0x18, 0x3C, 0x7E, 0x7E, 0x3C, 0x18, 0x00,
    // Tile 0x03: Explosion frame 1
    0x4A, 0x24, 0x42, 0x18, 0x18, 0x42, 0x24, 0x4A,
    0x4A, 0x24, 0x42, 0x18, 0x18, 0x42, 0x24, 0x4A,
    // Tile 0x04: Explosion frame 2
    0x82, 0x44, 0x00, 0x24, 0x24, 0x00, 0x44, 0x82,
    0x82, 0x44, 0x00, 0x24, 0x24, 0x00, 0x44, 0x82,
    // Tile 0x05: Star (single pixel)
    0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00
];

// Background tiles: blank + digits 0-9 + letters S,C,O,R,E
// 16 tiles x 16 bytes = 256 bytes
byte[] BG_TILES = [
    // Tile 0x00: Blank
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    // Tile 0x01: '0'
    0x7E, 0x42, 0x46, 0x4A, 0x52, 0x62, 0x7E, 0x00,
    0x7E, 0x42, 0x46, 0x4A, 0x52, 0x62, 0x7E, 0x00,
    // Tile 0x02: '1'
    0x18, 0x38, 0x18, 0x18, 0x18, 0x18, 0x7E, 0x00,
    0x18, 0x38, 0x18, 0x18, 0x18, 0x18, 0x7E, 0x00,
    // Tile 0x03: '2'
    0x7E, 0x02, 0x02, 0x7E, 0x40, 0x40, 0x7E, 0x00,
    0x7E, 0x02, 0x02, 0x7E, 0x40, 0x40, 0x7E, 0x00,
    // Tile 0x04: '3'
    0x7E, 0x02, 0x02, 0x3E, 0x02, 0x02, 0x7E, 0x00,
    0x7E, 0x02, 0x02, 0x3E, 0x02, 0x02, 0x7E, 0x00,
    // Tile 0x05: '4'
    0x42, 0x42, 0x42, 0x7E, 0x02, 0x02, 0x02, 0x00,
    0x42, 0x42, 0x42, 0x7E, 0x02, 0x02, 0x02, 0x00,
    // Tile 0x06: '5'
    0x7E, 0x40, 0x40, 0x7E, 0x02, 0x02, 0x7E, 0x00,
    0x7E, 0x40, 0x40, 0x7E, 0x02, 0x02, 0x7E, 0x00,
    // Tile 0x07: '6'
    0x7E, 0x40, 0x40, 0x7E, 0x42, 0x42, 0x7E, 0x00,
    0x7E, 0x40, 0x40, 0x7E, 0x42, 0x42, 0x7E, 0x00,
    // Tile 0x08: '7'
    0x7E, 0x02, 0x02, 0x04, 0x08, 0x08, 0x08, 0x00,
    0x7E, 0x02, 0x02, 0x04, 0x08, 0x08, 0x08, 0x00,
    // Tile 0x09: '8'
    0x7E, 0x42, 0x42, 0x7E, 0x42, 0x42, 0x7E, 0x00,
    0x7E, 0x42, 0x42, 0x7E, 0x42, 0x42, 0x7E, 0x00,
    // Tile 0x0A: '9'
    0x7E, 0x42, 0x42, 0x7E, 0x02, 0x02, 0x7E, 0x00,
    0x7E, 0x42, 0x42, 0x7E, 0x02, 0x02, 0x7E, 0x00,
    // Tile 0x0B: 'S'
    0x7E, 0x42, 0x40, 0x7E, 0x02, 0x42, 0x7E, 0x00,
    0x7E, 0x42, 0x40, 0x7E, 0x02, 0x42, 0x7E, 0x00,
    // Tile 0x0C: 'C'
    0x7E, 0x42, 0x40, 0x40, 0x40, 0x42, 0x7E, 0x00,
    0x7E, 0x42, 0x40, 0x40, 0x40, 0x42, 0x7E, 0x00,
    // Tile 0x0D: 'O'
    0x7E, 0x42, 0x42, 0x42, 0x42, 0x42, 0x7E, 0x00,
    0x7E, 0x42, 0x42, 0x42, 0x42, 0x42, 0x7E, 0x00,
    // Tile 0x0E: 'R'
    0x7E, 0x42, 0x42, 0x7E, 0x48, 0x44, 0x42, 0x00,
    0x7E, 0x42, 0x42, 0x7E, 0x48, 0x44, 0x42, 0x00,
    // Tile 0x0F: 'E'
    0x7E, 0x40, 0x40, 0x7E, 0x40, 0x40, 0x7E, 0x00,
    0x7E, 0x40, 0x40, 0x7E, 0x40, 0x40, 0x7E, 0x00
];

// === Graphics initialization (inline, references tile data) ===
ppu_off();
oam_clear();

// Upload sprite tiles to CHR RAM pattern table 1 ($1000)
vram_adr(0x1000);
vram_write(SPRITE_TILES);

// Upload background tiles to CHR RAM pattern table 0 ($0000)
vram_adr(0x0000);
vram_write(BG_TILES);

// Set palettes
pal_bg(new byte[] {
    0x0F, 0x30, 0x10, 0x00,
    0x0F, 0x30, 0x10, 0x00,
    0x0F, 0x30, 0x10, 0x00,
    0x0F, 0x30, 0x10, 0x00
});
pal_spr(new byte[] {
    0x0F, 0x30, 0x10, 0x20,
    0x0F, 0x16, 0x26, 0x36,
    0x0F, 0x11, 0x21, 0x31,
    0x0F, 0x07, 0x17, 0x27
});

// Fill nametable with blank tiles
vram_adr(NAMETABLE_A);
vram_fill(0x00, 0x0400);

// Write "SCORE" label (BG tiles 0x0B-0x0F)
vram_adr(NTADR_A(2, 1));
vram_put(0x0B);
vram_put(0x0C);
vram_put(0x0D);
vram_put(0x0E);
vram_put(0x0F);

// Write initial score "000000" (tile 0x01 = '0')
vram_adr(NTADR_A(8, 1));
vram_put(0x01);
vram_put(0x01);
vram_put(0x01);
vram_put(0x01);
vram_put(0x01);
vram_put(0x01);

bank_spr(1);
bank_bg(0);
vrambuf_clear();
set_vram_update(updbuf);
ppu_on_all();

// Enable APU sound channels
enable_apu();
set_rand(42);

// === Game state ===
byte player_x = PLAYER_START_X;
byte player_y = PLAYER_START_Y;
byte fire_cooldown = 0;
byte spawn_timer = 0;

// Score: 6 digits as BG tile indices (0x01 = '0' ... 0x0A = '9')
byte d0 = 0x01;
byte d1 = 0x01;
byte d2 = 0x01;
byte d3 = 0x01;
byte d4 = 0x01;
byte d5 = 0x01;
byte[] digits = new byte[6];

// Structure-of-Arrays: Bullets
byte[] bullet_x = new byte[MAX_BULLETS];
byte[] bullet_y = new byte[MAX_BULLETS];
byte[] bullet_active = new byte[MAX_BULLETS];

// Structure-of-Arrays: Enemies
byte[] enemy_x = new byte[MAX_ENEMIES];
byte[] enemy_y = new byte[MAX_ENEMIES];
byte[] enemy_active = new byte[MAX_ENEMIES];
byte[] enemy_speed = new byte[MAX_ENEMIES];

// Structure-of-Arrays: Stars (parallax scrolling)
byte[] star_x = new byte[MAX_STARS];
byte[] star_y = new byte[MAX_STARS];
byte[] star_speed = new byte[MAX_STARS];

// Structure-of-Arrays: Explosions
byte[] exp_x = new byte[MAX_EXPLOSIONS];
byte[] exp_y = new byte[MAX_EXPLOSIONS];
byte[] exp_timer = new byte[MAX_EXPLOSIONS];

// Zero-initialize guard arrays (new byte[N] doesn't zero memory on NES)
for (byte zi = 0; zi < MAX_BULLETS; zi++) bullet_active[zi] = 0;
for (byte zi = 0; zi < MAX_ENEMIES; zi++) enemy_active[zi] = 0;
for (byte zi = 0; zi < MAX_EXPLOSIONS; zi++) exp_timer[zi] = 0;

// Initialize stars
for (byte i = 0; i < MAX_STARS; i++)
{
    star_x[i] = rand8();
    star_y[i] = rand8();
    star_speed[i] = (byte)(1 + (byte)(rand8() & 0x01));
}

// === Main game loop ===
while (true)
{
    pad_poll(0);
    // Compute trigger flag immediately while pad_trigger return is in A
    // (workaround: ldloc;ldc;and pattern broken when local not already in A)
    byte fire_pressed = (byte)(pad_trigger(0) & (byte)PAD.A);

    // --- Player movement (clamp to stay within bounds) ---
    // Call pad_state(0) inline for each check so return value is fresh in A
    if ((pad_state(0) & (byte)PAD.LEFT) != 0)
    {
        if (player_x > 8 + PLAYER_SPEED) player_x = (byte)(player_x - PLAYER_SPEED);
        else player_x = 8;
    }
    if ((pad_state(0) & (byte)PAD.RIGHT) != 0)
    {
        if (player_x < 240 - PLAYER_SPEED) player_x = (byte)(player_x + PLAYER_SPEED);
        else player_x = 240;
    }
    if ((pad_state(0) & (byte)PAD.UP) != 0)
    {
        if (player_y > 32 + PLAYER_SPEED) player_y = (byte)(player_y - PLAYER_SPEED);
        else player_y = 32;
    }
    if ((pad_state(0) & (byte)PAD.DOWN) != 0)
    {
        if (player_y < 224 - PLAYER_SPEED) player_y = (byte)(player_y + PLAYER_SPEED);
        else player_y = 224;
    }

    // --- Fire bullet ---
    if (fire_cooldown != 0) fire_cooldown = (byte)(fire_cooldown - 1);
    if (fire_pressed != 0 && fire_cooldown == 0)
    {
        for (byte i = 0; i < MAX_BULLETS; i++)
        {
            if (bullet_active[i] == 0)
            {
                bullet_x[i] = (byte)(player_x + 3);
                bullet_y[i] = (byte)(player_y - 8);
                bullet_active[i] = 1;
                fire_cooldown = FIRE_COOLDOWN;
                sfx_shoot();
                break;
            }
        }
    }

    // --- Move bullets ---
    for (byte i = 0; i < MAX_BULLETS; i++)
    {
        if (bullet_active[i] != 0)
        {
            if (bullet_y[i] < BULLET_SPEED)
            {
                bullet_active[i] = 0;
            }
            else
            {
                bullet_y[i] = (byte)(bullet_y[i] - BULLET_SPEED);
                if (bullet_y[i] < 16) bullet_active[i] = 0;
            }
        }
    }

    // --- Spawn enemies ---
    spawn_timer = (byte)(spawn_timer + 1);
    if (spawn_timer >= SPAWN_INTERVAL)
    {
        spawn_timer = 0;
        for (byte i = 0; i < MAX_ENEMIES; i++)
        {
            if (enemy_active[i] == 0)
            {
                enemy_x[i] = rnd_range(16, 232);
                enemy_y[i] = 0;
                enemy_active[i] = 1;
                enemy_speed[i] = rnd_range(1, 3);
                break;
            }
        }
    }

    // --- Move enemies ---
    for (byte i = 0; i < MAX_ENEMIES; i++)
    {
        if (enemy_active[i] != 0)
        {
            enemy_y[i] = (byte)(enemy_y[i] + enemy_speed[i]);
            if (enemy_y[i] > 230) enemy_active[i] = 0;
        }
    }

    // --- Move stars (parallax scrolling) ---
    for (byte i = 0; i < MAX_STARS; i++)
    {
        star_y[i] = (byte)(star_y[i] + star_speed[i]);
        if (star_y[i] > 239)
        {
            star_y[i] = 0;
            star_x[i] = rand8();
        }
    }

    // --- Update explosions ---
    for (byte i = 0; i < MAX_EXPLOSIONS; i++)
    {
        if (exp_timer[i] != 0) exp_timer[i] = (byte)(exp_timer[i] - 1);
    }

    // --- Collision: bullets vs enemies ---
    for (byte bi = 0; bi < MAX_BULLETS; bi++)
    {
        if (bullet_active[bi] != 0)
        {
            for (byte ei = 0; ei < MAX_ENEMIES; ei++)
            {
                if (enemy_active[ei] != 0)
                {
                    byte dx = abs_diff(bullet_x[bi], enemy_x[ei]);
                    byte dy = abs_diff(bullet_y[bi], enemy_y[ei]);
                    if (dx < HIT_DISTANCE && dy < HIT_DISTANCE)
                    {
                        // Start explosion
                        for (byte xi = 0; xi < MAX_EXPLOSIONS; xi++)
                        {
                            if (exp_timer[xi] == 0)
                            {
                                exp_x[xi] = enemy_x[ei];
                                exp_y[xi] = enemy_y[ei];
                                exp_timer[xi] = 16;
                                break;
                            }
                        }
                        bullet_active[bi] = 0;
                        enemy_active[ei] = 0;
                        sfx_hit();
                        // Increment score
                        d5 = (byte)(d5 + 1);
                        if (d5 > 0x0A) { d5 = 0x01; d4 = (byte)(d4 + 1); }
                        if (d4 > 0x0A) { d4 = 0x01; d3 = (byte)(d3 + 1); }
                        if (d3 > 0x0A) { d3 = 0x01; d2 = (byte)(d2 + 1); }
                        if (d2 > 0x0A) { d2 = 0x01; d1 = (byte)(d1 + 1); }
                        if (d1 > 0x0A) { d1 = 0x01; d0 = (byte)(d0 + 1); }
                        if (d0 > 0x0A) d0 = 0x01;
                        break;
                    }
                }
            }
        }
    }

    // --- Collision: enemies vs player ---
    for (byte ei = 0; ei < MAX_ENEMIES; ei++)
    {
        if (enemy_active[ei] != 0)
        {
            byte dx = abs_diff(player_x, enemy_x[ei]);
            byte dy = abs_diff(player_y, enemy_y[ei]);
            if (dx < HIT_DISTANCE && dy < HIT_DISTANCE)
            {
                enemy_active[ei] = 0;
                sfx_player_die();
                pal_spr_bright(8);
                ppu_wait_nmi();
                ppu_wait_nmi();
                ppu_wait_nmi();
                ppu_wait_nmi();
                pal_spr_bright(4);
            }
        }
    }

    // --- Draw sprites ---
    oam_clear();
    oam_off = 0;

    // Player
    oam_off = oam_spr(player_x, player_y, SPR_PLAYER, 0, oam_off);

    // Bullets
    for (byte i = 0; i < MAX_BULLETS; i++)
    {
        if (bullet_active[i] != 0)
            oam_off = oam_spr(bullet_x[i], bullet_y[i], SPR_BULLET, 0, oam_off);
    }

    // Enemies
    for (byte i = 0; i < MAX_ENEMIES; i++)
    {
        if (enemy_active[i] != 0)
            oam_off = oam_spr(enemy_x[i], enemy_y[i], SPR_ENEMY, 1, oam_off);
    }

    // Explosions
    for (byte i = 0; i < MAX_EXPLOSIONS; i++)
    {
        if (exp_timer[i] != 0)
        {
            byte tile = SPR_EXPLODE1;
            if (exp_timer[i] < 8) tile = SPR_EXPLODE2;
            oam_off = oam_spr(exp_x[i], exp_y[i], tile, 3, oam_off);
        }
    }

    // Stars
    for (byte i = 0; i < MAX_STARS; i++)
    {
        oam_off = oam_spr(star_x[i], star_y[i], SPR_STAR, 2, oam_off);
    }

    oam_hide_rest(oam_off);

    // --- Update score display ---
    digits[0] = d0;
    digits[1] = d1;
    digits[2] = d2;
    digits[3] = d3;
    digits[4] = d4;
    digits[5] = d5;
    vrambuf_put(NTADR_A(8, 1), digits, 6);

    ppu_wait_nmi();
    vrambuf_clear();
}

// === User-defined functions (static, no captured variables) ===

// Random number in range [lo, hi)
static byte rnd_range(byte lo, byte hi)
{
    byte range = (byte)(hi - lo);
    byte r = rand8();
    return (byte)((byte)(r % range) + lo);
}

// Absolute difference of two bytes
static byte abs_diff(byte a, byte b)
{
    if (a > b) return (byte)(a - b);
    return (byte)(b - a);
}

// Enable all APU sound channels
static void enable_apu()
{
    poke(APU_STATUS, 0x0F);
}

// APU pulse channel 1 — caller passes pre-computed register values
// (avoids runtime expressions in poke which leak C stack bytes)
static void apu_pulse1_write(byte ctrl, byte timer_lo, byte timer_hi)
{
    poke(APU_PULSE1_CTRL, ctrl);
    poke(APU_PULSE1_SWEEP, 0x00);
    poke(APU_PULSE1_TIMER_LO, timer_lo);
    poke(APU_PULSE1_TIMER_HI, timer_hi);
}

// APU pulse channel 2 — caller passes pre-computed register values
static void apu_pulse2_write(byte ctrl, byte timer_lo, byte timer_hi)
{
    poke(APU_PULSE2_CTRL, ctrl);
    poke(APU_PULSE2_SWEEP, 0x00);
    poke(APU_PULSE2_TIMER_LO, timer_lo);
    poke(APU_PULSE2_TIMER_HI, timer_hi);
}

// APU noise channel — caller passes pre-computed register values
static void apu_noise_write(byte ctrl, byte period)
{
    poke(APU_NOISE_CTRL, ctrl);
    poke(APU_NOISE_PERIOD, period);
    poke(APU_NOISE_LENGTH, 0xF8);
}

// APU triangle channel — caller passes pre-computed register values
static void apu_triangle_write(byte timer_lo, byte timer_hi)
{
    poke(APU_TRIANGLE_CTRL, 0xFF);
    poke(APU_TRIANGLE_TIMER_LO, timer_lo);
    poke(APU_TRIANGLE_TIMER_HI, timer_hi);
}

// Sound effect: player fires (pulse1 decay: 0x30|0x4A=0x7A, timer_hi 0xF8|0x01=0xF9)
static void sfx_shoot()
{
    apu_pulse1_write(0x7A, 0x80, 0xF9);
}

// Sound effect: enemy hit (noise decay: 0x30|0x0A=0x3A, period 4)
static void sfx_hit()
{
    apu_noise_write(0x3A, 0x04);
}

// Sound effect: explosion (noise: 0x30|0x0F=0x3F, period 6; triangle: timer_hi 0xF8|0x01=0xF9)
static void sfx_explode()
{
    apu_noise_write(0x3F, 0x06);
    apu_triangle_write(0x40, 0xF9);
}

// Sound effect: player destroyed (pulse1: 0x30|0x8F=0xBF, timer_hi 0xF8|0x02=0xFA; noise: 0x30|0x0F=0x3F, period 8)
static void sfx_player_die()
{
    apu_pulse1_write(0xBF, 0x00, 0xFA);
    apu_noise_write(0x3F, 0x08);
}
