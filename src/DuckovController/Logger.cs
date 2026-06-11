using UnityEngine;

namespace DuckovController
{
    internal static class Log
    {
        private const string Prefix = "[GoldenController] ";

        internal static volatile bool Verbose;

        internal static void Info(string msg) => Debug.Log(Prefix + msg);

        internal static void Warn(string msg) => Debug.LogWarning(Prefix + msg);

        internal static void Error(string msg) => Debug.LogError(Prefix + msg);

        internal static void Debug_(string msg)
        {
            if (Verbose) Debug.Log(Prefix + msg);
        }
    }
}
