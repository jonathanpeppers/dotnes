local OUT = "C:/src/dotnes4/.github/skills/nes-emu-debug/fall_debug2.txt"
local f = io.open(OUT, "w")
local frameCount = 0
local wasFalling = false
local logFrames = 0

local ACTOR_STATE = 0x3D1
local ACTOR_FLOOR = 0x3C9
local ACTOR_YY_LO = 0x3B9
local ACTOR_YY_HI = 0x3C1
local ACTOR_YVEL  = 0x3D9
local ACTOR_X     = 0x3B1
local ACTOR_XVEL  = 0x3E1
local FLOOR_YPOS  = 0x325
local FALLING = 5

emu.addEventCallback(function()
  frameCount = frameCount + 1
  
  if frameCount == 300 then
    -- Move player to floor 5, set Y to that floor's position
    local target_floor = 5
    local ypos = emu.read(FLOOR_YPOS + target_floor, emu.memType.nesInternalRam)
    local yy = ypos * 8 + 16
    emu.write(ACTOR_FLOOR, target_floor, emu.memType.nesInternalRam)
    emu.write(ACTOR_YY_LO, yy & 0xFF, emu.memType.nesInternalRam)
    emu.write(ACTOR_YY_HI, (yy >> 8) & 0xFF, emu.memType.nesInternalRam)
    emu.write(ACTOR_STATE, 1, emu.memType.nesInternalRam) -- STANDING
    f:write(string.format("Placed player on floor %d, ypos=%d, yy=0x%04X\n", target_floor, ypos, yy))
  end
  
  if frameCount == 310 then
    -- Now force fall_down
    local pfloor = emu.read(ACTOR_FLOOR, emu.memType.nesInternalRam)
    local pyy_lo = emu.read(ACTOR_YY_LO, emu.memType.nesInternalRam)
    local pyy_hi = emu.read(ACTOR_YY_HI, emu.memType.nesInternalRam)
    
    f:write(string.format("\n=== FORCING FALL from floor %d, yy=%02X%02X ===\n", pfloor, pyy_hi, pyy_lo))
    f:write("floor_ypos: ")
    for i = 0, 19 do
      f:write(string.format("%d ", emu.read(FLOOR_YPOS + i, emu.memType.nesInternalRam)))
    end
    f:write("\n")
    
    -- fall_down: floor--, state=FALLING, xvel=0, yvel=0
    emu.write(ACTOR_FLOOR, pfloor - 1, emu.memType.nesInternalRam)
    emu.write(ACTOR_STATE, FALLING, emu.memType.nesInternalRam)
    emu.write(ACTOR_YVEL, 0, emu.memType.nesInternalRam)
    emu.write(ACTOR_XVEL, 0, emu.memType.nesInternalRam)
    
    local new_floor = pfloor - 1
    local target_ypos = emu.read(FLOOR_YPOS + new_floor, emu.memType.nesInternalRam)
    local target_yy = target_ypos * 8 + 16
    f:write(string.format("Target: floor %d, ypos=%d, target_yy=0x%04X\n", new_floor, target_ypos, target_yy))
    f:write(string.format("Current yy=0x%04X, distance=%d pixels\n",
      pyy_hi * 256 + pyy_lo, pyy_hi * 256 + pyy_lo - target_yy))
    
    wasFalling = true
    logFrames = 0
  end
  
  if wasFalling and logFrames < 200 then
    local pstate = emu.read(ACTOR_STATE, emu.memType.nesInternalRam)
    local pfloor = emu.read(ACTOR_FLOOR, emu.memType.nesInternalRam)
    local pyy_lo = emu.read(ACTOR_YY_LO, emu.memType.nesInternalRam)
    local pyy_hi = emu.read(ACTOR_YY_HI, emu.memType.nesInternalRam)
    local pyvel = emu.read(ACTOR_YVEL, emu.memType.nesInternalRam)
    
    f:write(string.format("F%d: st=%d fl=%d yy=%02X%02X yvel=%d(0x%02X)\n",
      frameCount, pstate, pfloor, pyy_hi, pyy_lo, pyvel, pyvel))
    logFrames = logFrames + 1
    
    if pstate ~= FALLING and frameCount > 311 then
      f:write("=== LANDED ===\n")
      wasFalling = false
    end
  end
  
  if frameCount == 600 then
    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
