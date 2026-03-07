---
name: nes-emu-debug
description: >-
  Run NES ROMs in the Mesen2 emulator to debug runtime behavior. Use this skill
  whenever the user wants to run a ROM and inspect what actually happens at runtime:
  read NES memory (RAM, palette, nametable, OAM), dump CPU/PPU/APU state after
  N frames, capture the screen buffer, compare runtime behavior between two ROMs,
  or verify that a sample displays correctly. Also use when the user says things like
  "run the ROM", "what does the screen look like", "check the palette", "inspect
  nametable", "read zero page", "dump memory", or "the ROM doesn't display correctly".
  This is the dynamic/runtime counterpart to nes-rom-debug (which does static
  binary analysis).
---

# NES Emulator Debug (Mesen2)

Run NES ROMs headlessly in Mesen2's test runner to inspect runtime state, memory,
and screen output. This complements the `nes-rom-debug` skill (static binary
analysis) with dynamic runtime inspection.

## Prerequisites

Build the Mesen2 package first:

```powershell
dotnet build src/dotnes.mesen/dotnes.mesen.csproj
```

This downloads Mesen2 to `src/dotnes.mesen/obj/Debug/mesen/`. The executable is:
- **Windows:** `src/dotnes.mesen/obj/Debug/mesen/Mesen.exe`
- **Linux/macOS:** `src/dotnes.mesen/obj/Debug/mesen/Mesen`

## How It Works

Mesen2 has a **headless test runner** mode that loads a ROM, runs a Lua script,
and exits — no window, no GUI. The Lua script has full access to the emulator API:
CPU/PPU/APU state, all memory types, screen buffer, frame callbacks, etc.

### Basic Invocation

```powershell
# Find the Mesen executable (works on any OS / config)
$mesenDir = Get-ChildItem "src/dotnes.mesen/obj/*/mesen" -Directory | Select-Object -First 1
$mesenExe = if ($IsWindows -or $env:OS -match 'Windows') {
  Join-Path $mesenDir "Mesen.exe"
} else {
  Join-Path $mesenDir "Mesen"
}
$mesen = (Resolve-Path $mesenExe).Path
$rom   = (Resolve-Path "path/to/rom.nes").Path
$script = (Resolve-Path "path/to/script.lua").Path

$proc = Start-Process -FilePath $mesen -ArgumentList `
  "--testRunner",            # Headless mode (no window)
  "--enableStdout",          # Show ROM loading info on stdout
  "--doNotSaveSettings",     # Don't persist config changes
  "--timeout=30",            # Kill after N seconds if script doesn't call emu.stop()
  "--debug.scriptWindow.allowIoOsAccess=true",  # Enable Lua file I/O
  $script, $rom `
  -PassThru -RedirectStandardOutput out.txt -RedirectStandardError err.txt -NoNewWindow
$proc.WaitForExit(35000)
# Exit code comes from emu.stop(N) in the Lua script, or -1 on timeout
```

### Key Command-Line Flags

| Flag | Purpose |
|------|---------|
| `--testRunner` | Headless mode — no window, runs at max speed, exits via `emu.stop(code)` |
| `--enableStdout` | Print emulator log (ROM info, mapper details) to stdout |
| `--doNotSaveSettings` | Don't write settings.json (safe for automation) |
| `--timeout=N` | Kill if script doesn't exit within N seconds (default: 100) |
| `--debug.scriptWindow.allowIoOsAccess=true` | **Required** for Lua `io.open`/`os.*` functions |

## Lua Script API Reference

### State Access

The state table returned by `emu.getState()` uses **flat dotted-string keys**, not
nested tables. Access fields with bracket notation:

```lua
local state = emu.getState()
-- CORRECT:
local pc = state["cpu.pc"]
local scanline = state["ppu.scanline"]
-- WRONG (nil!):
-- local pc = state.cpu.pc
```

Key state fields:

| Key | Type | Description |
|-----|------|-------------|
| `cpu.pc` | number | Program counter |
| `cpu.a` | number | Accumulator |
| `cpu.x` | number | X index register |
| `cpu.y` | number | Y index register |
| `cpu.sp` | number | Stack pointer |
| `cpu.ps` | number | Processor status flags |
| `cpu.cycleCount` | number | Total CPU cycles elapsed |
| `ppu.scanline` | number | Current PPU scanline |
| `ppu.cycle` | number | Current PPU cycle within scanline |
| `ppu.frameCount` | number | Total PPU frames rendered |
| `ppu.control.*` | various | PPU control register bits |
| `ppu.mask.*` | various | PPU mask register bits |
| `ppu.statusFlags.*` | various | PPU status flags |
| `frameCount` | number | Emulator frame count |
| `masterClock` | number | Master clock ticks |

