/*
Based on: https://8bitworkshop.com/ (siegegame.c)

A tower-defense siege game for NES.
Enemies march from the right side toward the castle on the left.
The player places walls to block enemy paths.
*/

#pragma warning disable CS0649, CS0219

// Tile constants (background layer)
const byte CH_BLANK = 0x00;
const byte CH_WALL = 0x01;
const byte CH_CASTLE = 0x02;
const byte CH_FLOOR = 0x05;

// Sprite tile indices
const byte SPR_CURSOR = 0x04;
const byte SPR_ENEMY = 0x03;

// Game constants
const byte BOARD_X = 2;
const byte BOARD_Y = 4;
const byte BOARD_W = 26;
const byte BOARD_H = 20;
const byte MAX_ENEMIES = 8;
const byte MAX_WALLS = 24;
const byte STATE_TITLE = 0;
const byte STATE_PLAY = 1;
const byte STATE_OVER = 2;

// Palette
byte[] palette = new byte[] {
    Black, DarkGray, Gray, White,
    Black, DarkRed, Red, LightRed,
    Black, Azure, LightAzure, PaleAzure,
    Black, DarkLime, Lime, LightLime,
    Black, DarkGray, Gray, White,
    Black, DarkRed, Red, LightRed,
    Black, Azure, LightAzure, PaleAzure,
    Black, DarkLime, Lime, LightLime
};

// Game state — all globals so they survive across function calls
byte gameState = STATE_TITLE;
byte cursorX = 14;
byte cursorY = 14;
byte score = 0;
byte wave = 1;
byte lives = 3;
byte frameCount = 0;
byte spawnTimer = 0;
byte spawnRate = 60;
PAD lastTrigger = 0;
byte castleHits = 0;
byte activeCount = 0;

// Temp globals for collision checks (avoids local variable slot collisions)
byte hitWall = 0;
byte tempNX = 0;
byte tempNY = 0;

// Flags to defer array updates outside VRAM-access code
byte doPlaceWall = 0;
byte doRemoveWall = 0;

// Enemy arrays (Structure of Arrays)
byte[] enemyX = new byte[MAX_ENEMIES];
byte[] enemyY = new byte[MAX_ENEMIES];
byte[] enemyActive = new byte[MAX_ENEMIES];
byte[] enemyTimer = new byte[MAX_ENEMIES];

// Wall arrays — used for placement, removal, and enemy collision checks.
// VRAM updates are deferred to separate blocks because NTADR_A with
// runtime args can't be mixed with array operations in the same block.
byte wallCount = 0;
byte[] wallX = new byte[MAX_WALLS];
byte[] wallY = new byte[MAX_WALLS];

// --- Setup ---

static void setup_graphics(byte[] pal)
{
    ppu_off();
    oam_clear();
    pal_all(pal);
    vram_adr(0x2000);
    vram_fill(CH_BLANK, 0x0400);
    ppu_on_all();
}

// --- Title screen ---

static void draw_title_text()
{
    ppu_off();
    vram_adr(NTADR_A(10, 10));
    vram_write("SIEGE GAME");
    vram_adr(NTADR_A(7, 14));
    vram_write("PRESS A TO PLAY");
    vram_adr(NTADR_A(8, 18));
    vram_write("DEFEND THE CASTLE!");
    ppu_on_all();
}

// --- HUD (1-param functions only) ---

static void draw_hud_score(byte sc)
{
    ppu_off();
    vram_adr(NTADR_A(2, 2));
    vram_write("SCORE:");
    vram_adr(NTADR_A(8, 2));
    byte tens = (byte)(sc / 10);
    byte ones = (byte)(sc % 10);
    vram_put((byte)(0x30 + tens));
    vram_put((byte)(0x30 + ones));
    ppu_on_all();
}

static void draw_hud_wave(byte w)
{
    ppu_off();
    vram_adr(NTADR_A(14, 2));
    vram_write("WAVE:");
    vram_adr(NTADR_A(19, 2));
    vram_put((byte)(0x30 + w));
    ppu_on_all();
}

static void draw_hud_lives(byte lv)
{
    ppu_off();
    vram_adr(NTADR_A(23, 2));
    vram_write("HP:");
    vram_adr(NTADR_A(26, 2));
    vram_put((byte)(0x30 + lv));
    ppu_on_all();
}

