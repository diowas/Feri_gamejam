// The power goes out: `delay` seconds after the player spawns every light flickers and dies, and from then on only the
// flashlight (FLASH) lights anything. PS1 materials go dark through the _PS1_Blackout shader global (it also kills the
// lighting baked into vertex colours and the glowing lamps); normal Unity lights and the ambient light are switched off too.
// While it's dark a mission (top left) sends you to the lever on the teacher's desk in Alfa (FeriPowerLever), which
// brings the lights back for good (even after dying). When they die the player thinks out loud at
// the bottom of the screen (FeriThought). Dying in the dark (scene reload) keeps the lights off.
// The mission box (top left) also shows the escape's objective (FeriEscape) underneath.
// FeriAbilities adds this to the player.
using System.Collections.Generic;
using UnityEngine;

namespace FeriMap
{
    public class FeriBlackout : MonoBehaviour
    {
        [Tooltip("Seconds after spawning before the lights go out.")]
        public float delay = 10f;   // 10 s for testing (was 30)
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

        // survives the scene reload when you die, so the power stays out
        static bool powerOut, fixedForGood;
        float thoughtAt = -99f;   // when "I should go fix the lights" started typing
        const string Thought = "The lights... I should go fix them.";
        static Font font;

        /// <summary>Back to the start: lights on (the escape ran out of time).</summary>
        public static void ResetState() { powerOut = fixedForGood = false; }

        void OnEnable()
        {
            outAt = fixedForGood ? float.MaxValue : Time.time + delay;
            dead = captured = false;
            Shader.SetGlobalFloat(IdBlackout, 0f);
        }

        void Start()
        {
            lever = FeriPowerLever.Build(leverPosition, this);
            FeriAlfaWhiteboard.Setup();
            if (powerOut)
            {
                // died in the dark: still dark
                Capture();
                dead = true;
                Apply(1f);
                if (lever != null) lever.ResetLever();
            }
        }

        void OnDisable() { RestoreAll(); }
        void OnApplicationQuit() { RestoreAll(); }

        /// <summary>The lever in Alfa: lights back on (with a stutter), and they stay on.</summary>
        public void FixLights()
        {
            if (!dead) return;
            dead = powerOut = false;
            fixedForGood = true;
            fixedAt = Time.time;
            outAt = float.MaxValue;
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
            if (dead) { DeadFlicker(); return; }
            float t = Time.time - outAt;
            if (t < 0f) { if (sinceFix < 1f) Apply(0f); return; }
            if (!captured) Capture();

            float k;
            if (t >= flickerTime)
            {
                k = 1f; dead = powerOut = true;
                thoughtAt = Time.time;
                FeriThought.Say(Thought);
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

        // while the power is dead the lights now and then (every 4-7 s) stutter dimly, for half a second or so
        float nextFlick = -1f, flickAt = -1f, flickLength;
        void DeadFlicker()
        {
            if (nextFlick < 0f) nextFlick = Time.time + Random.Range(4f, 7f);
            if (Time.time >= nextFlick)
            {
                flickAt = Time.time;
                flickLength = Random.Range(0.3f, 0.7f);
                nextFlick = Time.time + Random.Range(4f, 7f);
            }
            float ft = Time.time - flickAt;
            if (flickAt >= 0f && ft < flickLength)
            {
                if (!captured) Capture();
                float n = Mathf.PerlinNoise(Time.time * 25f, 7.1f);
                Apply(n > 0.45f ? Mathf.Lerp(0.93f, 0.8f, (n - 0.45f) / 0.55f) : 1f);   // 1 = black, 0.8 = a dim glow
            }
            else if (flickAt >= 0f) { flickAt = -1f; Apply(1f); }
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
            if (font == null) font = Resources.Load<Font>("FeriFont");   // LiberationSans, the Prologue's font
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false };
                textStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, wordWrap = false };
                if (font != null) { titleStyle.font = font; textStyle.font = font; }
            }
            DrawThought();
            DrawMission();
            GUI.color = Color.white;
        }

        void DrawThought() { FeriThought.Draw(textStyle); }

        // top left: the lights (once the thought has been typed out), then the escape under it
        void DrawMission()
        {
            float u = Mathf.Max(2f, Screen.height / 360f);   // one PS1 pixel
            float y = u * 10f;
            float sinceFix = Time.time - fixedAt;
            bool show = dead && Time.time - thoughtAt > Thought.Length * 0.1f;
            float fade = show ? 1f : dead ? 0f : Mathf.Clamp01((4f - sinceFix) / 1f);   // "lights back on" stays up a few seconds
            if (fade > 0f)
                y += MissionBox(y, u, dead ? "Fix the lights in Alfa" : "The lights are back on", dead ? "Pull the lever on the desk" : null,
                    dead ? new Color(0.62f, 0.58f, 0.47f) : new Color(0.45f, 0.8f, 0.5f), fade) + u * 4f;   // the Prologue's warm grey
            if (FeriEscape.Objective(out string line, out string sub))
                y += MissionBox(y, u, line, sub, new Color(0.75f, 0.35f, 0.3f), 1f) + u * 4f;
            if (FeriEscape.PowerObjective(out line, out sub))
                MissionBox(y, u, line, sub, new Color(0.85f, 0.6f, 0.2f), 1f);
        }

        float MissionBox(float y, float u, string line, string sub, Color accent, float fade)
        {
            float m = u * 10f;
            titleStyle.fontSize = Mathf.RoundToInt(u * 8f);
            float w = u * 150f, h = sub != null ? u * 26f : u * 15f;
            var box = new Rect(m, y, w, h);
            Box(new Rect(box.x, box.y, u * 2f, box.height), new Color(accent.r, accent.g, accent.b, 0.9f * fade));   // accent bar
            Box(new Rect(box.x + u * 2f, box.y, box.width, box.height), new Color(0f, 0f, 0f, 0.55f * fade));
            Label(new Rect(box.x + u * 6f, box.y + u * 2f, w, u * 11f), line, titleStyle, new Color(accent.r, accent.g, accent.b, fade), u);
            if (sub != null)
            {
                int big = titleStyle.fontSize;
                titleStyle.fontSize = Mathf.RoundToInt(u * 6.5f);
                Label(new Rect(box.x + u * 6f, box.y + u * 13f, w, u * 11f), sub, titleStyle, new Color(0.65f, 0.65f, 0.65f, fade), u);
                titleStyle.fontSize = big;
            }
            return h;
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
        static void ResetGlobal() { Shader.SetGlobalFloat(IdBlackout, 0f); powerOut = fixedForGood = false; }
    }
}
