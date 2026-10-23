using System.Collections.Generic;
using UnityEngine;

namespace FeriMap
{

    [ExecuteAlways]
    public class PS1Light : MonoBehaviour
    {
        public Color color = Color.white;
        public float intensity = 1f;
        public float range = 7f;
        [Tooltip("Zapečena v barve vozlišč (statična). Za utripajoče luči izklopi.")]
        public bool baked = true;
        [Range(0f, 1f)] public float flicker = 0f;
        [Tooltip("Opcijsko: svetilka (mesh), ki utripa skupaj z lučjo.")]
        public Renderer lampRenderer;

        public static readonly List<PS1Light> All = new List<PS1Light>();
        public float CurrentIntensity { get; private set; }

        float seed;
        float dropTimer;
        bool dropped;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            seed = Random.value * 100f;
            CurrentIntensity = intensity;
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        void Update()
        {
            if (flicker <= 0f || !Application.isPlaying)
            {
                CurrentIntensity = intensity;
                return;
            }

            dropTimer -= Time.deltaTime;
            if (dropTimer <= 0f)
            {
                dropped = Random.value < flicker * 0.6f;
                dropTimer = dropped ? Random.Range(0.03f, 0.25f) : Random.Range(0.05f, 2.5f * (1.1f - flicker));
            }
            float buzz = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 9f, seed);
            float k = dropped ? Random.Range(0f, 0.12f) : buzz;
            CurrentIntensity = intensity * k;
            if (lampRenderer != null)
            {
                if (mpb == null) mpb = new MaterialPropertyBlock();
                lampRenderer.GetPropertyBlock(mpb);
                mpb.SetColor(ColorId, Color.white * Mathf.Lerp(0.12f, 1f, k));
                lampRenderer.SetPropertyBlock(mpb);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = color;
            Gizmos.DrawWireSphere(transform.position, range);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = baked ? new Color(1f, 0.9f, 0.2f, 0.8f) : new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.DrawSphere(transform.position, 0.08f);
        }
    }
}
