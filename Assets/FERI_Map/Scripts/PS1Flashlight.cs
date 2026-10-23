using UnityEngine;

namespace FeriMap
{

    public class PS1Flashlight : MonoBehaviour
    {
        public bool on = true;
        public Color color = new Color(1f, 0.95f, 0.82f);
        public float intensity = 1.5f;
        public float range = 16f;
        [Range(1f, 80f)] public float outerAngle = 26f;
        [Range(1f, 80f)] public float innerAngle = 16f;
        [Range(0f, 1f)] public float flicker = 0f;

        public static PS1Flashlight Active { get; private set; }
        public float CurrentIntensity { get; private set; }

        float seed;

        void OnEnable() { Active = this; seed = Random.value * 50f; }
        void OnDisable() { if (Active == this) Active = null; }

        public void Toggle() { on = !on; }

        void Update()
        {
            float k = 1f;
            if (flicker > 0f && Application.isPlaying)
            {
                float n = Mathf.PerlinNoise(Time.time * 6f, seed);
                k = n < flicker * 0.5f ? 0.15f : 1f;
            }
            CurrentIntensity = on ? intensity * k : 0f;
        }
    }
}
