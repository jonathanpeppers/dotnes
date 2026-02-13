# Music Sample

The `samples/music` project is a simple music player that plays "The Easy Winners" by Scott Joplin. It demonstrates the NES APU (Audio Processing Unit) and the dotnes music engine.

## C# vs C

The C# sample (`Program.cs`) is designed to be functionally equivalent to a C program compiled with [cc65](https://cc65.github.io/) via [8bitworkshop.com](https://8bitworkshop.com). The reference C source is in `music.c`.

### Program.cs (C#)

```csharp
ushort[] note_table_49 = [ 4304, 4062, ... ];
set_music_pulse_table(note_table_49);

ushort[] note_table_tri = [ 2138, 2018, ... ];
set_music_triangle_table(note_table_tri);

byte[] music1 = [ 0x2a, 0x1e, 0x95, ... ];

apu_init();
start_music(music1);

while (true)
{
    ppu_wait_nmi();
    play_music();
}
```

### music.c (C — for 8bitworkshop.com)

```c
const int note_table_49[64] = { 4304, 4062, ... };
const int note_table_tri[64] = { 2138, 2018, ... };
const byte music1[] = { 0x2a, 0x1e, 0x95, ... };

void main() {
    apu_init();
    start_music(music1);
    while (1) {
        ppu_wait_nmi();
        play_music();
    }
}
```

## NESLib APIs Used

| API | Description |
|-----|-------------|
| `set_music_pulse_table(ushort[])` | Sets the note frequency table for pulse channels |
| `set_music_triangle_table(ushort[])` | Sets the note frequency table for the triangle channel |
| `apu_init()` | Initializes the APU — enables channels and silences all |
| `start_music(byte[])` | Sets the music data pointer and begins playback |
| `play_music()` | Advances one frame of music playback (call every NMI) |
| `ppu_wait_nmi()` | Waits for the next vertical blank interrupt |

## CHR ROM

The music sample has no graphics. Its `chr_generic.s` contains a single zero byte that gets padded to 8KB of zeros. Other samples use the shared `chr_generic.s` with tile/sprite data.

## ROM Layout

The transpiler emits code in this order to match cc65's layout:

```
$8000-$84FF  neslib runtime (palette, PPU, NMI handler, etc.)
$8500-$85E7  play_music subroutine (232 bytes)
$85E8-$85FD  start_music subroutine (22 bytes)
$85FE-$8610  main() — JSR apu_init, JSR start_music, loop
$8611-$86xx  donelib, copydata, popax, incsp2, popa, pusha, pushax, zerobss
$86xx-$86xx  apu_init subroutine
$86xx-$87xx  note_table_pulse (128 bytes, interleaved lo/hi)
$87xx-$88xx  note_table_triangle (128 bytes, interleaved lo/hi)
$88xx-$94xx  music1 data (3260 bytes)
$FFxx        interrupt vectors (NMI, RESET, IRQ)
```

## ROM Differences vs cc65

The transpiler output is **functionally equivalent** to a cc65-compiled ROM — both play identical music. However, 46 PRG bytes differ due to structural differences between the dotnes transpiler and cc65's linker/runtime.

### Differences Explained

| Address | Bytes | cc65 | dotnes | Reason |
|---------|-------|------|--------|--------|
| `$84FD` | 1 | `04` | `00` | `initlib` jumps to `$0304` (cc65 condes table) vs `$0300` |
| `$866E` | 1 | `04` | `00` | `donelib` same condes offset |
| `$8682` | 1 | `D6` | `DA` | `copydata` byte count: cc65 copies 42 bytes, dotnes copies 38 |
| `$86DA` | 1 | `29` | `25` | `zerobss` start address: cc65 starts at `$0329`, dotnes at `$0325` |
| `$86F4` | 1 | `05` | `00` | `zerobss` byte count: cc65 zeroes 5 bytes, dotnes zeroes 0 |
| `$94B9` | 41 | data+code | code+pad | BSS init data and start_music tail position |

### Why These Differences Exist

**cc65's DATA/BSS model:** cc65 separates initialized data (DATA segment) from zero-initialized data (BSS segment). The music sample's cc65 ROM has:
- 4 bytes of constructor/destructor metadata at `$0300-$0303` (condes table)
- BSS variables at `$0329-$032D` (5 bytes: `MUSIC_TEMP`, `MUSIC_PERIOD_LO/HI`, `MUSIC_TRI_PERIOD_LO/HI`)

**dotnes model:** The transpiler doesn't use cc65's DATA/BSS segment separation. Music state variables are at fixed addresses defined in `NESConstants.cs`, and `condes` is always `$0300`.

These differences affect only the runtime initialization sequence — the actual music engine code (`play_music`, `start_music`, `apu_init`, `main`) is **instruction-identical** between cc65 and dotnes.

### CHR ROM

The CHR ROMs match perfectly — both are 8KB of zeros (the music sample has no graphics).

## How to Verify

To compare against cc65:

1. Open `music.c` in [8bitworkshop.com](https://8bitworkshop.com) (NES platform)
2. Download the compiled ROM
3. Compare with `dotnet test` output:

```bash
# Run the music tests
dotnet test --filter "DisplayName~music"

# The received.bin will be in src/dotnes.tests/
# Compare with a hex editor or Python script
```
