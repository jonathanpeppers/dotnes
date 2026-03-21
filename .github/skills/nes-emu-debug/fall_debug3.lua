local OUT = "C:/src/dotnes4/.github/skills/nes-emu-debug/fall_debug3.txt"
local f = io.open(OUT, "w")
local frameCount = 0
local wasFalling = false
local logFrames = 0

emu.addEventCallback(function()
  frameCount = frameCount + 1
  
  if frameCount == 300 then
    local pfloor = emu.read(0x3C9, emu.memType.nesInternalRam)
    local pyy_lo = emu.read(0x3B9, emu.memType.nesInternalRam)
    local pyy_hi = emu.read(0x3C1, emu.memType.nesInternalRam)
    
    -- Move player to floor 3
    local target_floor = 3
    local ypos = emu.read(0x325 + target_floor, emu.memType.nesInternalRam)
    local yy = ypos * 8 + 16
    emu.write(0x3C9, target_floor, emu.memType.nesInternalRam)
    emu.write(0x3B9, yy & 0xFF, emu.memType.nesInternalRam)
    emu.write(0x3C1, (yy >> 8) & 0xFF, emu.memType.nesInternalRam)
    emu.write(0x3D1, 1, emu.memType.nesInternalRam) -- STANDING
    f:write(string.format("Placed on floor %d, yy=0x%04X\n", target_floor, yy))
  end
  
  if frameCount == 310 then
    local pfloor = emu.read(0x3C9, emu.memType.nesInternalRam)
    local pyy_lo = emu.read(0x3B9, emu.memType.nesInternalRam)
    local pyy_hi = emu.read(0x3C1, emu.memType.nesInternalRam)
    f:write(string.format("\n=== FORCE FALL from floor %d yy=%02X%02X ===\n", pfloor, pyy_hi, pyy_lo))
    -- fall_down: floor--, state=FALLING, xvel=0, yvel=0
    emu.write(0x3C9, pfloor - 1, emu.memType.nesInternalRam)
    emu.write(0x3D1, 5, emu.memType.nesInternalRam) -- FALLING
    emu.write(0x3D9, 0, emu.memType.nesInternalRam) -- yvel=0
    emu.write(0x3E1, 0, emu.memType.nesInternalRam) -- xvel=0
    
    local new_floor = pfloor - 1
    local target_ypos = emu.read(0x325 + new_floor, emu.memType.nesInternalRam)
    f:write(string.format("Target: floor %d, target_yy=0x%04X\n", new_floor, target_ypos * 8 + 16))
    wasFalling = true
    logFrames = 0
  end
  
  if wasFalling and logFrames < 80 then
    local pstate = emu.read(0x3D1, emu.memType.nesInternalRam)
    local pfloor = emu.read(0x3C9, emu.memType.nesInternalRam)
    local pyy_lo = emu.read(0x3B9, emu.memType.nesInternalRam)
    local pyy_hi = emu.read(0x3C1, emu.memType.nesInternalRam)
    local pyvel = emu.read(0x3D9, emu.memType.nesInternalRam)
    f:write(string.format("F%d: st=%d fl=%d yy=%02X%02X yvel=%d(0x%02X)\n",
      frameCount, pstate, pfloor, pyy_hi, pyy_lo, pyvel, pyvel))
    logFrames = logFrames + 1
    if pstate ~= 5 and frameCount > 311 then
      f:write("=== LANDED ===\n")
      wasFalling = false
    end
  end
  
  if frameCount == 500 then f:close(); emu.stop(0) end
end, emu.eventType.endFrame)
