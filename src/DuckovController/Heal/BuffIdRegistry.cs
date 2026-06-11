using System.Collections.Generic;
using Duckov.Buffs;
using Duckov.Utilities;

namespace DuckovController.Heal
{
    // Lazily resolves buff IDs from GameplayDataSettings.Buffs. IDs shift between game patches if the asset
    // is re-saved — never hardcode; always read from live BuffsData.
    internal static class BuffIdRegistry
    {
        private static bool _resolved;
        private static HashSet<int> _bleedIDs = new();
        private static int _painID = -1;
        private static int _burnID = -1;
        private static int _electricID = -1;
        private static int _spaceID = -1;
        private static int _poisonID = -1;
        private static int _coldID = -1;

        internal static HashSet<int> BleedIDs    { get { Ensure(); return _bleedIDs; } }
        internal static int PainID               { get { Ensure(); return _painID; } }
        internal static int BurnID               { get { Ensure(); return _burnID; } }
        internal static int ElectricID           { get { Ensure(); return _electricID; } }
        internal static int SpaceID              { get { Ensure(); return _spaceID; } }
        internal static int PoisonID             { get { Ensure(); return _poisonID; } }
        internal static int ColdID               { get { Ensure(); return _coldID; } }

        // Force re-resolution if GameplayDataSettings asset changes (e.g. new save).
        internal static void Invalidate() { _resolved = false; }

        private static void Ensure()
        {
            if (_resolved) return;
            try
            {
                var b = GameplayDataSettings.Buffs;
                if (b == null) { _resolved = true; return; }

                _bleedIDs.Clear();
                AddIf(_bleedIDs, b.BleedSBuff);
                AddIf(_bleedIDs, b.UnlimitBleedBuff);
                AddIf(_bleedIDs, b.BoneCrackBuff);
                AddIf(_bleedIDs, b.WoundBuff);

                _painID     = SafeID(b.Pain);
                _burnID     = SafeID(b.Burn);
                _electricID = SafeID(b.Electric);
                _spaceID    = SafeID(b.Space);
                _poisonID   = SafeID(b.Poison);
                _coldID     = SafeID(TryGetBuffByName(b, "Cold"));

                _resolved = true;
                Log.Debug_($"BuffIdRegistry: bleed=[{string.Join(",", _bleedIDs)}] " +
                           $"pain={_painID} burn={_burnID} elec={_electricID} " +
                           $"space={_spaceID} poison={_poisonID} cold={_coldID}");
            }
            catch (System.Exception e)
            {
                Log.Error($"BuffIdRegistry.Ensure failed: {e.Message}");
                _resolved = true; // don't retry every frame after a failure
            }
        }

        private static int SafeID(Buff? b) => b != null ? b.ID : -1;

        private static void AddIf(HashSet<int> set, Buff? b)
        {
            if (b != null) set.Add(b.ID);
        }

        // Reflect a Buff by name; used for buffs absent on older builds (e.g. Cold added in a later patch).
        private static Buff? TryGetBuffByName(object container, string memberName)
        {
            try
            {
                var t = container.GetType();
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic;
                var prop = t.GetProperty(memberName, flags);
                if (prop != null && typeof(Buff).IsAssignableFrom(prop.PropertyType))
                    return prop.GetValue(container) as Buff;
                var fld = t.GetField(memberName, flags);
                if (fld != null && typeof(Buff).IsAssignableFrom(fld.FieldType))
                    return fld.GetValue(container) as Buff;
            }
            catch { /* tolerated */ }
            return null;
        }
    }
}
