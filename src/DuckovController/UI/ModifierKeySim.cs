using System;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DuckovController.UI
{
    // Simulates Ctrl/Alt/Shift for synthesized clicks (inventory split=Ctrl, send-to-character=Alt).
    // Uses InputState.Change (not QueueStateEvent+InputSystem.Update — calling Update from MonoBehaviour Update corrupts the pipeline).
    internal static class ModifierKeySim
    {
        internal enum Modifier { None, Ctrl, Alt, Shift }

        internal static void WithModifier(Modifier mod, Action action)
        {
            if (mod == Modifier.None) { action(); return; }
            var keyboard = Keyboard.current;
            if (keyboard == null) { action(); return; }

            Key key = mod switch
            {
                Modifier.Ctrl => Key.LeftCtrl,
                Modifier.Alt => Key.LeftAlt,
                Modifier.Shift => Key.LeftShift,
                _ => Key.None,
            };
            if (key == Key.None) { action(); return; }

            try
            {
                InputState.Change(keyboard, new KeyboardState(key));
                action();
            }
            finally
            {
                // Empty KeyboardState also releases any real keys held this frame (one-frame hiccup on hybrid setups).
                InputState.Change(keyboard, new KeyboardState());
            }
        }
    }
}
