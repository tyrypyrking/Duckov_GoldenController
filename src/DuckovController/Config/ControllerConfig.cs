using Newtonsoft.Json;

namespace DuckovController.Config
{
    public sealed class ControllerConfig
    {
        public AimConfig Aim { get; set; } = new AimConfig();
        public BindingsConfig Bindings { get; set; } = new BindingsConfig();
        public UiConfig Ui { get; set; } = new UiConfig();
        public DiagnosticsConfig Diagnostics { get; set; } = new DiagnosticsConfig();
        public SmartTakeRules SmartTake { get; set; } = new SmartTakeRules();
        public SmartHealRules SmartHeal { get; set; } = new SmartHealRules();
        public CostDisplayRules CostDisplay { get; set; } = new CostDisplayRules();
        public HapticsConfig Haptics { get; set; } = new HapticsConfig();
        public AutoAimConfig AutoAim { get; set; } = new AutoAimConfig();
        public ThrowConfig Throw { get; set; } = new ThrowConfig();
        public ScopeConfig Scope { get; set; } = new ScopeConfig();
        public RecoilConfig Recoil { get; set; } = new RecoilConfig();
        public BiasRingConfig BiasRing { get; set; } = new BiasRingConfig();
        public PerfConfig Perf { get; set; } = new PerfConfig();
    }

    // Per-subsystem kill-switches for FPS bisection. All default true; hot-reloads via file-watcher →
    // PerfFlags.Apply. Static PerfFlags mirror means no per-component cfg plumbing.
    public sealed class PerfConfig
    {
        public bool EnableMenuOverlay    { get; set; } = true;  // MenuFocusOverlay.Update (effective-root + confirm scans)
        public bool EnableMenuController { get; set; } = true;  // MenuFocusController.Update (splash/scene-loader/nav)
        public bool EnableBackGlyph      { get; set; } = true;  // MenuBackGlyphInjector.Update (4Hz back-button scan)
        public bool EnableGlyphInjectors { get; set; } = true;  // Bullet/MiniGame glyph OnAfterRefresh handlers
        public bool EnableAimDriver      { get; set; } = true;  // AimDriverPatch.Postfix (gameplay aim)
        public bool EnableThrowables     { get; set; } = true;  // ThrowableController (gameplay throwables)
        public bool EnableGameplayInput  { get; set; } = true;  // GameplayInputDriverPatch.Postfix (gameplay verbs)
        public bool EnableBulletColor    { get; set; } = true;  // BulletTypeColorPatch.Postfix (ammo-HUD tint)
        public bool EnableGridFocus      { get; set; } = true;  // GridFocusController.Update (View grid nav)
        public bool EnableSmartHeal      { get; set; } = true;  // SmartHealController.Update
        public bool EnableCutscene       { get; set; } = true;  // CutsceneDialogueHandler.Update
        public bool EnableMiniGameGate   { get; set; } = true;  // MiniGameInputGate.Tick

        // Boot-time skips (read once in OnAfterSetup; NOT hot-reloadable — require restart).
        public bool LogFps               { get; set; } = false; // true → PerfHud logs 1 Hz fps/frametime to Player.log
        public bool ApplyHarmonyPatches  { get; set; } = true;  // false → skip Harmony.PatchAll entirely
        // Default OFF: ~5.7ms/frame (InputSystem processes all bindings each frame); redundant with Harmony
        // direct-drive + Gamepad.current polling. Flip true only if a native handler requires a binding.
        public bool ApplyGamepadBindings { get; set; } = false; // false → don't inject bindings into the action asset
    }

    // Smart-Heal Rank-9 item-selection preference among heals that COVER the missing HP.
    public enum HealPickMode
    {
        Off,        // vanilla: largest heal that covers the wound (fast chains)
        Price,      // cheapest per-unit sell price among heals that cover
        HealAmount, // least overshoot: smallest heal value that still covers (default)
    }

