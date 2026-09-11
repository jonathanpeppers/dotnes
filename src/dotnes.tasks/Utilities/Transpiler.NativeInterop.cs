using dotnes.ObjectModel;

namespace dotnes;

partial class Transpiler
{
    void ValidateNativeRenderer(ILInstruction[] instructions)
    {
        foreach (var instruction in instructions.Concat(UserMethods.Values.SelectMany(body => body)))
        {
            if (instruction.String?.StartsWith("OamScope.", StringComparison.Ordinal) == true)
                throw new TranspileException(
                    "'OamScope' uses the stock renderer and cannot be combined with ppu_use_native_renderer(). " +
                    "Use a native-owned OAM buffer, or remove the native renderer directive.");
        }

        foreach (string method in UsedMethods.OrderBy(name => name, StringComparer.Ordinal))
        {
            bool ownsStockGraphics =
                method.StartsWith("pal_", StringComparison.Ordinal) ||
                method.StartsWith("oam_", StringComparison.Ordinal) ||
                method.StartsWith("vrambuf_", StringComparison.Ordinal) ||
                method is nameof(NESLib.ppu_off) or nameof(NESLib.ppu_on_all) or
                    nameof(NESLib.ppu_on_bg) or nameof(NESLib.ppu_on_spr) or nameof(NESLib.ppu_mask) or
                    nameof(NESLib.scroll) or nameof(NESLib.split) or
                    nameof(NESLib.set_scroll_x) or nameof(NESLib.set_scroll_y) or
                    nameof(NESLib.bank_bg) or nameof(NESLib.bank_spr) or nameof(NESLib.vram_inc) or
                    nameof(NESLib.set_vram_update) or nameof(NESLib.flush_vram_update) or
                    nameof(NESLib.one_vram_buffer) or nameof(NESLib.multi_vram_buffer_horz) or
                    nameof(NESLib.multi_vram_buffer_vert) or nameof(NESLib.clear_vram_buffer) or
                    nameof(NESLib.fade_in) or nameof(NESLib.fade_out) or
                    nameof(NESLib.set_ppu_ctrl_var) or
                    "get_oam_off" or "set_oam_off";
            if (ownsStockGraphics)
            {
                throw new TranspileException(
                    $"'{method}' uses the stock renderer and cannot be combined with ppu_use_native_renderer(). " +
                    "Use native graphics I/O, or remove the native renderer directive.");
            }
        }
    }
}