### Memory Types

Use `emu.read(address, memType)` to read memory. The `address` is relative to the
start of that memory region (not the CPU/PPU mapped address).

| Memory Type | Enum Value | Size | Description |
|-------------|-----------|------|-------------|
| `emu.memType.nesInternalRam` | 46 | 2 KB | CPU RAM ($0000-$07FF). **Use this for zero page.** |
| `emu.memType.nesPaletteRam` | 53 | 32 B | Background + sprite palette (addr 0-31) |
| `emu.memType.nesNametableRam` | 49 | 2 KB | VRAM nametables (addr 0 = NT0 tile 0,0) |
| `emu.memType.nesSpriteRam` | 51 | 256 B | OAM — 64 sprites × 4 bytes each |
| `emu.memType.nesPrgRom` | 45 | varies | PRG ROM (addr 0 = first PRG byte) |
| `emu.memType.nesChrRom` | 55 | varies | CHR ROM pattern tables |
| `emu.memType.nesChrRam` | 54 | varies | CHR RAM (if mapper uses RAM instead of ROM) |
| `emu.memType.nesWorkRam` | 47 | varies | Battery-backed / work RAM |
| `emu.memType.nesSaveRam` | 48 | varies | Save RAM |
| `emu.memType.nesMemory` | 8 | 64 KB | Full CPU address space (mapped, may cause side effects) |
| `emu.memType.nesPpuMemory` | 9 | 16 KB | Full PPU address space (mapped) |

**Important:** For reading zero page / RAM, use `nesInternalRam` (direct access),
not `nesMemory` (which goes through the CPU bus and may return stale/zero values in
test runner mode). Similarly, use `nesPaletteRam` for palette, not `nesPpuMemory`.

### Frame Callbacks

Register a callback to run after each frame:

```lua
local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == 120 then
    -- Do your inspection here
    emu.stop(0)  -- exit with code 0
  end
end, emu.eventType.endFrame)
```

### Screen Buffer

```lua
local pixels = emu.getScreenBuffer()
-- Returns a Lua table of 61440 ARGB integer values (256 × 240 NES resolution)
-- Each pixel: (A << 24) | (R << 16) | (G << 8) | B
```

To save as raw RGB file:
```lua
local sf = io.open("screen.raw", "wb")
for i = 1, #pixels do
  local p = pixels[i]
  sf:write(string.char((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF))
end
sf:close()
-- Result: 184320 bytes (256 × 240 × 3 RGB), convertible with ImageMagick/ffmpeg
```

Convert to PNG (from PowerShell):
```powershell
# Using Python PIL/Pillow
python -c "
from PIL import Image
raw = open('screen.raw','rb').read()
img = Image.frombytes('RGB', (256,240), raw)
img.save('screenshot.png')
"
# Or using ffmpeg
ffmpeg -f rawvideo -pixel_format rgb24 -video_size 256x240 -i screen.raw screenshot.png
```

### Exiting

```lua
emu.stop(0)    -- exit test runner with code 0 (success)
emu.stop(1)    -- exit with code 1 (failure)
emu.stop(42)   -- any integer exit code
```

The exit code is returned as the process exit code, so you can check it from
PowerShell via `$proc.ExitCode`.

## Lua Script Templates

### Template 1: Dump CPU State After N Frames

```lua
-- dump_state.lua — Dump CPU/PPU state after N frames
-- Usage: Mesen.exe --testRunner ... dump_state.lua rom.nes
local FRAMES = 120  -- wait 2 seconds (60 fps)
local OUT_FILE = "OUTPUT_PATH"  -- replace with absolute path using forward slashes

local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == FRAMES then
    local state = emu.getState()
    local f = io.open(OUT_FILE, "w")

    f:write("=== CPU State ===\n")
    f:write(string.format("PC=$%04X  A=$%02X  X=$%02X  Y=$%02X  SP=$%02X  PS=$%02X\n",
      state["cpu.pc"], state["cpu.a"], state["cpu.x"],
      state["cpu.y"], state["cpu.sp"], state["cpu.ps"]))
    f:write("Cycles: " .. state["cpu.cycleCount"] .. "\n")

    f:write("\n=== PPU State ===\n")
    f:write("Scanline: " .. state["ppu.scanline"] .. "  Cycle: " .. state["ppu.cycle"] .. "\n")
    f:write("Frame: " .. state["ppu.frameCount"] .. "\n")
    f:write("NMI enabled: " .. tostring(state["ppu.control.nmiOnVerticalBlank"]) .. "\n")
    f:write("BG enabled: " .. tostring(state["ppu.mask.backgroundEnabled"]) .. "\n")
    f:write("Sprites enabled: " .. tostring(state["ppu.mask.spritesEnabled"]) .. "\n")

    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
```

