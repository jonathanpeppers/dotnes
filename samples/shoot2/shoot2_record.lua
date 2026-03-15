--
-- shoot2_record.lua — Mesen2 Lua script for recording shoot2 gameplay.
--
-- Simulates Player 1 controller input: moves the ship around and fires
-- bullets at enemies, creating a natural gameplay recording.
-- Captures PNG screenshots for GIF assembly.
--
-- Usage (from repo root):
--   <Mesen.exe> <shoot2.nes> --testRunner --enableStdout --doNotSaveSettings
--       --timeout=30 --debug.scriptWindow.allowIoOsAccess=true
--       shoot2_record.lua
--
-- Then assemble into GIF with:
--   python -c "
--   from PIL import Image; import glob
--   frames = [Image.open(f) for f in sorted(glob.glob('shoot2_frames/*.png'))]
--   frames[0].save('shoot2.gif', save_all=True, append_images=frames[1:],
--                   duration=67, loop=0)
--   "
--

local TOTAL_FRAMES     = 480   -- 8 seconds of gameplay at 60fps
local CAPTURE_INTERVAL = 4     -- capture every 4th frame (~15 fps GIF)
local WARMUP_FRAMES    = 60    -- let the game initialize before capturing

-- Resolve output directory relative to this script's location.
local scriptPath = debug.getinfo(1, "S").source:sub(2)
local scriptDir = scriptPath:match("(.*[/\\])") or "./"
local OUT_DIR = scriptDir .. "shoot2_frames"

os.execute('mkdir "' .. OUT_DIR .. '" 2>NUL')
os.execute('mkdir -p "' .. OUT_DIR .. '" 2>/dev/null')

local frameCount = 0
local captureIndex = 0

emu.addEventCallback(function()
  frameCount = frameCount + 1

  local p1 = {}
  local t = frameCount - WARMUP_FRAMES

  if t > 0 then
    -- Movement pattern: weave left and right while occasionally firing
    local phase = t % 120

    if phase < 30 then
      p1.left = true
    elseif phase < 40 then
      p1.up = true
    elseif phase < 70 then
      p1.right = true
    elseif phase < 80 then
      p1.down = true
    elseif phase < 100 then
      p1.left = true
      p1.up = true
    else
      p1.right = true
      p1.down = true
    end

    -- Fire roughly every 8 frames for a steady stream of bullets
    if t % 8 < 2 then
      p1.a = true
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
