local OUT = "C:/src/dotnes3/sram_test.txt"
local f = io.open(OUT, "w")

-- Simulate pressing A 3 times by writing directly to SRAM
emu.write(0x0000, 3, emu.memType.nesSaveRam)

-- Wait a few frames then read it back
local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == 10 then
    local val = emu.read(0x0000, emu.memType.nesSaveRam)
    f:write(string.format("SRAM[0] = %d\n", val))
    
    -- Check what the ROM reads via peek($6000)
    local ram_val = emu.read(0x0325, emu.memType.nesInternalRam)
    f:write(string.format("count local var ($0325) = %d\n", ram_val))
    
    -- Check nametable for the digit
    local tile = emu.read(12 * 32 + 9, emu.memType.nesNametableRam)
    f:write(string.format("Nametable tile at COUNT position = $%02X ('%s')\n", tile, string.char(tile)))
    
    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
