using UnityEngine;

namespace DuckovController.UI
{
    // Reusable "virtual dpad" driven by an analog stick: discretizes the stick
    // to a single 4-way direction (dominant axis, with magnitude hysteresis so
    // it doesn't chatter at the threshold) and synthesizes per-direction press
    // edges across frames. Lets the left stick mirror the physical dpad for
    // menu / grid / slider navigation (accessibility).
    //
    // Usage: Sample(stickValue, enabled) exactly once per frame, then query
    // Edge()/Held() — OR them with the physical dpad at the call site. Holds its
    // own cross-frame state, so one instance per input owner.
    internal sealed class StickDpad
    {
        private const float Enter = 0.5f; // deflection magnitude to engage a direction
        private const float Exit  = 0.4f; // lower release threshold (hysteresis)

        private NavDir? _cur;
        private NavDir? _prev;

        public void Sample(Vector2 stick, bool enabled)
        {
            _prev = _cur;
            if (!enabled) { _cur = null; return; }
            float threshold = _cur.HasValue ? Exit : Enter;
            if (stick.magnitude < threshold) { _cur = null; return; }
            // Dominant axis → single 4-way direction (matches dpad semantics).
            _cur = Mathf.Abs(stick.x) >= Mathf.Abs(stick.y)
                ? (stick.x > 0f ? NavDir.Right : NavDir.Left)
                : (stick.y > 0f ? NavDir.Up : NavDir.Down);
        }

        // True while the stick is deflected toward `d`.
        public bool Held(NavDir d) => _cur == d;

        // True only on the frame the stick first enters direction `d`.
        public bool Edge(NavDir d) => _cur == d && _prev != d;
    }
}
