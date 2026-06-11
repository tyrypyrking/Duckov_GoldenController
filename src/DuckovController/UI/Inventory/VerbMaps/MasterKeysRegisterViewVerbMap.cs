using DuckovController.UI.Common;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // MasterKeysRegisterView (Duckov.MasterKeys.UI) — the key-registration
    // terminal. Behaviour is pure DefaultViewVerbMap (A fast-picks the key into
    // the register slot / fires the focused submit button, Y opens the item
    // menu, B exits); the only override is the horizontal prompt strip so it
    // matches the trader/craft views.
    internal sealed class MasterKeysRegisterViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "MasterKeysRegisterView";
        public override bool HorizontalPrompts => true;
    }
}
