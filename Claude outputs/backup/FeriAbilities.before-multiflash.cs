// The 5 switches on the hand display (FeriPlayerBody), in order. Each one runs on a timer (durations) and then
// flips itself back off:
//   1 FLASH  - spawns a comically big flashlight in front of you that floats above the floor and shines ahead; [E] puts it
//              in your right palm with a fresh 5 s battery (HUD bottom right). It flickers in its last second.
//   2 XRAY   - monsters glow through walls
//   3 NOCLIP - walk through walls and props; a ray keeps the feet on the floor (FeriPlayer.noclip)
//   4 SLOW   - every monster runs at slowScale (FeriSlow clock + NavMeshAgent speeds)
//   5 BHOP   - hold Space to hop automatically, every hop adds speed (FeriPlayer.bhop)
// FeriPlayer adds this component by itself.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FeriMap
{
    [RequireComponent(typeof(FeriPlayer))]
    public class FeriAbilities : MonoBehaviour
    {
        public const int Flash = 0, XRay = 1, NoClip = 2, Slow = 3, BHop = 4;

        [Tooltip("Seconds each ability lasts: FLASH, XRAY, NOCLIP, SLOW, BHOP.")]
        public float[] durations = { 5f, 5f, 5f, 5f, 30f };

        [Header("FLASH")]
        [Tooltip("Length of the spawned flashlight (metres).")]
        public float flashlightLength = 0.9f;
        [Tooltip("Length of the flashlight once it's in your hand (metres).")]
        public float heldLength = 0.35f;
        [Tooltip("How high the spawned flashlight floats above the floor (metres).")]
        public float hoverHeight = 1f;
        [Tooltip("How far in front of the player it spawns (metres).")]
        public float spawnDistance = 1.2f;
        [Tooltip("The player's own flashlight starts off and F does nothing: light only comes from the FLASH switch.")]
        public bool flashlightOnlyFromAbility = true;

        [Header("XRAY")]
        public Color xrayColor = new Color(1f, 0.25f, 0.15f, 1f);

        [Header("SLOW")]
        [Range(0.05f, 1f)] public float slowScale = 0.3f;

        FeriPlayer player;
        FeriPlayerBody body;
        float[] until = { -1f, -1f, -1f, -1f, -1f };
        GameObject handLight;   // the spawned flashlight
        float dropFloor, dropSpeed;
        GameObject heldLight;   // the same flashlight once it's been picked up, in the right palm
        bool landed;
        PS1Flashlight beam;
        Material xrayMat;
        readonly HashSet<Renderer> xrayed = new HashSet<Renderer>();
        float nextXrayScan;
        readonly Dictionary<NavMeshAgent, Vector2> agentSpeed = new Dictionary<NavMeshAgent, Vector2>();   // x = script's speed, y = what we set
        float nextAgentScan;

        void Awake()
        {
            player = GetComponent<FeriPlayer>();
        }

        void OnEnable() { FeriPlayerBody.SwitchToggled += OnSwitch; }

        void OnDisable()
        {
            FeriPlayerBody.SwitchToggled -= OnSwitch;
            SetXRay(false);
            SetSlow(false);
            DropFlashlight();
        }

        void Start()
        {
            body = GetComponent<FeriPlayerBody>();
            if (flashlightOnlyFromAbility)
            {
                player.flashlightKey = false;
                if (player.flashlight != null) player.flashlight.on = false;
            }
        }

        bool IsOn(int i) => body != null && body.switches != null && i < body.switches.Length && body.switches[i];

        /// <summary>Seconds left on ability i, 0 when it's off.</summary>
        public float Remaining(int i) => i >= 0 && i < until.Length && until[i] > 0f ? Mathf.Max(0f, until[i] - Time.time) : 0f;

        void OnSwitch(int i, bool on)
        {
            if (i >= 0 && i < until.Length)
                until[i] = on ? Time.time + (durations != null && i < durations.Length ? durations[i] : 5f) : -1f;
            switch (i)
            {
                case Flash: if (on) SpawnFlashlight(); else DropFlashlight(); break;
                case XRay: SetXRay(on); break;
                case NoClip: player.noclip = on; break;
                case Slow: SetSlow(on); break;
                case BHop: player.bhop = on; break;
            }
        }

        void Update()
        {
            // timers run out -> the switch flips back off (which switches the ability off)
            for (int i = 0; i < until.Length; i++)
                if (until[i] > 0f && Time.time >= until[i])
                {
                    until[i] = -1f;
                    if (body != null && IsOn(i)) body.SetSwitch(i, false);
                    else OnSwitch(i, false);
                }
            if (heldLight != null && beam != null)
            {
                if (player.enabled && player.inputEnabled && FeriInput.Flashlight()) beam.on = !beam.on;
                if (!beam.on && until[Flash] > 0f) until[Flash] += Time.deltaTime;   // battery paused while off
            }
            if (beam != null) beam.flicker = Remaining(Flash) < 1f ? 0.6f : 0f;
            if (handLight != null)
            {
                Vector3 p = handLight.transform.position;
                if (!landed)
                {
                    dropSpeed += 9.81f * Time.deltaTime;
                    p.y = Mathf.Max(dropFloor, p.y - dropSpeed * Time.deltaTime);
                    landed = p.y <= dropFloor;
                }
                else p.y = dropFloor + Mathf.Sin(Time.time * 2.5f) * 0.06f;   // floats and bobs
                handLight.transform.position = p;
            }

            // monsters spawned after the switch went on
            if (IsOn(XRay) && Time.time > nextXrayScan) { nextXrayScan = Time.time + 0.5f; ApplyXRay(); }
        }

        void LateUpdate()
        {
            if (!IsOn(Slow)) return;
            if (Time.time > nextAgentScan)
            {
                nextAgentScan = Time.time + 1f;
                foreach (var a in FindObjectsByType<NavMeshAgent>(FindObjectsSortMode.None))
                    if (!agentSpeed.ContainsKey(a)) agentSpeed[a] = new Vector2(a.speed, -1f);
            }
            // monster scripts change agent.speed all the time (patrol / chase): pick up their value, scale it
            var keys = new List<NavMeshAgent>(agentSpeed.Keys);
            foreach (var a in keys)
            {
                if (a == null) { agentSpeed.Remove(a); continue; }
                Vector2 s = agentSpeed[a];
                if (!Mathf.Approximately(a.speed, s.y)) s.x = a.speed;
                s.y = s.x * slowScale;
                a.speed = s.y;
                agentSpeed[a] = s;
            }
        }

        // ------------------------------------------------------------------ FLASH
        // A real flashlight object (PlayerBody/Resources/AbilityFlashlight, built by PlayerBodySetup) spawns in front of
        // the player, drops onto the floor and shines the way you were looking. It carries its own PS1Flashlight.
        void SpawnFlashlight()
        {
            DropFlashlight();
            Transform cam = player.cameraPivot;
            if (cam == null) return;

            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 pos = cam.position + fwd * spawnDistance;
            // don't spawn it inside a wall
            if (Physics.Raycast(cam.position, fwd, out RaycastHit wall, spawnDistance + 0.6f, ~0, QueryTriggerInteraction.Ignore))
                pos = wall.point - fwd * 0.6f;

            handLight = MakeFlashlight(pos, Quaternion.LookRotation(fwd), flashlightLength);
            if (handLight == null) return;
            handLight.name = "SpawnedFlashlight";
            var col = handLight.AddComponent<CapsuleCollider>();
            col.direction = 2; col.height = 1.2f; col.radius = 0.3f;   // a bit bigger than the model, easier to aim at
            handLight.AddComponent<FeriFlashlightPickup>().owner = this;
            if (beam != null) beam.on = false;   // off until you pick it up and press F

            // falls to hoverHeight above the floor, then bobs there (no Rigidbody: it tunnelled through the floor)
            col.enabled = false;   // don't let the ray hit itself
            float feet = transform.position.y;   // the floor you're standing on
            dropFloor = (Physics.Raycast(pos, Vector3.down, out RaycastHit floor, 20f, ~0, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(floor.point.y, feet) : feet) + hoverHeight;
            col.enabled = true;
            dropSpeed = 0f;
            landed = false;
            int rends = handLight.GetComponentsInChildren<Renderer>().Length;
            Debug.Log($"FLASH: spawned flashlight at {pos}, floats at y={dropFloor:F2} (you are at y={transform.position.y:F2}), {rends} renderer(s)", handLight);
        }

        // The flashlight model (PlayerBody/Resources/AbilityFlashlight: 1 m long, lens at +Z) with its own beam.
        GameObject MakeFlashlight(Vector3 pos, Quaternion rot, float length)
        {
            var prefab = Resources.Load<GameObject>("AbilityFlashlight");
            if (prefab == null) return null;
            var model = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            var part = model.GetComponent<FeriPlayerBodyPart>();
            bool ps1 = FindAnyObjectByType<PS1Lighting>() != null;
            Material baseMat = part != null ? (ps1 && part.ps1Material != null ? part.ps1Material : part.litMaterial) : null;
            // flashlight.fbx is only a Git LFS pointer when LFS isn't installed: no mesh at all, so build one from primitives
            bool primitive = model.GetComponentInChildren<Renderer>() == null;
            if (primitive) BuildPrimitiveFlashlight(model.transform, baseMat);
            // normalise here too, from the renderer bounds (centred, longest axis along +Z, 1 m), in case the
            // prefab was built before the setup could measure the model
            Bounds bb = default; bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
            }
            var go = new GameObject();
            if (any && bb.size.sqrMagnitude > 1e-8f)
            {
                Vector3 e = bb.size;
                Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right : e.y >= e.z ? Vector3.up : Vector3.forward;
                float longest = Vector3.Dot(e, axis);
                var inner = new GameObject("Model").transform;
                inner.SetParent(go.transform, false);
                inner.localRotation = Quaternion.FromToRotation(axis, Vector3.forward);
                inner.localScale = Vector3.one / longest;
                model.transform.SetParent(inner, false);      // measured at the origin, so its bounds centre is in model space
                model.transform.localPosition = -bb.center;
            }
            else model.transform.SetParent(go.transform, true);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * length;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!primitive && baseMat != null) r.sharedMaterial = baseMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var src = player.flashlight;
            var b = new GameObject("Beam").transform;
            b.SetParent(go.transform, false);
            b.localPosition = new Vector3(0, 0, 0.5f);
            beam = b.gameObject.AddComponent<PS1Flashlight>();   // becomes PS1Flashlight.Active
            if (src != null)
            {
                beam.color = src.color; beam.intensity = src.intensity; beam.range = src.range;
                beam.outerAngle = src.outerAngle; beam.innerAngle = src.innerAngle;
            }
            beam.on = true;
            return go;
        }

        // A chunky cartoon flashlight, 1 m long along +Z with the lens at +Z, centred on the origin.
        static void BuildPrimitiveFlashlight(Transform parent, Material baseMat)
        {
            Material body = Tint(baseMat, new Color(0.16f, 0.16f, 0.18f), false);
            Material metal = Tint(baseMat, new Color(0.55f, 0.55f, 0.6f), false);
            Material grip = Tint(baseMat, new Color(0.08f, 0.08f, 0.08f), false);
            Material lens = Tint(baseMat, new Color(1f, 0.93f, 0.6f), true);
            Material button = Tint(baseMat, new Color(0.8f, 0.1f, 0.08f), false);
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, -0.45f), 0.20f, 0.05f, metal);   // tail cap
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, -0.12f), 0.18f, 0.62f, body);    // tube
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, -0.20f), 0.19f, 0.06f, grip);    // grip rings
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, -0.05f), 0.19f, 0.06f, grip);
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.22f), 0.25f, 0.08f, metal);    // neck
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.36f), 0.32f, 0.20f, body);     // head
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.465f), 0.34f, 0.03f, metal);   // bezel
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.475f), 0.27f, 0.02f, lens);    // lens
            var b = Part(parent, PrimitiveType.Cube, new Vector3(0, 0.095f, 0.05f), 0f, 0f, button); // switch
            b.localRotation = Quaternion.identity;
            b.localScale = new Vector3(0.06f, 0.04f, 0.1f);
        }

        static Transform Part(Transform parent, PrimitiveType type, Vector3 at, float diameter, float length, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>());
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);              // cylinder axis Y -> Z
            t.localScale = new Vector3(diameter, length * 0.5f, diameter); // unity cylinder is 2 m tall
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        static Material Tint(Material src, Color c, bool glow)
        {
            Material m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "Flashlight (runtime)";
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (glow)
            {
                if (m.HasProperty("_Emission")) m.SetFloat("_Emission", 1f);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 1.5f);
                m.EnableKeyword("_EMISSION");
            }
            return m;
        }

        /// <summary>The spawned flashlight jumps into the right palm for the rest of the FLASH timer.</summary>
        public void PickUpFlashlight()
        {
            if (handLight != null) Destroy(handLight);
            handLight = null;
            beam = null;
            Transform cam = player.cameraPivot;
            if (cam == null) return;
            Transform hand = Find(cam, "hand.R"), finger = Find(cam, "f_middle.01.R"), tip = Find(cam, "f_middle.03.R");

            Vector3 grip = cam.TransformPoint(new Vector3(0.2f, -0.22f, 0.4f));
            if (hand != null && finger != null)
            {
                grip = Vector3.Lerp(hand.position, finger.position, 0.55f);
                // the palm side is where the relaxed fingers curl to
                Vector3 palm = tip != null ? Vector3.ProjectOnPlane(tip.position - finger.position, finger.position - hand.position) : Vector3.zero;
                if (palm.sqrMagnitude > 1e-8f) grip += palm.normalized * Vector3.Distance(hand.position, finger.position) * 0.35f;
            }
            heldLight = MakeFlashlight(grip, Quaternion.LookRotation(cam.forward, cam.up), heldLength);
            if (heldLight == null) return;
            heldLight.name = "HeldFlashlight";
            Debug.Log($"FLASH: picked up, in {(hand != null ? "hand.R" : "camera")} at {grip}", heldLight);
            heldLight.transform.SetParent(hand != null ? hand : cam, true);
            until[Flash] = Time.time + BatteryLife;   // fresh battery; it only drains while the light is on (F)
            if (beam != null) beam.on = false;        // picked up switched off
        }

        float BatteryLife => durations != null && durations.Length > Flash ? durations[Flash] : 5f;

        // Battery HUD, bottom right, while the flashlight is in your hand: 5 chunky cells that drain with the FLASH timer.
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || heldLight == null || !player.enabled) return;
            float left = Remaining(Flash), k = Mathf.Clamp01(left / Mathf.Max(BatteryLife, 0.01f));
            float u = Mathf.Max(2f, Screen.height / 360f);   // one PS1 pixel
            float w = u * 40f, h = u * 16f, m = u * 10f;
            var body = new Rect(Screen.width - m - w - u * 3f, Screen.height - m - h, w, h);
            Color col = k > 0.5f ? new Color(0.35f, 0.95f, 0.45f) : k > 0.25f ? new Color(1f, 0.8f, 0.25f) : new Color(1f, 0.25f, 0.2f);
            if (left < 1f && Mathf.Repeat(Time.time * 8f, 1f) < 0.5f) col.a = 0.25f;   // dying battery blinks

            Box(new Rect(body.x - u, body.y - u, body.width + u * 2f, body.height + u * 2f), new Color(0, 0, 0, 0.75f));
            Box(body, new Color(0.85f, 0.85f, 0.8f, 0.9f));                                                    // casing
            Box(new Rect(body.xMax, body.y + h * 0.3f, u * 3f, h * 0.4f), new Color(0.85f, 0.85f, 0.8f, 0.9f));   // + terminal
            Box(new Rect(body.x + u, body.y + u, body.width - u * 2f, body.height - u * 2f), new Color(0.05f, 0.05f, 0.05f, 0.95f));
            const int Cells = 5;
            float cw = (body.width - u * 2f - u * (Cells + 1)) / Cells;
            int lit = Mathf.CeilToInt(k * Cells - 0.001f);
            for (int i = 0; i < lit; i++)
                Box(new Rect(body.x + u * 2f + i * (cw + u), body.y + u * 2f, cw, body.height - u * 4f), col);
            GUI.color = Color.white;
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        void DropFlashlight()
        {
            if (handLight != null) Destroy(handLight);
            if (heldLight != null) Destroy(heldLight);
            handLight = heldLight = null;
            beam = null;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }

        // ------------------------------------------------------------------ SLOW
        void SetSlow(bool on)
        {
            FeriSlow.Scale = on ? slowScale : 1f;
            if (!on)
            {
                foreach (var kv in agentSpeed)
                    if (kv.Key != null && Mathf.Approximately(kv.Key.speed, kv.Value.y)) kv.Key.speed = kv.Value.x;
                agentSpeed.Clear();
            }
            nextAgentScan = 0f;
        }

        // ------------------------------------------------------------------ XRAY
        void SetXRay(bool on)
        {
            if (on) { ApplyXRay(); return; }
            foreach (var r in xrayed)
            {
                if (r == null) continue;
                var mats = new List<Material>(r.sharedMaterials);
                mats.RemoveAll(m => m == xrayMat);
                r.sharedMaterials = mats.ToArray();
            }
            xrayed.Clear();
        }

        void ApplyXRay()
        {
            if (xrayMat == null)
            {
                var shader = Resources.Load<Shader>("FERI_XRay");
                if (shader == null) shader = Shader.Find("FERI/XRay");
                if (shader == null) return;
                xrayMat = new Material(shader) { name = "XRay (runtime)" };
            }
            xrayMat.SetColor("_Color", xrayColor);
            foreach (var root in Monsters())
                foreach (var r in root.GetComponentsInChildren<Renderer>())
                {
                    if (xrayed.Contains(r) || r is ParticleSystemRenderer) continue;
                    var mats = new List<Material>(r.sharedMaterials) { xrayMat };
                    r.sharedMaterials = mats.ToArray();
                    xrayed.Add(r);
                }
        }

        static IEnumerable<Component> Monsters()
        {
            foreach (var m in FindObjectsByType<TrickOrTreater>(FindObjectsSortMode.None)) yield return m;
            foreach (var m in FindObjectsByType<NullRefEntity>(FindObjectsSortMode.None)) yield return m;
            foreach (var m in FindObjectsByType<CeilingWatcher>(FindObjectsSortMode.None)) yield return m;
            foreach (var m in FindObjectsByType<FloatingEyes>(FindObjectsSortMode.None)) yield return m;
        }
    }
}