    // Smart-Heal rule set.
    public sealed class SmartHealRules
    {
        public bool Enabled                 = true;

        // Bleed handling
        public bool   BleedAggressive       = true;
        public int    BleedDangerHpThreshold = 35;
        public float  BleedDangerHpFraction = 0.30f;

        // Per-debuff treat flags
        public bool   TreatPain             = true;
        public bool   TreatFire             = true;
        public bool   TreatElectric         = true;
        public bool   TreatSpace            = true;
        public bool   TreatPoison           = true;
        public bool   TreatCold             = true;

        // HP restore
        public bool   HealMissingHp         = true;

        // HP-restore item preference among heals that cover the wound.
        // HealAmount (default) = least overshoot; Price = cheapest per-unit sell price;
        // Off = vanilla largest-sufficient. See SmartHealEngine.PickHeal.
        public HealPickMode HealPick = HealPickMode.HealAmount;

        // Buff at full HP
        public bool   AutoBuffAtFullHp      = true;
        public int    HotbarSlotCount       = 6;

        // Input
        public float  TapWindowSec          = 0.08f;
        public float  StartWatchdogSec      = 0.25f;

        // Chain (LB hold)
        public bool   QueueOnHold           = true;
        public string[] QueueCancelButtons  = new[] { "buttonSouth", "buttonEast",
                                                       "leftTrigger", "rightTrigger" };
        public float  NoOpAudioCooldownSec  = 0.60f;

        // Audio
        public bool   AudioOnNoOp           = true;
        public string NoOpAudioEvent        = "UI/cancel";
    }

    // Smart-Take rule set. Gates AND together; includes OR together.
    // Predicate IDs from decompiled source:
    //   - Quest/Wishlist/Building-required → ItemWishlist.Instance.Is*(TypeID).
    //   - Ammo → Tag.Bullet (GameplayDataSettings.Tags.Bullet) + caliber match.
    //   - AboveValue → Item.Value (NOTE: "SellPrice" property doesn't exist).
    // IncludeTags is an escape hatch for tag strings discovered post-deploy.
    public sealed class SmartTakeRules
    {
        // Master
        public bool Enabled                     = true;  // master switch; when off, X = vanilla take-all

        // Gates (AND)
        public bool RespectActiveFilter         = true;  // only path for untagged categories (Heal/Currency have no canonical tag)
        public bool SkipLockedInventoryIndices  = true;
        public bool RequireInspected            = true;
        public bool SkipPetSlot                 = true;  // pet is a curated safe-slot, never auto-fill.

        // Includes (OR)
        public bool TakeAmmoForOwnedGuns        = true;
        // Minimum ammo tier for the "Take ammo for guns I own" smart-loot rule.
        // Off = take any tier. Mapped to Item.DisplayQuality (mapping verified in-game).
        public AmmoTier MinAmmoTier             = AmmoTier.Off;
        public bool TakeWishlisted              = true;
        public bool TakeQuestRequired           = true;
        public bool TakeBuildingRequired        = false; // base-crafting demand; off by default to keep corpse-loot tight.
        // Default ON + AND'd (see ValueRulesRequireBoth): out-of-box "take items worth carrying"
        // = sell value ≥ 100 (per-stack) AND ≥ 500/kg. Players retune by feel/progression.
        public bool TakeAboveValue              = true;
        public int  ValueThreshold              = 100;
        public bool TakeAboveValuePerWeight     = true;
        public int  ValuePerWeightThreshold     = 500;  // sell value per kg
        // How the two value rules above combine WITH EACH OTHER (only when both are on):
        // true = AND (must clear flat value AND value/kg, default); false = OR (either qualifies).
        // AND lets the flat per-stack value gate out light-but-cheap items the density rule alone
        // would grab (e.g. a small aspirin stack: 533/kg passes, but 48 sell fails the 100 flat gate).
        // Non-value includes (ammo/wishlist/quest/building/tags) stay independently OR'd regardless.
        public bool ValueRulesRequireBoth       = true;
        public bool TopUpExistingStacks         = true;  // fill partial stacks already carried
        public bool AllowStackOverflowOnTopUp   = false; // when top-up is the only reason, take whole stack anyway

