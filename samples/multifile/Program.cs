/*
Multi-file demo.
Demonstrates splitting a NES program across multiple .cs files.
The main program calls helper methods defined in separate static classes.
*/

// setup palette
Palette.setup();

// write text to nametable
Display.write_message();

// enable PPU rendering
Display.enable();

// infinite loop
while (true) ;