### Template 2: Read NES Library Zero Page Variables

The NES library stores state in zero page. Key addresses
(from `src/dotnes.tasks/Utilities/NESConstants.cs`):

| Address | Name | Description |
|---------|------|-------------|
| $01 | STARTUP | Startup flag |
| $02 | NES_PRG_BANKS | Number of PRG banks |
| $03 | VRAM_UPDATE | Non-zero = VRAM update pending |
| $04-$05 | NAME_UPD_ADR | Nametable update address (16-bit) |
| $06 | NAME_UPD_ENABLE | Nametable update enable flag |
| $07 | PAL_UPDATE | Palette update pending |
| $08-$09 | PAL_BG_PTR | Background palette pointer (16-bit) |
| $0A-$0B | PAL_SPR_PTR | Sprite palette pointer (16-bit) |
| $0C | SCROLL_X | Horizontal scroll position |
| $0D | SCROLL_Y | Vertical scroll position |
| $0E | SCROLL_X1 | split() saved X scroll |
| $0F | PPU_CTRL_VAR1 | split() saved PPU_CTRL |
| $10 | PRG_FILEOFFS | PRG file offset |
| $12 | PPU_MASK_VAR | Shadow of PPU mask register |
| $14-$16 | NMI_CALLBACK | JMP opcode + address for NMI callback |
| $17 | TEMP | Temporary variable |
| $18 | TEMP_HI | Temp high byte / DUP_TEMP |
| $19 | TEMP2 | Additional temp |
| $1A | TEMP3 | Additional temp |
| $1B | OAM_OFF | OAM buffer offset |
| $1C | UPDPTR | VRAM update buffer index |
| $22 | sp | C stack pointer |
| $3C | RAND_SEED | Random seed for PRNG |

```lua
-- read_zeropage.lua
local f = io.open("OUTPUT_PATH", "w")

local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == 120 then
    f:write("=== NES Library Zero Page ===\n")
    -- Names from src/dotnes.tasks/Utilities/NESConstants.cs
    local names = {
      [0x01]="STARTUP", [0x02]="NES_PRG_BANKS", [0x03]="VRAM_UPDATE",
      [0x04]="NAME_UPD_ADR", [0x06]="NAME_UPD_ENABLE", [0x07]="PAL_UPDATE",
      [0x08]="PAL_BG_PTR", [0x0A]="PAL_SPR_PTR",
      [0x0C]="SCROLL_X", [0x0D]="SCROLL_Y", [0x0E]="SCROLL_X1",
      [0x0F]="PPU_CTRL_VAR1", [0x10]="PRG_FILEOFFS", [0x12]="PPU_MASK_VAR",
      [0x14]="NMI_CALLBACK", [0x17]="TEMP", [0x18]="TEMP_HI",
      [0x19]="TEMP2", [0x1A]="TEMP3", [0x1B]="OAM_OFF", [0x1C]="UPDPTR",
      [0x22]="sp", [0x3C]="RAND_SEED"
    }

    for addr = 0, 31 do
      local val = emu.read(addr, emu.memType.nesInternalRam)
      local name = names[addr] or ""
      f:write(string.format("  $%02X = $%02X  %s\n", addr, val, name))
    end

    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
```

### Template 3: Dump Palette

