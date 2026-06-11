using System;
using System.Reflection;

namespace DuckovController
{
    // Single-source accessors for game members the mod reaches via reflection.
    // Each member is resolved once and cached, so a game-side rename is a
    // one-line fix here instead of a scavenger hunt across subsystems.
    internal static class GameRef
    {
        // Duckov.AudioManager.Post(string): reflected to avoid FMODUnity build dep; no-ops silently.
        private static MethodInfo? _audioPost;
        private static bool _audioPostResolved;

        internal static void PostAudio(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            try
            {
                if (!_audioPostResolved)
                {
                    _audioPostResolved = true;
                    var t = Type.GetType("Duckov.AudioManager, TeamSoda.Duckov.Core");
                    _audioPost = t?.GetMethod("Post", BindingFlags.Public | BindingFlags.Static,
                        binder: null, types: new[] { typeof(string) }, modifiers: null);
                }
                _audioPost?.Invoke(null, new object[] { eventName });
            }
            catch { /* audio is non-essential */ }
        }
    }
}
