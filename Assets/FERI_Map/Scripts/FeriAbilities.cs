// The 5 switches on the hand display (FeriPlayerBody), in order. Each one runs on a timer (durations) and then
// flips itself back off:
//   1 FLASH  - a button, not a timer: every click spawns another comically big flashlight that floats in front of you and
//              stays there. [E] puts one in your right palm with a fresh 10 s battery (HUD bottom right) that only drains
//              while it's on (F). It flickers in its last second and stays in your hand, dead, until you grab another.
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
        public float[] durations = { 10f, 10f, 10f, 10f, 30f };

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
        GameObject heldLight;   // the flashlight in the right palm (the floating ones look after themselves: FeriFlashlightPickup)
        PS1Flashlight beam;     // its light
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
            // the lights go out 30 s in (only where the map is lit by PS1Lighting, not e.g. the Prologue)
            if (FindAnyObjectByType<PS1Lighting>() != null && GetComponent<FeriBlackout>() == null) gameObject.AddComponent<FeriBlackout>();
            if (FindAnyObjectByType<PS1Lighting>() != null && GetComponent<FeriHauntedDoors>() == null) gameObject.AddComponent<FeriHauntedDoors>();
            if (FindAnyObjectByType<PS1Lighting>() != null && GetComponent<FeriEscape>() == null) gameObject.AddComponent<FeriEscape>();
            if (flashlightOnlyFromAbility)
            {
                player.flashlightKey = false;
                if (player.flashlight != null) player.flashlight.on = false;
            }
        }

        bool IsOn(int i) => body != null && body.switches != null && i < body.switches.Length && body.switches[i];

        /// <summary>"10s" style countdown for ability i (never above its full duration), null when it's off.</summary>
        public string SecondsLabel(int i)
        {
            float left = Remaining(i);
            if (left <= 0f) return null;
            float total = durations != null && i < durations.Length ? durations[i] : 5f;
            return Mathf.Clamp(Mathf.CeilToInt(left - 0.05f), 1, Mathf.CeilToInt(total)) + "s";
        }

        /// <summary>Seconds left on ability i, 0 when it's off.</summary>
        public float Remaining(int i) => i >= 0 && i < until.Length && until[i] > 0f ? Mathf.Max(0f, until[i] - Time.time) : 0f;

        void OnSwitch(int i, bool on)
        {
            if (i == Flash)
            {
                // a push button: spawn one and spring back off, so you can spam it
                if (on) { SpawnFlashlight(); if (body != null) body.SetSwitch(Flash, false); }
                return;
            }
            if (i >= 0 && i < until.Length)
                until[i] = on ? Time.time + (durations != null && i < durations.Length ? durations[i] : 5f) : -1f;
            switch (i)
            {
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
                    if (i == Flash) { if (beam != null) beam.on = false; continue; }   // battery empty: dead, but kept in hand
                    if (body != null && IsOn(i)) body.SetSwitch(i, false);
                    else OnSwitch(i, false);
                }
            if (heldLight != null && beam != null)
            {
                if (player.enabled && player.inputEnabled && FeriInput.Flashlight() && until[Flash] > 0f) beam.on = !beam.on;
                if (!beam.on && until[Flash] > 0f) until[Flash] += Time.deltaTime;   // battery paused while off
            }
            if (beam != null) beam.flicker = Remaining(Flash) < 1f ? 0.6f : 0f;

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
            Transform cam = player.cameraPivot;
            if (cam == null) return;

            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 pos = cam.position + fwd * spawnDistance;
            // don't spawn it inside a wall
            if (Physics.Raycast(cam.position, fwd, out RaycastHit wall, spawnDistance + 0.6f, ~0, QueryTriggerInteraction.Ignore))
                pos = wall.point - fwd * 0.6f;

            // a bit of random spin so a pile of them doesn't look copy-pasted
            var go = MakeFlashlight(pos, Quaternion.LookRotation(Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * fwd), flashlightLength, false, out _);
            if (go == null) return;
            go.name = "SpawnedFlashlight";
            var col = go.AddComponent<CapsuleCollider>();
            col.direction = 2; col.height = 1.2f; col.radius = 0.3f;   // a bit bigger than the model, easier to aim at
            var pickup = go.AddComponent<FeriFlashlightPickup>();
            pickup.owner = this;

            // falls to hoverHeight above the floor, then bobs there (no Rigidbody: it tunnelled through the floor)
            col.enabled = false;   // don't let the ray hit itself
            float feet = transform.position.y;   // the floor you're standing on
            pickup.floor = (Physics.Raycast(pos, Vector3.down, out RaycastHit floor, 20f, ~0, QueryTriggerInteraction.Ignore)
                ? Mathf.Max(floor.point.y, feet) : feet) + hoverHeight;
            col.enabled = true;
        }

        // The flashlight model (PlayerBody/Resources/AbilityFlashlight: 1 m long, lens at +Z) with its own beam.
        GameObject MakeFlashlight(Vector3 pos, Quaternion rot, float length, bool withBeam, out PS1Flashlight light)
        {
            light = null;
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

            if (!withBeam) return go;   // only the held one gets a light (PS1Flashlight.Active is whichever was enabled last)
            var src = player.flashlight;
            var b = new GameObject("Beam").transform;
            b.SetParent(go.transform, false);
            b.localPosition = new Vector3(0, 0, 0.5f);
            light = b.gameObject.AddComponent<PS1Flashlight>();   // becomes PS1Flashlight.Active
            if (src != null)
            {
                light.color = src.color; light.intensity = src.intensity; light.range = src.range;
                light.outerAngle = src.outerAngle; light.innerAngle = src.innerAngle;
            }
            light.on = true;
            return go;
        }

        // A chunky cartoon flashlight, 1 m long along +Z with the lens at +Z, centred on the origin, with little
        // PS1-style pixel textures painted in code (cylinder UVs: u goes round, v goes along its length).
        static Texture2D texTube, texMetal, texGrip, texHead, texLens, texButton;

        static void BuildPrimitiveFlashlight(Transform parent, Material baseMat)
        {
            if (texTube == null) MakeFlashlightTextures();
            Material tube = Tint(baseMat, texTube, false);
            Material metal = Tint(baseMat, texMetal, false);
            Material grip = Tint(baseMat, texGrip, false);
            Material head = Tint(baseMat, texHead, false);
            Material lens = Tint(baseMat, texLens, true);
            Material button = Tint(baseMat, texButton, false);
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, -0.45f), 0.20f, 0.05f, metal);   // tail cap
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, -0.12f), 0.18f, 0.62f, tube);    // tube
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, -0.27f), 0.19f, 0.22f, grip);    // rubber grip
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.22f), 0.25f, 0.08f, metal);    // neck
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.36f), 0.32f, 0.20f, head);     // head
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.465f), 0.34f, 0.03f, metal);   // bezel
            Part(parent, PrimitiveType.Cylinder, new Vector3(0, 0, 0.475f), 0.27f, 0.02f, lens);    // lens (the cap face is planar mapped)
            var b = Part(parent, PrimitiveType.Cube, new Vector3(0, 0.095f, 0.05f), 0f, 0f, button); // switch
            b.localRotation = Quaternion.identity;
            b.localScale = new Vector3(0.06f, 0.04f, 0.1f);
        }

        static void MakeFlashlightTextures()
        {
            var rnd = new System.Random(7);
            float N() => (float)rnd.NextDouble();

            // tube: dark blue-grey anodised metal, a light streak (fake shine), a yellow warning band and a "FERI" label
            texTube = Paint(32, 32, (x, y) =>
            {
                Color c = new Color(0.17f, 0.19f, 0.24f) * (0.85f + 0.3f * N());
                if (x == 6 || x == 7) c = Color.Lerp(c, new Color(0.6f, 0.65f, 0.75f), 0.6f);   // shine
                if (y == 26 || y == 27) c = ((x / 3 + y) % 2 == 0) ? new Color(0.95f, 0.75f, 0.1f) : new Color(0.1f, 0.1f, 0.1f);
                if (y >= 17 && y <= 21 && x >= 14 && x <= 29 && Label(x - 14, y - 17)) c = new Color(0.85f, 0.85f, 0.8f);
                return c;
            });
            // brushed aluminium: streaks round the cylinder, a bright shine column, scratches
            texMetal = Paint(16, 16, (x, y) =>
            {
                float v = 0.55f + 0.12f * Mathf.Sin(y * 2.3f) + 0.08f * N();
                if (x == 3) v += 0.35f;
                if (x == 4) v += 0.15f;
                if (N() < 0.03f) v -= 0.25f;
                return new Color(v, v, v * 1.05f);
            });
            // black rubber with a diamond knurl
            texGrip = Paint(16, 16, (x, y) =>
            {
                bool ridge = ((x + y) % 4 == 0) || ((x - y + 64) % 4 == 0);
                float v = (ridge ? 0.16f : 0.06f) + 0.03f * N();
                return new Color(v, v, v);
            });
            // head: ribbed cooling fins
            texHead = Paint(32, 16, (x, y) =>
            {
                float v = (x % 4 == 0 ? 0.32f : x % 4 == 1 ? 0.22f : 0.12f) + 0.04f * N();
                if (y == 0 || y == 15) v *= 0.6f;
                return new Color(v, v * 1.02f, v * 1.1f);
            });
            // lens: silver reflector rings round a hot bulb
            texLens = Paint(16, 16, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(8f, 8f));
                if (d < 2.2f) return new Color(1f, 1f, 0.9f);
                if (d < 3.5f) return new Color(1f, 0.85f, 0.35f);
                float ring = Mathf.Repeat(d, 2f) < 1f ? 0.95f : 0.7f;
                return new Color(ring, ring * 0.95f, ring * 0.75f);
            });
            // red rubber button with a lighter top edge
            texButton = Paint(8, 8, (x, y) => y >= 6 ? new Color(1f, 0.35f, 0.3f) : new Color(0.75f, 0.08f, 0.06f) * (0.85f + 0.3f * N()));
        }

        // "FERI" in 3x5 pixel letters, 16 px wide
        static bool Label(int x, int y)
        {
            string[] rows = { "### ### ##  ### ", "#   #   # #  #  ", "##  ##  ##   #  ", "#   #   # #  #  ", "#   ### # # ### " };
            y = 4 - y;   // texture rows go bottom-up
            return x >= 0 && x < 16 && y >= 0 && y < 5 && rows[y][x] == '#';
        }

        static Texture2D Paint(int w, int h, System.Func<int, int, Color> px)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "Flashlight (runtime)" };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = px(x, y); c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
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

        static Material Tint(Material src, Texture2D tex, bool glow)
        {
            Material m = src != null ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "Flashlight (runtime)";
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (glow)
            {
                if (m.HasProperty("_Emission")) m.SetFloat("_Emission", 1f);
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(1f, 0.9f, 0.6f));
                m.EnableKeyword("_EMISSION");
            }
            return m;
        }

        /// <summary>The spawned flashlight jumps into the right palm for the rest of the FLASH timer.</summary>
        public void PickUpFlashlight(GameObject from)
        {
            if (from != null) Destroy(from);
            if (heldLight != null) Destroy(heldLight);   // swap out the old (maybe dead) one
            heldLight = null;
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
            heldLight = MakeFlashlight(grip, Quaternion.LookRotation(cam.forward, cam.up), heldLength, true, out beam);
            if (heldLight == null) return;
            heldLight.name = "HeldFlashlight";
            heldLight.transform.SetParent(hand != null ? hand : cam, true);
            until[Flash] = Time.time + BatteryLife;   // fresh battery; it only drains while the light is on (F)
            if (beam != null) beam.on = false;        // picked up switched off
        }

        float BatteryLife => durations != null && durations.Length > Flash ? durations[Flash] : 5f;

        // While the flashlight is in your hand: the F hint, bottom right (the countdown is the top-right tile).
        void OnGUI()
        {
            GUI.depth = -5;   // above PS1Camera's game picture (depth 100)
            if (Event.current.type != EventType.Repaint || !player.enabled) return;
            DrawActiveAbilities();
            if (heldLight == null || Remaining(Flash) <= 0f) return;
            float u = Mathf.Max(2f, Screen.height / 360f);   // one PS1 pixel
            float m = u * 10f;
            const string hint = "[F] Toggle flashlight";
            if (hintText == null) hintText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.LowerRight, fontStyle = FontStyle.Bold };
            hintText.fontSize = Mathf.RoundToInt(u * 7f);
            var hr = new Rect(Screen.width - m - u * 120f, Screen.height - m - u * 12f, u * 120f, u * 12f);
            GUI.color = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(hr.x + u * 0.7f, hr.y + u * 0.7f, hr.width, hr.height), hint, hintText);
            GUI.color = new Color(1f, 0.9f, 0.6f);
            GUI.Label(hr, hint, hintText);
            GUI.color = Color.white;
        }

        GUIStyle hintText;

        static readonly Color[] abilityColor =
        {
            new Color(1f, 0.85f, 0.35f),   // FLASH
            new Color(1f, 0.35f, 0.25f),   // XRAY
            new Color(0.6f, 0.8f, 1f),     // NOCLIP
            new Color(0.55f, 1f, 0.6f),    // SLOW
            new Color(1f, 0.55f, 1f),      // BHOP
        };
        GUIStyle hudText;

        // Top right: a tile for every ability that's running, with its icon, seconds left and a bar that drains.
        // FLASH shows while the flashlight in your hand still has battery.
        void DrawActiveAbilities()
        {
            float u = Mathf.Max(2f, Screen.height / 360f);   // one PS1 pixel
            float tile = u * 22f, gap = u * 4f, m = u * 10f;
            if (hudText == null) hudText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            hudText.fontSize = Mathf.RoundToInt(u * 7f);
            float x = Screen.width - m - tile;
            for (int i = 0; i < until.Length; i++)
            {
                float left = Remaining(i);
                if (left <= 0f || (i == Flash && heldLight == null)) continue;
                float total = Mathf.Max(0.01f, durations != null && i < durations.Length ? durations[i] : 5f);
                float k = Mathf.Clamp01(left / total);
                Color c = abilityColor[i];
                if (left < 1.5f && Mathf.Repeat(Time.time * 6f, 1f) < 0.5f) c.a = 0.35f;   // about to run out: blinks

                var r = new Rect(x, m + FeriEscape.HudTopOffset, tile, tile);   // under the escape timer
                Box(new Rect(r.x - u, r.y - u, r.width + u * 2f, r.height + u * 2f), new Color(0f, 0f, 0f, 0.8f));
                Box(r, new Color(0.04f, 0.04f, 0.04f, 0.9f));
                Box(new Rect(r.x, r.yMax - r.height * k, r.width, r.height * k), new Color(c.r, c.g, c.b, 0.18f));   // fill drains down
                var icon = FeriPlayerBody.Icon(i);
                if (icon != null)
                {
                    GUI.color = c;
                    GUI.DrawTexture(new Rect(r.x + u * 5f, r.y + u * 5f, u * 12f, u * 12f), icon);
                }
                Box(new Rect(r.x, r.yMax + u, r.width * k, u * 2f), c);   // bar under the tile

                string secs = SecondsLabel(i);
                GUI.color = new Color(0f, 0f, 0f, 0.9f);
                GUI.Label(new Rect(r.x + u * 0.7f, r.yMax + u * 3.7f, r.width, u * 10f), secs, hudText);   // drop shadow
                GUI.color = c;
                GUI.Label(new Rect(r.x, r.yMax + u * 3f, r.width, u * 10f), secs, hudText);
                x -= tile + gap;
            }
            GUI.color = Color.white;
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        void DropFlashlight()
        {
            if (heldLight != null) Destroy(heldLight);
            heldLight = null;
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
            if (FeriEscape.Key != null) yield return FeriEscape.Key.transform;   // the elevator key shows through walls too
        }
    }
}
