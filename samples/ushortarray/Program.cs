// ushort[] array test: exercises newarr, ldelem.u2, stelem.i2 patterns.

// Set palette
pal_col(0, DarkBlue);
pal_col(1, White);

// Create ushort array and store constants
ushort[] arr = new ushort[4];
arr[0] = 100;
arr[1] = 300;
arr[2] = 1000;
arr[3] = 50000;

// Variable index load
byte idx = 1;
ushort loaded = arr[idx];

// Variable index store with constant value
arr[idx] = 310;

// Use values to write something visible on screen
vram_adr(NTADR_A(2, 2));
vram_write("USHORT ARRAY");

ppu_on_all();
while (true) ;
