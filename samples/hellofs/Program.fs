open NES

NESLib.pal_col(0uy,0x02uy)  // set screen to dark blue
NESLib.pal_col(1uy,0x14uy) // fuchsia
NESLib.pal_col(2uy,0x20uy) // grey
NESLib.pal_col(3uy,0x30uy) // white

// write text to name table
NESLib.vram_adr(NESLib.NTADR_A(2uy, 2uy))    // set address
NESLib.vram_write "HELLO, WORLD!" // write bytes to video RAM

// enable PPU rendering (turn on screen)
NESLib.ppu_on_all()

// infinite loop
while true do
  ()