        // Player-extensible tag list. Default empty; populate with strings
        // discovered from in-game items if/when a tag-dumping diagnostic
        // surfaces canonical names like "Heal" / "Money" / "Quest".
        public string[] IncludeTags             = new string[0];

        // Behaviour
        public bool AudioOnSmartTake            = true;
        public bool QuickAccess                 = true;  // Y long-press → settings (not wired yet).
    }

    // Detailed Cost List: replaces the vanilla horizontal icon + ( have / need )
    // counter row (shown everywhere CostDisplay renders item requirements) with a
    // vertical ( has / needs ) [icon] Name list at 1.75x. Applies globally via a
    // postfix on CostDisplay.Setup. Money/currency rows are left untouched.
    public sealed class CostDisplayRules
    {
        public bool Enabled = true;  // master: on by default
    }

    public sealed class HapticsConfig
    {
        public bool  Enabled         { get; set; } = true;
        public float Intensity       { get; set; } = 0.5f;   // 0..1 slider position; effective gain = Intensity × MaxGain (3×)
        public bool  UiEnabled       { get; set; } = true;
        public bool  GameplayEnabled { get; set; } = true;
    }

    public sealed class AimConfig
    {
        // Stick → mouseDelta pipeline.
        public float DeadzoneInner { get; set; } = 0.18f;
        public float DeadzoneOuter { get; set; } = 0.95f;
        public float ResponseExponent { get; set; } = 2.0f;
        public float Sensitivity { get; set; } = 28.0f;        // px per frame at stick mag 1.0
        public float AdsSensitivityMultiplier { get; set; } = 0.55f;

        // Tier 1 — Magnetism (default ON).
        public bool MagnetismEnabled { get; set; } = true;
        public float MagnetismStrength { get; set; } = 0.4f;   // 0..1, additive bias scale
        public float MagnetismLookAheadMeters { get; set; } = 6.0f;
        public float MagnetismMinStickMag { get; set; } = 0.3f;
        public float MagnetismSearchRadiusMeters { get; set; } = 3.5f;
        public float MagnetismRateHz { get; set; } = 8.0f;     // bias velocity scale; higher = snappier

        // Tier 2 — Slowdown (default OFF).
        public bool SlowdownEnabled { get; set; } = false;
        public float SlowdownScreenRadiusPx { get; set; } = 80.0f;
        public float SlowdownFactor { get; set; } = 0.35f;

        // Master toggle for the assist layer (magnetism + ADS lock).
        // Absolute-radial cursor + perception gate are pure correctness and stay on regardless.
        public bool BaselineAssistEnabled { get; set; } = true;
        // Absolute-radial cursor: stick direction = crosshair direction; radius = this * Screen.height.
        // Keeps aim instant (no flight lag) and off screen corners.
        public float CursorCircleRadiusFactor { get; set; } = 0.5f;
    }

    public sealed class BindingsConfig
    {
        // Map of action name → gamepad control path (Unity InputSystem syntax).
        // Strings can be edited by users; falls back to defaults for any unknown action.
        public string MoveAxis { get; set; } = "<Gamepad>/leftStick";
        public string Run { get; set; } = "<Gamepad>/rightShoulder";
        public string ADS { get; set; } = "<Gamepad>/leftTrigger";
        public string Trigger { get; set; } = "<Gamepad>/rightTrigger";
        public string Dash { get; set; } = "<Gamepad>/buttonSouth";
        public string Reload { get; set; } = "<Gamepad>/buttonWest";
        public string Interact { get; set; } = "<Gamepad>/buttonNorth";
        // PutAway sharing B with CancelSkill would cancel skills on holster; left empty by default.
        public string PutAway { get; set; } = "";
        public string CancelSkill { get; set; } = "<Gamepad>/buttonEast";
        public string UI_Inventory { get; set; } = "<Gamepad>/start";
        public string UI_Map { get; set; } = "<Gamepad>/select";
        public string ToggleNightVision { get; set; } = "<Gamepad>/leftStickPress";
        public string ToggleView { get; set; } = "<Gamepad>/rightStickPress";
        public string Quack { get; set; } = "";
        // Shares binding with CancelSkill; both fire on B — game handlers decide per-context which acts.
        public string StopAction { get; set; } = "<Gamepad>/buttonEast";

