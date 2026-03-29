local outPath = "C:/src/dotnes"
local f = io.open(outPath .. "/climb_test.txt", "w")

emu.addEventCallback(function()
  local fc = emu.getState()["ppu.frameCount"]
  
  -- Climb up aggressively: alternate UP and A to find ladders and jump
  if fc > 120 then
    if fc % 30 < 15 then
      emu.setInput(0, {up = true})
    else
      emu.setInput(0, {a = true, right = true})
    end
  end
  
  -- Log state every 300 frames
  if fc % 300 == 0 and fc > 0 then
    local sp_lo = emu.read(0x22, emu.memType.nesInternalRam)
    local sp_hi = emu.read(0x23, emu.memType.nesInternalRam)
    local floor = emu.read(0x03C9, emu.memType.nesInternalRam)
    local state = emu.read(0x03D1, emu.memType.nesInternalRam)
    local pc = emu.getState()["cpu.pc"]
    f:write(string.format("Frame %5d: floor=%d state=%d sp=$%02X%02X pc=$%04X\n", fc, floor, state, sp_hi, sp_lo, pc))
  end
  
  if fc >= 18000 then
    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
