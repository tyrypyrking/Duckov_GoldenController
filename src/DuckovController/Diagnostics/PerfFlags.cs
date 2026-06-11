using DuckovController.Config;

namespace DuckovController.Diagnostics
{
    // Static mirror of PerfConfig. Hot-reloaded by ModBehaviour on config change; read at the top of every
    // per-frame path. Static so no per-component cfg plumbing is required.
    internal static class PerfFlags
    {
        internal static bool MenuOverlay    = true;
        internal static bool MenuController = true;
        internal static bool BackGlyph      = true;
        internal static bool GlyphInjectors = true;
        internal static bool AimDriver      = true;
        internal static bool Throwables     = true;
        internal static bool GameplayInput  = true;
        internal static bool BulletColor    = true;
        internal static bool GridFocus      = true;
        internal static bool SmartHeal      = true;
        internal static bool Cutscene       = true;
        internal static bool MiniGameGate   = true;

        internal static void Apply(PerfConfig? p)
        {
            if (p == null) return;
            MenuOverlay    = p.EnableMenuOverlay;
            MenuController = p.EnableMenuController;
            BackGlyph      = p.EnableBackGlyph;
            GlyphInjectors = p.EnableGlyphInjectors;
            AimDriver      = p.EnableAimDriver;
            Throwables     = p.EnableThrowables;
            GameplayInput  = p.EnableGameplayInput;
            BulletColor    = p.EnableBulletColor;
            GridFocus      = p.EnableGridFocus;
            SmartHeal      = p.EnableSmartHeal;
            Cutscene       = p.EnableCutscene;
            MiniGameGate   = p.EnableMiniGameGate;
            PerfHud.Enabled = p.LogFps;

            // Log only disabled subsystems so a bisection run's Player.log shows exactly what was off.
            var off = new System.Collections.Generic.List<string>();
            if (!MenuOverlay)    off.Add("MenuOverlay");
            if (!MenuController) off.Add("MenuController");
            if (!BackGlyph)      off.Add("BackGlyph");
            if (!GlyphInjectors) off.Add("GlyphInjectors");
            if (!AimDriver)      off.Add("AimDriver");
            if (!Throwables)     off.Add("Throwables");
            if (!GameplayInput)  off.Add("GameplayInput");
            if (!BulletColor)    off.Add("BulletColor");
            if (!GridFocus)      off.Add("GridFocus");
            if (!SmartHeal)      off.Add("SmartHeal");
            if (!Cutscene)       off.Add("Cutscene");
            if (!MiniGameGate)   off.Add("MiniGameGate");
            Log.Info(off.Count == 0
                ? "PerfFlags: all subsystems ENABLED."
                : $"PerfFlags: DISABLED → {string.Join(", ", off)}");
        }
    }
}