        public string SwitchWeaponPositive { get; set; } = "<Gamepad>/dpad/up";
        public string SwitchWeaponNegative { get; set; } = "<Gamepad>/dpad/down";
        public string SwitchInteractAndBulletTypePositive { get; set; } = "<Gamepad>/dpad/right";
        public string SwitchInteractAndBulletTypeNegative { get; set; } = "<Gamepad>/dpad/left";

        // UI-mode actions.
        public string UI_Confirm { get; set; } = "<Gamepad>/buttonSouth";
        public string UI_Cancel { get; set; } = "<Gamepad>/buttonEast";
        public string Click { get; set; } = "<Gamepad>/buttonSouth";
        // Router owns Y (ItemOperationMenu) and X (Smart-Take). Drop/Use reachable via menu rows.
        // Restore to "<Gamepad>/..." paths to re-enable vanilla direct-press verbs.
        public string UI_Item_Drop { get; set; } = "";
        public string UI_Item_use { get; set; } = "";

        // UI_Navigate composed from dpad (cardinal stepping).
        public string UI_Navigate_Up { get; set; } = "<Gamepad>/dpad/up";
        public string UI_Navigate_Down { get; set; } = "<Gamepad>/dpad/down";
        public string UI_Navigate_Left { get; set; } = "<Gamepad>/dpad/left";
        public string UI_Navigate_Right { get; set; } = "<Gamepad>/dpad/right";

        // Mini-game buttons are NOT configurable: MiniGameInputGate direct-drives from Gamepad.current
        // (PlayerInput stays KeyAndMouse on the Deck so bindings don't resolve).
    }

    public sealed class UiConfig
    {
        public float NavRepeatDelaySec { get; set; } = 0.35f;
        public float NavRepeatRateSec { get; set; } = 0.08f;
        public float DragHoldThresholdSec { get; set; } = 0.18f;  // legacy, unused
        public bool EnableGridFocus { get; set; } = true;
        // Accessibility: the left stick mirrors the dpad for menu/grid/slider
        // navigation (a thresholded 4-way virtual dpad). On by default; intended
        // to become an in-game accessibility toggle.
        public bool StickAsDpad { get; set; } = true;

        public float CarryHoldThresholdSec { get; set; } = 0.25f;   // A-down to carry-start
        public float CrossPaneDistancePenalty { get; set; } = 0.10f; // FocusGraph neighbor scoring
        public float FadeSettleAlphaThreshold { get; set; } = 0.05f; // FadeGroup considered hidden below this

        // Focus outline overlay.
        public bool FocusOutlineEnabled { get; set; } = true;
        public float FocusOutlineThicknessPx { get; set; } = 4f;
        public string FocusOutlineColorHex { get; set; } = "#FFD700";

        // Select long-press fires vanilla pickAll/storeAll (bypasses Smart-Take) while LootView is open.
        public float SelectLongPressSec { get; set; } = 0.5f;

        // INV-1: long-press Y opens Details panel; tap Y still opens operation menu.
        // Only consulted in views that embed an ItemDetailsDisplay panel.
        public float DetailsHoldThresholdSec { get; set; } = 0.35f;
    }

