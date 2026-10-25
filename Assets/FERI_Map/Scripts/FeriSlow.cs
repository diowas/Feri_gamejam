using UnityEngine;

namespace FeriMap
{
    // The monsters' clock. Monster scripts use FeriSlow.DeltaTime / FeriSlow.Time instead of Time.deltaTime / Time.time,
    // so the SLOW ability (FeriAbilities) can slow every one of them down at once. Scale 1 = normal speed.
    public static class FeriSlow
    {
        public static float Scale = 1f;

        static int frame = -1;
        static float clock = -1f;

        public static float DeltaTime => UnityEngine.Time.deltaTime * Scale;

        public static float Time
        {
            get
            {
                if (clock < 0f) { clock = UnityEngine.Time.time; frame = UnityEngine.Time.frameCount; }
                else if (frame != UnityEngine.Time.frameCount)
                {
                    clock += UnityEngine.Time.deltaTime * Scale * (UnityEngine.Time.frameCount - frame);
                    frame = UnityEngine.Time.frameCount;
                }
                return clock;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Scale = 1f; frame = -1; clock = -1f; }
    }
}
