// The power goes out: `delay` seconds after the player spawns every light flickers and dies, and from then on only the
// flashlight (FLASH) lights anything. PS1 materials go dark through the _PS1_Blackout shader global (it also kills the
// lighting baked into vertex colours and the glowing lamps); normal Unity lights and the ambient light are switched off too.
// While it's dark a mission (top left) sends you to the lever on the teacher's desk in Alfa (FeriPowerLever), which
// brings the lights back for `fixedDuration` seconds, then they die again.
// FeriAbilities adds this to the player.
using System.Collections.Generic;
using UnityEngine;

namespace FeriMap
{
    public class FeriBlackout : MonoBehaviour
    {
        [Tooltip("Seconds after spawning before the lights go out.")]
        public float delay = 10f;   // 10 s for testing (was 30)
        [Tooltip("How long the lights stay on after the lever in Alfa is pulled.")]
        public float fixedDuration = 120f;
        [Tooltip("How long the lights flicker before they die.")]
        public float flickerTime = 1.6f;
        [Tooltip("Where the lever goes: on the teacher's desk in Alfa (world space, on the desk top).")]
        public Vector3 leverPosition = new Vector3(9.65f, 0.58f, 35.8f);

        static readonly int IdBlackout = Shader.PropertyToID("_PS1_Blackout");

        float outAt;          // when the lights (next) go out
        float fixedAt = -99f; // when the lever was last pulled
        bool dead, captured;
        readonly List<Light> lights = new List<Light>();
        readonly List<float> lightIntensity = new List<float>();
        Color ambient;
        float ambientIntensity;
        FeriPowerLever lever;
        GUIStyle titleStyle, textStyle;

        public bool IsOut => dead;

        void OnEnable()
        {
            outAt = Time.time + delay;
            dead = captured = false;
            Shader.SetGlobalFloat(IdBlackout, 0f);
        }

        void Start()
        {
            lever = FeriPowerLever.Build(leverPosition, this);
            FeriAlfaWhiteboard.Setup();
        }

        void OnDisable() { RestoreAll(); }
        void OnApplicationQuit() { RestoreAll(); }

        /// <summary>The lever in Alfa: lights back on (with a stutter) for fixedDuration seconds.</summary>
        public void FixLights()
        {
            if (!dead) return;
            dead = false;
            fixedAt = Time.time;
            outAt = Time.time + fixedDuration;
        }

        void Update()
        {
            float sinceFix = Time.time - fixedAt;
            if (!dead && sinceFix < 0.7f)
            {
                // stutters back on
                Apply(Mathf.PerlinNoise(Time.time * 20f, 1.3f) < Mathf.Lerp(0.2f, 1f, sinceFix / 0.7f) ? 0f : 1f);
                return;
            }
            if (dead) return;
            float t = Time.time - outAt;
            if (t < 0f) { if (sinceFix < 1f) Apply(0f); return; }
            if (!captured) Capture();

            float k;
            if (t >= flickerTime)
            {
                k = 1f; dead = true;
                if (lever != null) lever.ResetLever();
                Debug.Log("Blackout: the lights are out, mission shown top left", this);
            }
            else
            {
                // stutters on and off, getting darker
                float n = Mathf.PerlinNoise(Time.time * 18f, 3.7f);
                k = n < Mathf.Lerp(0.25f, 0.85f, t / flickerTime) ? 1f : t / flickerTime * 0.5f;
            }
            Apply(k);
        }

        void Capture()
        {
            captured = true;
            lights.Clear(); lightIntensity.Clear();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                lights.Add(l);
                lightIntensity.Add(l.intensity);
            }
            ambient = RenderSettings.ambientLight;
            ambientIntensity = RenderSettings.ambientIntensity;
        }

        void Apply(float k)
        {
            Shader.SetGlobalFloat(IdBlackout, k);
            if (!captured) return;
            for (int i = 0; i < lights.Count; i++)
                if (lights[i] != null) lights[i].intensity = lightIntensity[i] * (1f - k);
            RenderSettings.ambientLight = Color.Lerp(ambient, Color.black, k);
            RenderSettings.ambientIntensity = Mathf.Lerp(ambientIntensity, 0f, k);
        }

        void RestoreAll()
        {
            Shader.SetGlobalFloat(IdBlackout, 0f);
            if (captured)
            {
                for (int i = 0; i < lights.Count; i++)
                    if (lights[i] != null) lights[i].intensity = lightIntensity[i];
                RenderSettings.ambientLight = ambient;
                RenderSettings.ambientIntensity = ambientIntensity;
                lights.Clear(); lightIntensity.Clear();
                captured = false;
            }
        }

        // ------------------------------------------------------------------ mission, top left
        void OnGUI()
        {
            // first thing, in every event: PS1Camera draws the whole game picture through IMGUI at depth 100, and a
            // script that never sets its depth can end up underneath it
            GUI.depth = -10;
            if (Event.current.type != EventType.Repaint) return;
            float sinceFix = Time.time - fixedAt;
            float fade = dead ? 1f : Mathf.Clamp01((4f - sinceFix) / 1f);   // "lights back on" stays up a few seconds
            if (fade <= 0f) return;

            float u = Mathf.Max(2f, Screen.height / 360f);   // one PS1 pixel
            float m = u * 10f;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
                textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false };
            }
            titleStyle.fontSize = Mathf.RoundToInt(u * 6f);
            textStyle.fontSize = Mathf.RoundToInt(u * 8f);

            string line = dead ? "Fix the lights in Alfa" : "The lights are back on";
            string sub = dead ? "Pull the lever on the desk" : null;
            Color accent = dead ? new Color(1f, 0.8f, 0.3f) : new Color(0.45f, 1f, 0.5f);
            if (dead && Time.time - outAt - flickerTime < 2f && Mathf.Repeat(Time.time * 4f, 1f) < 0.5f) accent.a = 0.3f;   // blinks as it appears

            float w = u * 130f, h = sub != null ? u * 26f : u * 15f;
            var box = new Rect(m, m, w, h);
            Box(new Rect(box.x, box.y, u * 2f, box.height), new Color(accent.r, accent.g, accent.b, 0.9f * fade * accent.a));   // accent bar
            Box(new Rect(box.x + u * 2f, box.y, box.width, box.height), new Color(0f, 0f, 0f, 0.55f * fade));
            Label(new Rect(box.x + u * 6f, box.y + u * 2f, w, u * 11f), line, textStyle, new Color(1f, 1f, 1f, fade), u);
            if (sub != null)
                Label(new Rect(box.x + u * 6f, box.y + u * 13f, w, u * 11f), sub, titleStyle, new Color(0.8f, 0.8f, 0.8f, fade), u);
            GUI.color = Color.white;
        }

        static void Label(Rect r, string s, GUIStyle st, Color c, float u)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.9f * c.a);
            GUI.Label(new Rect(r.x + u * 0.7f, r.y + u * 0.7f, r.width, r.height), s, st);
            GUI.color = c;
            GUI.Label(r, s, st);
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGlobal() { Shader.SetGlobalFloat(IdBlackout, 0f); }
    }
}