    public sealed class DiagnosticsConfig
    {
        // Master dev-mode switch; bypasses dumps/verbose logs/dev flags at boot. Off in release.
        public bool DevMode { get; set; } = false;

        // Per-subsystem verbose logging. DevMode implies DebugLog.
        public bool DebugLog { get; set; } = false;

        // UI structure dumper. Hold HotkeyKey for HotkeyHoldSec to fire. Off in release.
        public bool UIDumperEnabled { get; set; } = false;
        public float HotkeyHoldSec { get; set; } = 1.0f;
        // UnityEngine.InputSystem.Key name (e.g. "F8"). Bind Deck R5 → F8 via Steam Input. F12 avoided (Steam screenshot).
        public string HotkeyKey { get; set; } = "F8";
        // Legacy gamepad-chord fields; retained for config backwards-compat, no longer consulted.
        public string HotkeyButtonA { get; set; } = "leftShoulder";
        public string HotkeyButtonB { get; set; } = "rightShoulder";
        public string HotkeyButtonC { get; set; } = "buttonNorth";
        public string OutputSubdir { get; set; } = "ControllerDumps";
        // Soft cap; over-budget files still write but log a warning.
        public int SoftCapBytes { get; set; } = 1_000_000;
    }

    // Maps to Item.DisplayQuality rarity (Trash=White … Mythic=Red); names match game's rarity labels.
    public enum AmmoTier { Off, Trash, Common, Rare, VeryRare, Legendary, Mythic }

    // Preset tier for auto-aim; maps to bundled defaults in AutoAimTiers.Apply.
    // "Custom" = keep individual JSON values (used when the settings UI edits sub-knobs directly).
    public enum AutoAimTier
    {
        Off,
        Light,
        Standard,
        Aggressive,
        Cheat,
        Custom,
    }

    // Auto-aim configuration.
    public sealed class AutoAimConfig
    {
        public AutoAimTier Tier { get; set; } = AutoAimTier.Standard;

        // Master switch. Set by tier preset.
        public bool Enabled { get; set; } = false;

        // Cursor blend strengths (0..1). 1.0 = teleport at 60fps. 0.0 = no pull.
        public float SnapStrength { get; set; } = 0.60f;
        public float ReturnSpeed { get; set; } = 0.40f;

        // Target scoring weights.
        public float WeightScreenDist { get; set; } = 1.0f;
        public float WeightWorldDist { get; set; } = 0.3f;
        public float WeightCenterness { get; set; } = 0.5f;
        public float WeightLowHp { get; set; } = 0.0f;

        // Hysteresis.
        public float SwitchMargin { get; set; } = 0.30f;
        public int MinLockTimeMs { get; set; } = 200;

        // Stick override threshold.
        public float OverrideStickMagnitude { get; set; } = 0.9f;
        public float OverrideAngleDegrees { get; set; } = 90f;

        // LOS knob.
        public bool TargetThroughWalls { get; set; } = false;

        // Honor time-of-day + night vision + flashlight modifiers on the cone.
        public bool RespectNightVisionTimeOfDay { get; set; } = true;

        // Hard cap on target distance regardless of ViewDistance / SenseRange.
        // Prevents auto-aim from acquiring distant decorative entities (e.g.,
        // base test dummies). Effective dist = min(ViewDistance, this);
        // effective sense = min(SenseRange, this).
        public float MaxTargetDistanceMeters { get; set; } = 25f;

        // Decouple the fog-of-war view cone from the auto-aim cursor. When
        // true, FogOfWarPatch overrides the cone rotation to follow the right-
        // stick direction instead of the locked target. Bullets still go to
        // the lock; only the visible sight cone is rerouted.
        public bool DecoupleViewFromAim { get; set; } = true;

        // Minimum post-deadzone stick magnitude required to update the view
        // direction. Below this, the view holds its last direction.
        public float ViewDirectionMinStickMag { get; set; } = 0.15f;

