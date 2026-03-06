/*
Regression test: (byte)(ushort >> 8) stored to byte array.
Exercises HandleStelemI1 high-byte extraction pattern.
Previously the transpiler emitted LDA #$08 (the shift amount)
instead of extracting the actual high byte of the ushort local.
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

byte[] yhi = new byte[4];
byte[] ylo = new byte[4];

pal_all(PALETTE);
ppu_on_all();

// Runtime ushort (triggers _runtimeValueInA + _ushortInAX → IsWord)
byte py = 100;
ushort pyy = (ushort)(py * 256 + 128);

// High byte extraction: must emit LDA from pyy's hi-byte address, NOT LDA #$08
yhi[0] = (byte)(pyy >> 8);
// Low byte extraction
ylo[0] = (byte)pyy;

while (true) ;
