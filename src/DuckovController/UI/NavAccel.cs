namespace DuckovController.UI
{
    // Hold-repeat acceleration: base for steps 0-7, 60% for 8-23, 30% (min 20ms) beyond.
    // Shared curve; each controller owns its own step counter + timing state.
    internal static class NavAccel
    {
        internal static float EffectiveRate(float baseRate, int stepCount)
            => stepCount < 8  ? baseRate
             : stepCount < 24 ? baseRate * 0.6f
             : UnityEngine.Mathf.Max(0.020f, baseRate * 0.3f);
    }
}
