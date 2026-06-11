using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuckovController.UI
{
    internal sealed partial class MenuFocusController
    {
        // Title.ContinueTask() reflected handle; pointer-click also works — both kept for defense in depth.
        private static MethodInfo? _titleContinueTaskMethod;
        private static bool _reflectionResolved;

        private static FieldInfo? _sceneLoaderReceiverField;
        private static FieldInfo? _sceneLoaderClickedField;
        private static bool _sceneLoaderReflectionResolved;

        private void TryAdvanceTitleSplash()
        {
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad == null) return;

            // Title has no Selectable — submitHandler never reaches it; must drive Title directly.
            bool anyButtonPressed =
                pad.buttonSouth.wasPressedThisFrame ||
                pad.buttonEast.wasPressedThisFrame ||
                pad.buttonWest.wasPressedThisFrame ||
                pad.buttonNorth.wasPressedThisFrame ||
                pad.startButton.wasPressedThisFrame;
            if (!anyButtonPressed) return;

            // Find any active Title behaviour in the loaded scenes.
            var title = FindActiveTitle();
            if (title == null) return;

            TryFireTitleContinue(title);
        }

        private static Title? FindActiveTitle()
        {
            // Title is rare; search at most once per frame and only via
            // FindObjectsOfType which is fast for top-level UI Canvas children.
            var titles = Object.FindObjectsOfType<Title>(includeInactive: false);
            for (int i = 0; i < titles.Length; i++)
            {
                if (titles[i] != null && titles[i].isActiveAndEnabled)
                    return titles[i];
            }
            return null;
        }

        private static void TryFireTitleContinue(Title title)
        {
            // Primary: synthesize OnPointerClick (Title is explicit IPointerClickHandler).
            var ped = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime,
            };
            bool handled = ExecuteEvents.Execute(title.gameObject, ped, ExecuteEvents.pointerClickHandler);
            if (handled) return;

            // Fallback: ContinueTask directly (raycaster may be disabled during timelineToTitle phase).
            ResolveReflection();
            if (_titleContinueTaskMethod != null)
            {
                try { _titleContinueTaskMethod.Invoke(title, null); }
                catch (System.Exception e) { Log.Debug_("Title.ContinueTask invoke threw: " + e.Message); }
            }
        }

        private static void ResolveReflection()
        {
            if (_reflectionResolved) return;
            _reflectionResolved = true;
            _titleContinueTaskMethod = typeof(Title).GetMethod(
                "ContinueTask", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_titleContinueTaskMethod == null)
                Log.Debug_("MenuFocusController: Title.ContinueTask not found");
        }

        // Post-load "press any key" overlay: synthesize pointerClick on pointerClickEventRecevier
        // (IPointerClickHandler whose listener flips SceneLoader.clicked=true).
        private void TryAdvanceSceneLoader()
        {
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad == null) return;
            if (!SceneLoader.IsSceneLoading) return;
            var sl = SceneLoader.Instance;
            if (sl == null) return;

            bool anyButton =
                pad.buttonSouth.wasPressedThisFrame ||
                pad.buttonEast.wasPressedThisFrame ||
                pad.buttonNorth.wasPressedThisFrame ||
                pad.buttonWest.wasPressedThisFrame ||
                pad.startButton.wasPressedThisFrame;
            if (!anyButton) return;

            if (!_sceneLoaderReflectionResolved)
            {
                _sceneLoaderReflectionResolved = true;
                var t = typeof(SceneLoader);
                _sceneLoaderReceiverField = t.GetField("pointerClickEventRecevier",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _sceneLoaderClickedField = t.GetField("clicked",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            }
            if (_sceneLoaderReceiverField == null) return;
            var receiver = _sceneLoaderReceiverField.GetValue(sl) as MonoBehaviour;
            if (receiver == null) return;
            var receiverGo = receiver.gameObject;
            if (receiverGo == null || !receiverGo.activeInHierarchy) return;

            // ExecuteEvents fires OnPointerClick → SceneLoader.NotifyPointerClick → clicked=true.
            var ped = new PointerEventData(EventSystem.current)
            {
                position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f),
                button = PointerEventData.InputButton.Left,
                clickCount = 1,
                clickTime = Time.unscaledTime,
            };
            ExecuteEvents.Execute(receiverGo, ped, ExecuteEvents.pointerClickHandler);
            // Belt-and-braces: also flip the field directly so the splash
            // advances even if the OnPointerClick listener wasn't wired.
            try { _sceneLoaderClickedField?.SetValue(sl, true); } catch { /* ignore */ }
        }
    }
}
