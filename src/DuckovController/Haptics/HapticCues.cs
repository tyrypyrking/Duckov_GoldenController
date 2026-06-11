namespace DuckovController.Haptics
{
    internal enum HapticCue
    {
        FocusTick,
        Cancel,
        PageTab,
        CarryPickup,
        CarryPlace,
        Confirm,
        SmartAction,
        Denied,
        EmptyMag,
        Heal,
        Reload,
        DamageTaken,
        WeaponFire,
        MeleeHit,
        // TEMP probe (diag task) — spike-equivalent; remove after felt/not-felt verdict.
        DebugStrong,
    }

    internal enum HapticCategory { Ui, Gameplay }

    internal readonly struct HapticProfile
    {
        public readonly float Low;        // 0..1 low-freq (left/heavy) motor
        public readonly float High;       // 0..1 high-freq (right/light) motor
        public readonly int   DurationMs;
        public readonly int   GapMs;      // gap between pulses for double-taps
        public readonly int   Pulses;
        public readonly HapticCategory Category;
        public readonly int   Priority;   // higher replaces a lower in-flight pulse

        public HapticProfile(float low, float high, int durationMs, int gapMs, int pulses,
                             HapticCategory category, int priority)
        {
            Low = low; High = high; DurationMs = durationMs; GapMs = gapMs;
            Pulses = pulses; Category = category; Priority = priority;
        }
    }

    internal static class HapticCatalog
    {
        // Steam-anchored defaults — see spec §4/§6. Hierarchy:
        // FocusTick<Cancel<PageTab<pickup/place<Confirm<SmartAction<Denied<DamageTaken.
        public static HapticProfile Get(HapticCue cue) => cue switch
        {
            HapticCue.FocusTick   => new HapticProfile(0.12f, 0.12f,  15,  0, 1, HapticCategory.Ui,       1),
            HapticCue.Cancel      => new HapticProfile(0.23f, 0.23f,  25,  0, 1, HapticCategory.Ui,       2),
            HapticCue.PageTab     => new HapticProfile(0.28f, 0.28f,  30,  0, 1, HapticCategory.Ui,       2),
            HapticCue.CarryPickup => new HapticProfile(0.30f, 0.30f,  25,  0, 1, HapticCategory.Ui,       3),
            HapticCue.CarryPlace  => new HapticProfile(0.34f, 0.34f,  20,  0, 1, HapticCategory.Ui,       3),
            HapticCue.Confirm     => new HapticProfile(0.38f, 0.38f,  35,  0, 1, HapticCategory.Ui,       4),
            HapticCue.SmartAction => new HapticProfile(0.42f, 0.42f,  35,  0, 1, HapticCategory.Ui,       4),
            HapticCue.Denied      => new HapticProfile(0.45f, 0.45f,  15, 30, 2, HapticCategory.Ui,       5),
            HapticCue.EmptyMag    => new HapticProfile(0.12f, 0.12f,  10,  0, 1, HapticCategory.Gameplay, 2),
            HapticCue.Heal        => new HapticProfile(0.23f, 0.23f, 100,  0, 1, HapticCategory.Gameplay, 3),
            HapticCue.Reload      => new HapticProfile(0.18f, 0.53f,  40,  0, 1, HapticCategory.Gameplay, 3),
            HapticCue.DamageTaken  => new HapticProfile(0.76f, 0.76f,  80,  0, 1, HapticCategory.Gameplay, 6),
            HapticCue.WeaponFire   => new HapticProfile(0.55f, 0.55f,  55,  0, 1, HapticCategory.Gameplay, 5),
            HapticCue.MeleeHit     => new HapticProfile(0.60f, 0.60f,  70,  0, 1, HapticCategory.Gameplay, 5),
            HapticCue.DebugStrong  => new HapticProfile(0.40f, 0.40f, 200,  0, 1, HapticCategory.Ui,       9),
            _                      => new HapticProfile(0f,    0f,      0,  0, 0, HapticCategory.Ui,       0),
        };
    }
}
