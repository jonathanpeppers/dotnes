// Test case to expose the bcd_add bug with local variable second arg
using static NESLib;

ushort score = 0x1234;  // Non-zero high byte
byte increment = 1;     // Byte local variable
score = bcd_add(score, increment);  // BUG: X not cleared, uses garbage from score's high byte
ppu_on_all();
while (true) ;
