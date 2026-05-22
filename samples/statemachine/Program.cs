/*
State machine sample — demonstrates C# `switch` statements transpiled to 6502.

Three game states (title / playing / over) drive a simple background color
change. Press START on controller 1 to advance through the states. The
dispatch is written with a `switch` over a `byte` state variable with a
`default:` clause — Roslyn lowers this to the IL `switch` opcode which the
transpiler emits as CMP/BNE+JMP trampolines.
*/

const byte STATE_TITLE   = 0;
const byte STATE_PLAYING = 1;
const byte STATE_OVER    = 2;

// set palette colors
pal_col(0, DarkBlue);
pal_col(1, Magenta);
pal_col(2, LightGray);
pal_col(3, White);

// write text once to the nametable while the PPU is off
vram_adr(NTADR_A(8, 14));
vram_write("PRESS START");
ppu_on_all();

byte state = STATE_TITLE;

while (true)
{
    ppu_wait_nmi();
    pad_poll(0);

    switch (state)
    {
        case STATE_TITLE:
            pal_col(0, DarkBlue);
            if ((pad_trigger(0) & PAD.START) != 0)
                state = STATE_PLAYING;
            break;
        case STATE_PLAYING:
            pal_col(0, DarkGreen);
            if ((pad_trigger(0) & PAD.START) != 0)
                state = STATE_OVER;
            break;
        case STATE_OVER:
            pal_col(0, DarkRed);
            if ((pad_trigger(0) & PAD.START) != 0)
                state = STATE_TITLE;
            break;
        default:
            state = STATE_TITLE;
            break;
    }
}
