/*
Regression test: (byte)(runtimeA - runtimeB) < constant in branch.
Exercises EmitBranchCompare preserving SBC from HandleAddSub.
Previously the transpiler removed the SBC instruction and replaced
it with CMP #imm, comparing the raw minuend instead of the difference.
*/

byte[] PALETTE = [
    0x0f,
    0x11, 0x30, 0x27, 0x0,
    0x1c, 0x20, 0x2c, 0x0,
    0x00, 0x10, 0x20, 0x0,
    0x06, 0x16, 0x26, 0x0,
    0x14, 0x34, 0x0d, 0x0,
    0x00, 0x37, 0x25, 0x0,
    0x0d, 0x2d, 0x3a, 0x0,
    0x0d, 0x27, 0x2a
];

byte[] xpos = new byte[4];
byte result = 0;

pal_all(PALETTE);
ppu_on_all();

xpos[0] = 100;
xpos[1] = 90;

// (byte)(xpos[0] - xpos[1]) < 16 — the SBC must be preserved before CMP
if ((byte)(xpos[0] - xpos[1]) < 16)
{
    result = 1;
}

// Second pattern: different constant threshold
if ((byte)(xpos[0] - xpos[1]) < 32)
{
    result = 2;
}

// Use result so it's not optimized away
pal_col(0, result);

while (true) ;
