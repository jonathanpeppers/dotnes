--
-- smoke-test.lua — Mesen2 Lua script for CI headless smoke testing.
--
-- Runs the ROM for ~5 seconds (300 frames at 60 fps) and exits cleanly.
-- Validates that the ROM loads without crashing in the emulator.
--
-- Usage:
--   <Mesen> <rom.nes> --testrunner --doNotSaveSettings --timeout=10 smoke-test.lua
--

local TOTAL_FRAMES = 300  -- ~5 seconds at 60 fps
local frameCount = 0

emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount >= TOTAL_FRAMES then
    emu.stop(0)
  end
end, emu.eventType.endFrame)
