local FRAMES = 120
local OUT = "C:/Users/josep/.copilot/copilot-worktrees/dotnes/jonathanpeppers-carryable-maranda/emu_debug.txt"

local frameCount = 0
emu.addEventCallback(function()
  frameCount = frameCount + 1
  if frameCount == FRAMES then
    local f = io.open(OUT, "w")
    local state = emu.getState()
    f:write(string.format("PC=$%04X A=$%02X X=$%02X Y=$%02X SP=$%02X\n",
      state["cpu.pc"], state["cpu.a"], state["cpu.x"],
      state["cpu.y"], state["cpu.sp"]))

    f:write("\n=== Zero Page ===\n")
    for addr = 0, 0x3F do
      local val = emu.read(addr, emu.memType.nesInternalRam)
      f:write(string.format("  $%02X = $%02X\n", addr, val))
    end

    f:write("\n=== OAM (first 16 sprites) ===\n")
    for s = 0, 15 do
      local y = emu.read(s*4+0, emu.memType.nesSpriteRam)
      local t = emu.read(s*4+1, emu.memType.nesSpriteRam)
      local a = emu.read(s*4+2, emu.memType.nesSpriteRam)
      local x = emu.read(s*4+3, emu.memType.nesSpriteRam)
      f:write(string.format("  Sprite %2d: X=%3d Y=%3d Tile=$%02X Attr=$%02X\n", s, x, y, t, a))
    end

    -- Capture screen
    local pixels = emu.getScreenBuffer()
    local sf = io.open("C:/Users/josep/.copilot/copilot-worktrees/dotnes/jonathanpeppers-carryable-maranda/metasprites_screen.raw", "wb")
    for i = 1, #pixels do
      local p = pixels[i]
      sf:write(string.char((p >> 16) & 0xFF, (p >> 8) & 0xFF, p & 0xFF))
    end
    sf:close()

    f:close()
    emu.stop(0)
  end
end, emu.eventType.endFrame)