        // Melee on-attack aim-assist. OnAttackPressed snaps facing to the nearest enemy within
        // MeleeAcquireRange and within MeleeMaxTurnDegrees of current facing.
        // 0 = disabled. Set by AutoAimTiers: Off/Light/Standard=0, Aggressive=130, Cheat=180.
        public float MeleeMaxTurnDegrees { get; set; } = 180f;
        // Absolute acquisition radius for melee face-snap, ~5m; independent of the gun MaxTargetDistanceMeters cap.
        public float MeleeAcquireRange   { get; set; } = 5.0f;

        // AIM-6 kill-switch (diagnostic; not surfaced in the in-game settings UI). When true,
        // auto-aim never locks a render-cloaked enemy (e.g. the J-Lab "Test Object") until the
        // player's thermal/night-vision reveals it. Mirrored to CloakGate.Enabled in AutoAimTiers.Apply.
        public bool RespectCloak { get; set; } = true;
    }

    // Throwables (grenades) controller config. Master-gated by Enabled (+ PerfFlags.Throwables).
    public sealed class ThrowConfig
    {
        // Master enable for the throwables feature.
        public bool Enabled { get; set; } = true;

        // LT aim-assist lock while aiming a throw.
        public bool AimAssistEnabled { get; set; } = true;

        // Buffer an RT release that lands before the wind-up completes, then throw when ready.
        // (Base game cancels on early release; we prefer to honor the intent.)
        public bool BufferEarlyRelease { get; set; } = true;

        // Maps stick magnitude [0..1] -> fraction of castRange [0..1]. Exponent 1 = linear;
        // >1 eases in (small pushes stay short/precise). Kept >=1 and clamped.
        public float FreePanCurveExponent { get; set; } = 1.0f;

        // Reticle distance on aim-entry with a centered stick (fraction of castRange).
        public float IdleSeedFactor { get; set; } = 0.6f;

        // Safety margin added to skillReadyTime before we permit a release (avoids the game's
        // internal "released too early -> cancel" by ~1 frame). Seconds.
        public float ReadyEpsilonSeconds { get; set; } = 0.05f;

        // Pure: stick magnitude -> distance fraction. Public + static so it's trivially checkable.
        public float DistanceFraction(float stickMagnitude01)
        {
            float m = stickMagnitude01 < 0f ? 0f : (stickMagnitude01 > 1f ? 1f : stickMagnitude01);
            float e = FreePanCurveExponent < 1f ? 1f : FreePanCurveExponent;
            return e == 1f ? m : (float)System.Math.Pow(m, e);
        }
    }

    // AIM-4 sniper scope. Master-gated by Enabled. Engaged from AimDriverPatch when LT is held
    // on a gun classified "scoped" by ScopeDetector. Free-look + ESCAPABLE soft assist.
    public sealed class ScopeConfig
    {
        // --- Tier-independent (set from JSON; NOT overwritten by AutoAimTiers.Apply) ---
        public bool  Enabled                 { get; set; } = true;
        // gun.ADSAimDistanceFactor >= this  OR  gun.BulletDistance >= BulletDistanceThreshold
        // => scoped. PLACEHOLDERS: calibrate on-device from the [scope] detection log.
        public float AdsFactorThreshold      { get; set; } = 1.5f;
        public float BulletDistanceThreshold { get; set; } = 20f;
        // Free-look velocity gain (screen px/sec at full stick). Comfort/sensitivity, not assist.
        public float FreelookSpeedPxPerSec   { get; set; } = 900f;
        // Candidate range for the soft assist. 0 => use the gun's full BulletDistance
        // (NOT the 25 m hip cap), so distant targets qualify.
        public float MaxTargetDistanceMeters { get; set; } = 0f;
        // Phase-2 escalation (drive GameCamera offset directly). NOT IMPLEMENTED in this plan.
        public bool  DirectCameraPan         { get; set; } = false;

