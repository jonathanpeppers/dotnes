--
-- pong_record.lua — Mesen2 Lua script for recording pong gameplay.
--
-- Simulates Player 1 controller input so the paddle tracks the ball,
-- creating a natural gameplay recording. Player 2 stays stationary.
-- Captures PNG screenshots for GIF assembly.
--
-- Usage (from samples/pong/):
--   <Mesen.exe> <pong.nes> --testRunner --enableStdout --doNotSaveSettings
--       --timeout=30 --debug.scriptWindow.allowIoOsAccess=true
--       pong_record.lua
--
-- Then assemble into GIF with:
--   python -c "
--   from PIL import Image; import glob
--   frames = [Image.open(f) for f in sorted(glob.glob('pong_frames/*.png'))]
--   frames[0].save('pong.gif', save_all=True, append_images=frames[1:],
--                   duration=67, loop=0)
--   "
--

local TOTAL_FRAMES     = 360   -- 6 seconds of gameplay at 60fps
local CAPTURE_INTERVAL = 4     -- capture every 4th frame (~15 fps GIF)
local WARMUP_FRAMES    = 30    -- let the game initialize before capturing

-- Resolve output directory relative to this script's location.
local scriptPath = debug.getinfo(1, "S").source:sub(2)
local scriptDir = scriptPath:match("(.*[/\\])") or "./"
local OUT_DIR = scriptDir .. "pong_frames"

os.execute('mkdir "' .. OUT_DIR .. '" 2>NUL')
os.execute('mkdir -p "' .. OUT_DIR .. '" 2>/dev/null')

local frameCount = 0
local captureIndex = 0

emu.addEventCallback(function()
  frameCount = frameCount + 1

  -- Read ball Y position from OAM (sprite 6, byte 0 = Y)
  local ballY = emu.read(6 * 4, emu.memType.nesSpriteRam)
  -- Read Player 1 paddle Y (sprite 0, byte 0 = Y)
  local p1y = emu.read(0, emu.memType.nesSpriteRam)

  -- Player 1: track the ball with slight deadzone for natural movement
  local p1 = {}
  if frameCount > WARMUP_FRAMES then
    if ballY > p1y + 12 then
      p1.down = true
    elseif ballY < p1y - 4 then
      p1.up = true
    end
  end
  emu.setInput(p1)

  -- Capture screenshots at interval (after warmup)
  if frameCount >= WARMUP_FRAMES and frameCount % CAPTURE_INTERVAL == 0 then
    local png = emu.takeScreenshot()
    if png then
      local filename = string.format("%s/frame_%04d.png", OUT_DIR, captureIndex)
      local f = io.open(filename, "wb")
      if f then
        f:write(png)
        f:close()
        captureIndex = captureIndex + 1
      end
    end
  end

  -- Stop after enough frames
  if frameCount >= TOTAL_FRAMES then
    emu.stop(0)
  end
end, emu.eventType.endFrame)
