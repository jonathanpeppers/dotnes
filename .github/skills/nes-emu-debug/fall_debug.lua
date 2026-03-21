local OUT = "C:/src/dotnes4/.github/skills/nes-emu-debug/fall_debug.txt"
local f = io.open(OUT, "w")
local frameCount = 0
local wasFalling = false
local logFrames = 0

-- Actor memory addresses (from disassembly)
local ACTOR_STATE = 0x3D1   -- 8 bytes
local ACTOR_FLOOR = 0x3C9   -- 8 bytes
local ACTOR_YY_LO = 0x3B9   -- 8 bytes
local ACTOR_YY_HI = 0x3C1   -- 8 bytes
local ACTOR_YVEL  = 0x3D9   -- 8 bytes
local ACTOR_X     = 0x3B1   -- 8 bytes
local FLOOR_YPOS  = 0x325   -- 20 bytes

local FALLING = 5

emu.addEventCallback(function()
  frameCount = frameCount + 1
  
  local pstate = emu.read(ACTOR_STATE, emu.memType.nesInternalRam)
  local pfloor = emu.read(ACTOR_FLOOR, emu.memType.nesInternalRam)
  local pyy_lo = emu.read(ACTOR_YY_LO, emu.memType.nesInternalRam)
  local pyy_hi = emu.read(ACTOR_YY_HI, emu.memType.nesInternalRam)
  local pyvel = emu.read(ACTOR_YVEL, emu.memType.nesInternalRam)
  local px = emu.read(ACTOR_X, emu.memType.nesInternalRam)
  
  -- After 300 frames (game initialized), force a fall by setting state=FALLING
  if frameCount == 300 then
    f:write("=== FORCING FALL at frame 300 ===\n")
    f:write(string.format("Before: state=%d floor=%d yy=%02X%02X yvel=%d x=%d\n",
      pstate, pfloor, pyy_hi, pyy_lo, pyvel, px))
    
    -- Dump floor data
    f:write("floor_ypos: ")
    for i = 0, 19 do
      f:write(string.format("%d ", emu.read(FLOOR_YPOS + i, emu.memType.nesInternalRam)))
    end
    f:write("\n")
    
    -- Simulate fall_down: floor--, state=FALLING, xvel=0, yvel=0
    if pfloor > 0 then
      emu.write(ACTOR_FLOOR, pfloor - 1, emu.memType.nesInternalRam)
    end
    emu.write(ACTOR_STATE, FALLING, emu.memType.nesInternalRam)
    emu.write(ACTOR_YVEL, 0, emu.memType.nesInternalRam)
    emu.write(0x3E1, 0, emu.memType.nesInternalRam)  -- xvel=0
    
    local new_floor = emu.read(ACTOR_FLOOR, emu.memType.nesInternalRam)
    local target_ypos = emu.read(FLOOR_YPOS + new_floor, emu.memType.nesInternalRam)
    f:write(string.format("After: floor=%d target_ypos=%d target_yy=0x%04X\n",
      new_floor, target_ypos, target_ypos * 8 + 16))
    f:write(string.format("Current yy=0x%04X, distance=%d pixels\n",
      pyy_hi * 256 + pyy_lo, pyy_hi * 256 + pyy_lo - (target_ypos * 8 + 16)))
    
    wasFalling = true
    logFrames = 0
  end
  
  -- Log every frame during the fall
  if wasFalling and logFrames < 120 then
    pstate = emu.read(ACTOR_STATE, emu.memType.nesInternalRam)
    pfloor = emu.read(ACTOR_FLOOR, emu.memType.nesInternalRam)
    pyy_lo = emu.read(ACTOR_YY_LO, emu.memType.nesInternalRam)
    pyy_hi = emu.read(ACTOR_YY_HI, emu.memType.nesInternalRam)
    pyvel = emu.read(ACTOR_YVEL, emu.memType.nesInternalRam)
    
    f:write(string.format("F%d: st=%d fl=%d yy=%02X%02X yvel=%d(0x%02X)\n",
      frameCount, pstate, pfloor, pyy_hi, pyy_lo, pyvel, pyvel))
    logFrames = logFrames + 1
    
    if pstate ~= FALLING then
      f:write("=== LANDED ===\n")
      wasFalling = false
    end
  end
  
  if frameCount == 500 then
    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