        // --- Tier-managed (defaults mirror the Standard column; AutoAimTiers.Apply overwrites
        //     these unless Tier == Custom) ---
        public bool  SoftAssistEnabled       { get; set; } = true;
        public float SlowdownFactor          { get; set; } = 0.45f; // x velocity near a target; lower = stronger
        public float SlowdownRadiusPx        { get; set; } = 110f;
        public float SettleRadiusPx          { get; set; } = 80f;
        public float SettleTauSeconds        { get; set; } = 0.14f; // SmoothDamp ease; lower = snappier (never a snap)
        public float EscapeStickMag          { get; set; } = 0.55f; // stick mag at/above which slow+settle yield
    }

    // AIM-1 recoil assist + predictive lead. Master-gated by Enabled. Most fields are
    // tier-managed (AutoAimTiers.Apply overwrites them unless Tier == Custom); the caps/
    // master are tier-independent. All distances in screen px unless noted.
    public sealed class RecoilConfig
    {
        // --- Tier-independent (set from JSON; NOT overwritten by AutoAimTiers.Apply) ---
        public bool  Enabled              { get; set; } = true;
        public float MaxOffsetPx          { get; set; } = 260f;  // clamp on accumulated recoil offset
        public float MaxLeadMeters        { get; set; } = 6.0f;  // cap on predictive lead distance
        public float MaxFlightSeconds     { get; set; } = 1.5f;  // cap on flight time used for lead
        // Break-lock thresholds are multiplied by this while firing, so moderate RS counters
        // recoil instead of switching/leaving the target. 1 = no change; >1 = stickier.
        public float FireEscapeMultiplier { get; set; } = 2.0f;

        // --- Tier-managed (defaults mirror the Standard column; AutoAimTiers.Apply overwrites
        //     unless Tier == Custom) ---
        public float KickScale         { get; set; } = 0.60f;  // per-shot recoil impulse multiplier
        public float RecoverRate       { get; set; } = 420f;   // px/sec auto-recovery
        public float RecoverHoldSec    { get; set; } = 0.10f;  // delay after last shot before recovery
        public float CounterGainToward { get; set; } = 1.6f;   // recover bleed per px of correct-dir motion
        public float CounterGainAway   { get; set; } = 0.0f;   // recover bleed per px of wrong-dir motion
        public float LeadFraction      { get; set; } = 0.70f;  // 0..1 fraction of true intercept
    }

    // AIM-1 v2 bias-ring lock. Master-gated by Enabled. Tier-managed fields are overwritten by
    // AutoAimTiers.Apply unless Tier == Custom; the caps/master/shared knobs are tier-independent.
    public sealed class BiasRingConfig
    {
        // --- Tier-independent (JSON; NOT overwritten by AutoAimTiers.Apply) ---
        public bool  Enabled              { get; set; } = true;
        public float MaxRingRadiusPx      { get; set; } = 300f;  // cap on the ring radius
        public float CounterSuppressDot   { get; set; } = -0.71f; // suppress escape within ~135° of the counter-recoil side (cos135°≈-0.71); code-authoritative via AutoAimTiers.Apply
        public float EscapeBandFrac       { get; set; } = 0.08f; // outer 8% of full stick deflection = the escape band
        public float ReacquireSuppressSec { get; set; } = 0.30f; // post-escape no-lock window
        public float KickDirHoldSec       { get; set; } = 0.60f; // latch the recoil dir this long after it fades, so a counter-flick can't escape mid-correction

        // --- Tier-managed (overwritten by AutoAimTiers.Apply unless Tier == Custom) ---
        public float RingRadiusPx   { get; set; } = 130f; // 0 => ring inactive this tier (CursorBlend fallback)
        public float BiasStrength   { get; set; } = 0.70f; // 0..1; 1 = glued to ideal, 0 = full roam to the ring edge
        public float EscapeDwellSec { get; set; } = 0.28f; // hold the stick in-band this long to escape
    }
}