```lua
-- dump_palette.lua
local f = io.open("OUTPUT_PATH", "w")

local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == 120 then
    f:write("=== Palette RAM ===\n")
    f:write("Background palettes:\n")
    for pal = 0, 3 do
      f:write(string.format("  Palette %d: ", pal))
      for i = 0, 3 do
        local val = emu.read(pal * 4 + i, emu.memType.nesPaletteRam)
        f:write(string.format("$%02X ", val))
      end
      f:write("\n")
    end

    f:write("Sprite palettes:\n")
    for pal = 0, 3 do
      f:write(string.format("  Palette %d: ", pal + 4))
      for i = 0, 3 do
        local val = emu.read(16 + pal * 4 + i, emu.memType.nesPaletteRam)
        f:write(string.format("$%02X ", val))
      end
      f:write("\n")
    end

    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
```

### Template 4: Dump Nametable Region

```lua
-- dump_nametable.lua — Read a rectangular region of the nametable
local NT_BASE = 0       -- 0 for nametable 0, 0x400 for nametable 1
local START_ROW = 0
local END_ROW = 29      -- 30 rows total
local START_COL = 0
local END_COL = 31      -- 32 columns total

local f = io.open("OUTPUT_PATH", "w")

local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == 120 then
    f:write("=== Nametable ===\n")
    for row = START_ROW, END_ROW do
      f:write(string.format("Row %2d: ", row))
      for col = START_COL, END_COL do
        local addr = NT_BASE + row * 32 + col
        local tile = emu.read(addr, emu.memType.nesNametableRam)
        if tile == 0 then
          f:write(".. ")
        else
          f:write(string.format("%02X ", tile))
        end
      end
      f:write("\n")
    end

    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
```

### Template 5: Capture Screenshot as Raw RGB

```lua
-- capture_screen.lua
local FRAMES = 120
local RAW_FILE = "OUTPUT_PATH.raw"

local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == FRAMES then
    local pixels = emu.getScreenBuffer()
    local sf = io.open(RAW_FILE, "wb")
    for i = 1, #pixels do
      local p = pixels[i]
      sf:write(string.char((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF))
    end
    sf:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
```

## Common Debugging Workflows

### "The ROM doesn't display correctly"

1. Build the sample and run it in Mesen:
   ```powershell
   cd samples/<name> && dotnet build
   ```
2. Write a Lua script that waits 120 frames, then dumps:
   - Palette RAM (are the colors correct?)
   - Nametable (are tiles where expected?)
   - Zero page (did NES lib vars initialize?)
   - Screen buffer (what does it actually look like?)
3. Compare palette values against what the C# code specifies via `pal_col()`
4. Compare nametable tiles against what `vram_write()` should have written

### "Compare runtime behavior of two ROMs"

Run the same Lua script against both ROMs and diff the output:
```powershell
# Run against cc65 reference ROM
Start-Process $mesen ... reference.nes  # writes to ref_state.txt
# Run against dotnes ROM
Start-Process $mesen ... dotnes.nes     # writes to dotnes_state.txt
# Compare
Compare-Object (Get-Content ref_state.txt) (Get-Content dotnes_state.txt)
```

### "Check if a specific memory address has the expected value"

Write a targeted Lua script:
```lua
local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == 120 then
    local val = emu.read(0x0325, emu.memType.nesInternalRam)
    if val == expected then
      emu.stop(0)  -- pass
    else
      emu.stop(1)  -- fail
    end
  end
end, emu.eventType.endFrame)
```

Then check `$proc.ExitCode` — 0 means the value matched.

## Tips

- **Use forward slashes** in Lua file paths — Windows Lua handles them fine and
  avoids double-backslash escaping headaches.
- **Use absolute paths** for output files. The test runner's working directory may
  differ from where you launched it. Resolve paths in PowerShell first, then
  embed them in the Lua script.
- **PowerShell string interpolation**: Use `@"..."@` (expandable here-string) to
  embed paths into Lua scripts. Replace `\` with `/` first.
  **Note:** The closing `"@` must be at column 1 (no leading whitespace):
  ```powershell
  $outPath = ($PWD.Path) -replace '\\','/'
  $luaScript = @"
  local f = io.open("$outPath/output.txt", "w")
  "@
  ```
- **Frame count**: 60 frames ≈ 1 second of NES time. Most samples finish
  initialization within 60-120 frames.
- **Exit code -1** means timeout — your script didn't call `emu.stop()` within the
  `--timeout` period. Check for Lua errors by wrapping code in `pcall()` and
  writing error messages to a file.
- **Palette color 0x0F** is black in the NES palette. If all non-background palette
  entries are 0x0F, the sample likely didn't call `pal_col()`.
- **Screen buffer** is always 256×240 (standard NES resolution) regardless of
  overscan settings.