static void draw_gameover_text()
{
    ppu_off();
    vram_adr(NTADR_A(11, 12));
    vram_write("GAME OVER");
    vram_adr(NTADR_A(7, 16));
    vram_write("PRESS A TO RETRY");
    ppu_on_all();
}

// --- Wave management (1-param) ---

static byte advance_wave(byte w)
{
    if (w < 9)
        return (byte)(w + 1);
    return w;
}

static byte get_spawn_rate(byte w)
{
    if (w >= 5)
        return 20;
    if (w >= 3)
        return 30;
    return 45;
}

// --- Screen clear ---

static void clear_screen()
{
    ppu_off();
    vram_adr(0x2000);
    vram_fill(CH_BLANK, 0x0400);
    ppu_on_all();
}

// ===== MAIN PROGRAM =====

setup_graphics(palette);

while (true)
{
    if (gameState == STATE_TITLE)
    {
        draw_title_text();
        while (gameState == STATE_TITLE)
        {
            ppu_wait_nmi();
            lastTrigger = pad_trigger(0);
            if ((lastTrigger & PAD.A) != 0)
            {
                score = 0;
                wave = 1;
                lives = 3;
                frameCount = 0;
                spawnTimer = 0;
                cursorX = 14;
                cursorY = 14;
                // get_spawn_rate BEFORE any array access (avoids label leak)
                spawnRate = get_spawn_rate(wave);
                // Clear enemies
                for (byte ce = 0; ce < MAX_ENEMIES; ce++)
                {
                    enemyActive[ce] = 0;
                    enemyTimer[ce] = 0;
                }
                wallCount = 0;
                clear_screen();
                // Draw board with constant NTADR_A (avoids runtime args)
                ppu_off();
                vram_adr(NTADR_A(0, BOARD_Y));
                for (byte row = 0; row < BOARD_H; row++)
                {
                    for (byte col = 0; col < 32; col++)
                    {
                        if (col == BOARD_X)
                            vram_put(CH_CASTLE);
                        else if (col > BOARD_X && col < (byte)(BOARD_X + BOARD_W))
                            vram_put(CH_FLOOR);
                        else
                            vram_put(CH_BLANK);
                    }
                }
                ppu_on_all();
                draw_hud_score(score);
                draw_hud_wave(wave);
                draw_hud_lives(lives);
                gameState = STATE_PLAY;
            }
        }
    }

    if (gameState == STATE_PLAY)
    {
        while (gameState == STATE_PLAY)
        {
            ppu_wait_nmi();
            lastTrigger = pad_trigger(0);

            // Cursor movement
            if ((lastTrigger & PAD.LEFT) != 0)
            {
                if (cursorX > (byte)(BOARD_X + 1))
                    cursorX = (byte)(cursorX - 1);
            }
            if ((lastTrigger & PAD.RIGHT) != 0)
            {
                if (cursorX < (byte)(BOARD_X + BOARD_W - 1))
                    cursorX = (byte)(cursorX + 1);
            }
            if ((lastTrigger & PAD.UP) != 0)
            {
                if (cursorY > BOARD_Y)
                    cursorY = (byte)(cursorY - 1);
            }
            if ((lastTrigger & PAD.DOWN) != 0)
            {
                if (cursorY < (byte)(BOARD_Y + BOARD_H - 1))
                    cursorY = (byte)(cursorY + 1);
            }

            // Place wall with A — check arrays, defer VRAM update
            doPlaceWall = 0;
            if ((lastTrigger & PAD.A) != 0)
            {
                if (cursorX > BOARD_X)
                {
                    if (wallCount < MAX_WALLS)
                    {
                        hitWall = 0;
                        for (byte w = 0; w < wallCount; w++)
                        {
                            if (wallX[w] == cursorX)
                            {
                                if (wallY[w] == cursorY)
                                    hitWall = 1;
                            }
                        }
                        if (hitWall == 0)
                        {
                            wallX[wallCount] = cursorX;
                            wallY[wallCount] = cursorY;
                            wallCount = (byte)(wallCount + 1);
                            doPlaceWall = 1;
                        }
                    }
                }
            }
            // VRAM update — separated from array operations to keep clean
            // block state for NTADR_A with runtime args
            if (doPlaceWall != 0)
            {
                ppu_off();
                vram_adr(NTADR_A(cursorX, cursorY));
                vram_put(CH_WALL);
                ppu_on_all();
            }

            // Remove wall with B — check arrays, defer VRAM update
            doRemoveWall = 0;
            if ((lastTrigger & PAD.B) != 0)
            {
                for (byte w = 0; w < wallCount; w++)
                {
                    if (wallX[w] == cursorX)
                    {
                        if (wallY[w] == cursorY)
                        {
                            doRemoveWall = 1;
                            wallCount = (byte)(wallCount - 1);
                            wallX[w] = wallX[wallCount];
                            wallY[w] = wallY[wallCount];
                            w = wallCount; // exit loop
                        }
                    }
                }
            }
            // VRAM update — separated from array operations
            if (doRemoveWall != 0)
            {
                ppu_off();
                vram_adr(NTADR_A(cursorX, cursorY));
                vram_put(CH_FLOOR);
                ppu_on_all();
            }

            // Spawn enemies
            frameCount = (byte)(frameCount + 1);
            spawnTimer = (byte)(spawnTimer + 1);
            if (spawnTimer >= spawnRate)
            {
                spawnTimer = 0;
                for (byte s = 0; s < MAX_ENEMIES; s++)
                {
                    if (enemyActive[s] == 0)
                    {
                        enemyX[s] = (byte)(BOARD_X + BOARD_W - 1);
                        enemyY[s] = (byte)(BOARD_Y + (byte)(rand8() % BOARD_H));
                        enemyActive[s] = 1;
                        enemyTimer[s] = 0;
                        s = MAX_ENEMIES; // exit loop (avoid break)
                    }
                }
            }

            // Move enemies — check wall arrays for collisions
            castleHits = 0;
            for (byte i = 0; i < MAX_ENEMIES; i++)
            {
                if (enemyActive[i] != 0)
                {
                    enemyTimer[i] = (byte)(enemyTimer[i] + 1);
                    if (enemyTimer[i] >= 15)
                    {
                        enemyTimer[i] = 0;
                        tempNX = (byte)(enemyX[i] - 1);
                        tempNY = enemyY[i];

                        if (tempNX <= BOARD_X)
                        {
                            // Hit castle
                            enemyActive[i] = 0;
                            castleHits = (byte)(castleHits + 1);
                        }
                        else
                        {
                            // Check wall arrays (no VRAM here — can't mix
                            // arrays with NTADR_A runtime args in same block)
                            hitWall = 0;
                            for (byte w = 0; w < wallCount; w++)
                            {
                                if (wallX[w] == tempNX)
                                {
                                    if (wallY[w] == tempNY)
                                        hitWall = 1;
                                }
                            }
                            if (hitWall == 0)
                            {
                                // Move forward
                                enemyX[i] = tempNX;
                            }
                            // If wall, enemy stays in place (blocked)
                        }
                    }
                }
            }

            // Draw sprites: cursor + enemies
            using (var oam = new OamScope())
            {
                tempNX = (byte)(cursorX * 8);
                tempNY = (byte)(cursorY * 8);
                oam.spr(tempNX, tempNY, SPR_CURSOR, 0);
                for (byte i = 0; i < MAX_ENEMIES; i++)
                {
                    if (enemyActive[i] != 0)
                    {
                        tempNX = (byte)(enemyX[i] * 8);
                        tempNY = (byte)(enemyY[i] * 8);
                        oam.spr(tempNX, tempNY, SPR_ENEMY, 1);
                    }
                }
            }

            // Apply castle damage
            if (castleHits > 0)
            {
                if (lives > castleHits)
                    lives = (byte)(lives - castleHits);
                else
                    lives = 0;
                draw_hud_lives(lives);

                if (lives == 0)
                {
                    draw_gameover_text();
                    gameState = STATE_OVER;
                }
            }

            // Check wave complete
            activeCount = 0;
            for (byte i = 0; i < MAX_ENEMIES; i++)
            {
                if (enemyActive[i] != 0)
                    activeCount = (byte)(activeCount + 1);
            }
            if (activeCount == 0 && frameCount > spawnRate)
            {
                score = (byte)(score + wave);
                wave = advance_wave(wave);
                spawnRate = get_spawn_rate(wave);
                frameCount = 0;
                spawnTimer = 0;
                draw_hud_score(score);
                draw_hud_wave(wave);
                draw_hud_lives(lives);
            }
        }
    }

    if (gameState == STATE_OVER)
    {
        while (gameState == STATE_OVER)
        {
            ppu_wait_nmi();
            lastTrigger = pad_trigger(0);
            if ((lastTrigger & PAD.A) != 0)
            {
                clear_screen();
                gameState = STATE_TITLE;
            }
        }
    }
}
