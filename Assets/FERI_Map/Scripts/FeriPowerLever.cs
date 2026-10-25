// The power lever on the teacher's desk in Alfa. FeriBlackout builds it at runtime; while the lights are out its lamp
// glows red (so you can find it in the dark) and [E] throws it, which brings the lights back (FeriBlackout.FixLights).
// When the power dies again it flips back up.
using System.Collections;
using UnityEngine;

namespace FeriMap
{
    public class FeriPowerLever : FeriInteractable
    {
        const float UpAngle = -35f, DownAngle = 50f;

        FeriBlackout blackout;
        Transform handle;
        Material lamp;
        bool pulling;

        public override string Prompt => blackout != null && blackout.IsOut && !pulling ? "[E] Pull the lever" : "";

        public override void Interact(FeriPlayer player)
        {
            if (blackout == null || !blackout.IsOut || pulling) return;
            StartCoroutine(Pull());
        }

        IEnumerator Pull()
        {
            pulling = true;
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                float k = t / 0.35f;
                handle.localRotation = Quaternion.Euler(Mathf.Lerp(UpAngle, DownAngle, k * k), 0f, 0f);
                yield return null;
            }
            handle.localRotation = Quaternion.Euler(DownAngle, 0f, 0f);
            SetLamp(false);
            blackout.FixLights();
            pulling = false;
        }

        /// <summary>The power died again: lever back up, lamp red.</summary>
        public void ResetLever()
        {
            StopAllCoroutines();
            pulling = false;
            if (handle != null) handle.localRotation = Quaternion.Euler(UpAngle, 0f, 0f);
            SetLamp(true);
        }

        void SetLamp(bool off)
        {
            if (lamp == null) return;
            Color c = off ? new Color(1f, 0.1f, 0.05f) : new Color(0.2f, 1f, 0.3f);
            if (lamp.HasProperty("_Color")) lamp.SetColor("_Color", c);
            if (lamp.HasProperty("_BaseColor")) lamp.SetColor("_BaseColor", c);
            if (lamp.HasProperty("_EmissionColor")) lamp.SetColor("_EmissionColor", c * 1.2f);
        }

        public static FeriPowerLever Build(Vector3 deskTop, FeriBlackout owner)
        {
            var go = new GameObject("PowerLever");
            go.transform.position = deskTop;
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;                       // the player's interact ray hits triggers; nothing bumps into it
            col.center = new Vector3(0f, 0.22f, 0f);
            col.size = new Vector3(0.4f, 0.5f, 0.45f);
            var lever = go.AddComponent<FeriPowerLever>();
            lever.blackout = owner;

            Material steel = MapMaterial("steel", new Color(0.55f, 0.55f, 0.58f));
            Material dark = MapMaterial("dark", new Color(0.12f, 0.12f, 0.13f));
            Material red = MapMaterial("red", new Color(0.8f, 0.1f, 0.08f));
            Material yellow = MapMaterial(null, new Color(0.95f, 0.75f, 0.1f));
            lever.lamp = MapMaterial(null, Color.red);
            if (lever.lamp.HasProperty("_EmissionMap")) lever.lamp.SetTexture("_EmissionMap", Texture2D.whiteTexture);
            lever.lamp.EnableKeyword("_EMISSION");

            Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f), new Vector3(0.26f, 0.02f, 0.32f), yellow);    // warning plate
            Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.06f, 0f), new Vector3(0.16f, 0.08f, 0.24f), dark);     // housing
            Part(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.101f, 0f), new Vector3(0.03f, 0.005f, 0.18f), Black()); // slot
            Part(go.transform, PrimitiveType.Sphere, new Vector3(0.06f, 0.11f, -0.09f), Vector3.one * 0.035f, lever.lamp);   // lamp

            lever.handle = new GameObject("Handle").transform;
            lever.handle.SetParent(go.transform, false);
            lever.handle.localPosition = new Vector3(0f, 0.09f, 0f);
            Part(lever.handle, PrimitiveType.Cylinder, new Vector3(0f, 0.16f, 0f), new Vector3(0.03f, 0.16f, 0.03f), steel); // rod
            Part(lever.handle, PrimitiveType.Sphere, new Vector3(0f, 0.33f, 0f), Vector3.one * 0.07f, red);                // knob

            lever.ResetLever();
            return lever;
        }

        static Material Black() => MapMaterial(null, Color.black);

        static void Part(Transform parent, PrimitiveType type, Vector3 at, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // a copy of one of the map's own materials (so it's lit like the map), tinted, lit by the dynamic lights
        // instead of baked vertex colours (primitives have none); falls back to any PS1 material in the scene
        static Material MapMaterial(string name, Color tint)
        {
            Renderer src = null;
            if (name != null) { var g = GameObject.Find(name); if (g != null) src = g.GetComponent<Renderer>(); }
            bool own = src != null && src.sharedMaterial != null;   // keeps its texture; otherwise plain tinted
            if (src == null) { var g = GameObject.Find("door"); if (g != null) src = g.GetComponent<Renderer>(); }
            Material m = src != null && src.sharedMaterial != null ? new Material(src.sharedMaterial)
                                                                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "Lever (runtime)";
            if (!own)
            {
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            }
            if (m.HasProperty("_Color")) m.SetColor("_Color", own ? Color.white : tint);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", own ? Color.white : tint);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 0f);
            return m;
        }
    }
}
