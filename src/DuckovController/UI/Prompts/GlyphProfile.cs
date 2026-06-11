using System.Collections.Generic;
using DuckovController.UI.Common;

namespace DuckovController.UI.Prompts
{
    // Maps ButtonGlyph to controller-specific asset basenames. Swap controller = new IGlyphProfile.
    internal interface IGlyphProfile
    {
        // Subfolder under assets/glyphs/ for this controller's PNGs.
        string DirectoryName { get; }

        // Asset basename (no extension), or null if unmapped.
        string? FileFor(ButtonGlyph glyph);
    }

    internal sealed class SteamDeckGlyphProfile : IGlyphProfile
    {
        public string DirectoryName => "steamdeck";

        // Full enum mapped; missing PNGs resolve to null (GlyphProvider logs).
        // Deck naming: B=east(circle), L1/R1=bumpers, L2/R2=triggers, options=Start, view=Select.
        private static readonly Dictionary<ButtonGlyph, string> _map = new()
        {
            { ButtonGlyph.A, "steamdeck_button_a" },
            { ButtonGlyph.B, "steamdeck_button_b" },
            { ButtonGlyph.X, "steamdeck_button_x" },
            { ButtonGlyph.Y, "steamdeck_button_y" },
            { ButtonGlyph.LB, "steamdeck_button_l1" },
            { ButtonGlyph.RB, "steamdeck_button_r1" },
            { ButtonGlyph.LT, "steamdeck_button_l2" },
            { ButtonGlyph.RT, "steamdeck_button_r2" },
            { ButtonGlyph.DPad, "steamdeck_dpad" },
            { ButtonGlyph.DPadUp, "steamdeck_dpad_up" },
            { ButtonGlyph.LStick, "steamdeck_stick_l" },
            { ButtonGlyph.RStick, "steamdeck_stick_r" },
            { ButtonGlyph.Start, "steamdeck_button_options" },
            { ButtonGlyph.Select, "steamdeck_button_view" },
        };

        public string? FileFor(ButtonGlyph glyph)
            => _map.TryGetValue(glyph, out var name) ? name : null;
    }
}
