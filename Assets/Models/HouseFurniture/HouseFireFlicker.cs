// Flickering fire for the House Furniture pack.
// - Wobbles a Light's intensity (and nudges it around a little so shadows dance).
// - Plays the 4-frame flame flipbook on the "fire" cards by stepping the texture offset.
using UnityEngine;

public class HouseFireFlicker : MonoBehaviour
{
    [Tooltip("Light that flickers. Leave empty for flipbook only.")]
    public Light fireLight;
    [Tooltip("Renderer of the flame cards (child called 'fire'). Leave empty for light only.")]
    public Renderer fireRenderer;

    [Header("Light")]
    public float baseIntensity = 2f;
    [Range(0f, 1f)] public float flicker = 0.35f;
    public float speed = 8f;
    public bool jitterPosition = true;

    [Header("Flipbook")]
    public int frames = 4;
    public float fps = 9f;

    Material mat;
    Vector3 lightHome;
    float seed;

    void Start()
    {
        seed = Random.value * 100f;
        if (fireLight) lightHome = fireLight.transform.localPosition;
        if (fireRenderer) mat = fireRenderer.material;      // own instance, so each fire animates on its own
    }

    void Update()
    {
        float t = Time.time * speed + seed;
        float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.3f, seed + 5f) * 0.3f;

        if (fireLight)
        {
            fireLight.intensity = baseIntensity * (1f - flicker + flicker * 2f * n);
            if (jitterPosition)
                fireLight.transform.localPosition = lightHome + new Vector3(n - 0.5f, 0f, Mathf.PerlinNoise(seed, t) - 0.5f) * 0.06f;
        }

        if (mat && frames > 1)
        {
            int f = Mathf.FloorToInt((Time.time + seed) * fps) % frames;
            var off = new Vector2((float)f / frames, 0f);
            if (mat.HasProperty("_BaseMap")) mat.SetTextureOffset("_BaseMap", off);
            if (mat.HasProperty("_MainTex")) mat.SetTextureOffset("_MainTex", off);
        }
    }
}
