// First-person body: PSX arms on the camera + legs under the player.
// FeriPlayer adds this component by itself; the prefabs come from PlayerBody/Resources (see PlayerBody/Editor/PlayerBodySetup.cs).
//   Arms - legacy clips from arms_rig.fbx: "relax" as idle, "grab.R" when you interact with something, plus mouse sway.
//          A black touch display is burnt into the back of the left hand. Hold Tab to pull the hand up in front of the face
//          (2-bone IK over the animation) and the same switches show up as a big panel in the middle of the screen.
//          While it's up the mouse moves a cursor instead of the camera: click a switch, or press 1-5, to flip it (see SwitchToggled).
//   Legs - the full base character with the upper body collapsed. The legs swing procedurally while walking
//          and fold with a small 2-bone IK while crouching, so the feet stay on the floor.
// Both are hidden while FeriPlayer is disabled (e.g. lying in the Prologue bed).
using System.Linq;
using UnityEngine;

namespace FeriMap
{
    [RequireComponent(typeof(FeriPlayer))]
    public class FeriPlayerBody : MonoBehaviour
    {
        public bool showArms = true;
        public bool showLegs = true;

        [Header("Arms")]
        public string idleClip = "relax";
        public string interactClip = "grab.R";
        [Tooltip("Extra offset of the arms in camera space (metres).")]
        public Vector3 armsOffset = Vector3.zero;
        [Tooltip("Tilt the arms down (+) or up (-), degrees.")]
        public float armsPitch = 0f;
        [Tooltip("How far the arms lag behind mouse movement.")]
        public float sway = 1.5f;

        [Header("Hand display (hold Tab, keys 1-5 flip the switches)")]
        public bool handDisplay = true;
        [Tooltip("Where the left wrist goes while looking at the display, in camera space (metres).")]
        public Vector3 watchWrist = new Vector3(0.05f, -0.13f, 0.36f);
        [Tooltip("Seconds to pull the wrist up / put it back down.")]
        public float watchRaiseTime = 0.3f;
        [Tooltip("Extra distance of the display above the skin (metres).")]
        public float displayLift = 0.016f;
        [Tooltip("Size of the on-screen switch panel shown while the hand is up (fraction of the screen).")]
        [Range(0.2f, 0.9f)] public float panelSize = 0.6f;
        [Tooltip("State of the 5 toggle switches on the hand display.")]
        public bool[] switches = new bool[5];
        public string[] switchNames = { "FLASH", "XRAY", "NOCLIP", "SLOW", "BHOP" };
        /// <summary>Raised when a switch on the hand display flips: (index 0-4, new state).</summary>
        public static event System.Action<int, bool> SwitchToggled;

        [Header("Legs")]
        [Tooltip("How far behind the camera the body stands, so looking down shows the legs and not the inside of the chest.")]
        public float legsBack = 0.18f;
        [Tooltip("Leg swing at walking speed, degrees.")]
        public float stride = 28f;
        [Tooltip("Metres covered by one full step cycle (left + right) at walking speed.")]
        public float cycleLength = 1.4f;

        [Tooltip("Limit looking down to just past the point where the toes enter the bottom of the screen (degrees past that point).")]
        public bool limitLookDown = true;
        public float feetVisible = 4f;

        class Leg
        {
            public Transform thigh, calf;
            public Quaternion thighRest, calfRest;
            public float l1, l2;
        }

        FeriPlayer player;
        CharacterController cc;
        Transform arms, legs, pelvis;
        Renderer[] armsR, legsR;
        Animation anim;
        string idle, use;
        Leg left, right;
        Vector3 pelvisRest, moveDir;
        float phase, amp, swayX, swayY;
        Transform upperL, foreL, handL, fingerL, thumbL, tipL;
        float watchW;
        Transform[] knobs;
        Renderer[] tracks;
        float[] knobT;
        float knobTravel;
        Material trackOn, trackOff;
        Transform displayRoot;
        float displayLength;
        Vector2 cursor;

        void Start()
        {
            player = GetComponent<FeriPlayer>();
            cc = GetComponent<CharacterController>();
            moveDir = transform.forward;
            bool ps1 = FindAnyObjectByType<PS1Lighting>() != null;

            var armsPrefab = Resources.Load<GameObject>("PlayerArms");
            if (armsPrefab != null && player.cameraPivot != null)
            {
                arms = Instantiate(armsPrefab, player.cameraPivot, false).transform;
                arms.name = "PlayerArms";
                armsR = Setup(arms, ps1);
                anim = arms.GetComponentInChildren<Animation>();
                if (anim != null)
                {
                    anim.cullingType = AnimationCullingType.AlwaysAnimate;
                    idle = FindClip(idleClip);
                    use = FindClip(interactClip);
                    if (idle != null) { anim[idle].wrapMode = WrapMode.Loop; anim.Play(idle); }
                    if (use != null) anim[use].wrapMode = WrapMode.Once;
                }
                upperL = Find(arms, "upper_arm.L"); foreL = Find(arms, "forearm.L"); handL = Find(arms, "hand.L");
                fingerL = Find(arms, "f_middle.01.L"); thumbL = Find(arms, "thumb.01.L"); tipL = Find(arms, "f_middle.03.L");
                if (anim != null && idle != null) anim.Sample();   // build the display from the real idle pose, not the prefab pose
                if (handDisplay && upperL && foreL && handL && fingerL && thumbL && tipL) BuildDisplay(armsR.Length > 0 ? armsR[0].sharedMaterial : null);
            }

            var legsPrefab = Resources.Load<GameObject>("PlayerLegs");
            if (legsPrefab != null)
            {
                legs = Instantiate(legsPrefab, transform, false).transform;
                legs.name = "PlayerLegs";
                legs.localPosition = new Vector3(0, 0, -legsBack);
                legs.localScale = Vector3.one * ((player.standHeight - player.eyeOffset) / 1.70f);   // base character's eyes are at ~1.70 m
                legsR = Setup(legs, ps1);
                pelvis = Find(legs, "pelvis");
                if (pelvis != null) pelvisRest = pelvis.localPosition;
                left = MakeLeg("l");
                right = MakeLeg("r");
                if (limitLookDown) LimitLookDown();
            }
        }

        void LateUpdate()
        {
            bool on = player.enabled;
            Show(armsR, on && showArms);
            Show(legsR, on && showLegs);
            if (!on) return;
            Arms();
            Legs();
        }

        // ------------------------------------------------------------------ arms
        void Arms()
        {
            if (arms == null) return;
            bool active = player.inputEnabled && Cursor.lockState == CursorLockMode.Locked;
            Vector2 look = active ? FeriInput.Look() : Vector2.zero;
            bool checking = active && knobs != null && FeriInput.Watch();
            if (checking && watchW == 0f) cursor = new Vector2(Screen.width, Screen.height) * 0.5f;
            if (checking)
            {
                cursor += new Vector2(look.x, -look.y) * 20f;   // FeriInput.Look() is mouse pixels * 0.05
                cursor = new Vector2(Mathf.Clamp(cursor.x, 0, Screen.width), Mathf.Clamp(cursor.y, 0, Screen.height));
                look = Vector2.zero;
            }
            player.lookEnabled = !checking;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 8f);
            swayX = Mathf.Lerp(swayX, Mathf.Clamp(-look.x * sway, -6f, 6f), k);
            swayY = Mathf.Lerp(swayY, Mathf.Clamp(look.y * sway, -6f, 6f), k);
            arms.localRotation = Quaternion.Euler(armsPitch + swayY, swayX, 0);
            arms.localPosition = armsOffset;

            watchW = Mathf.MoveTowards(watchW, checking ? 1f : 0f, Time.deltaTime / Mathf.Max(watchRaiseTime, 0.01f));
            if (watchW > 0f)
            {
                var anim0 = new[] { upperL.localRotation, foreL.localRotation, handL.localRotation };
                PoseWatch();
                float w = Mathf.SmoothStep(0f, 1f, watchW);
                upperL.localRotation = Quaternion.Slerp(anim0[0], upperL.localRotation, w);
                foreL.localRotation = Quaternion.Slerp(anim0[1], foreL.localRotation, w);
                handL.localRotation = Quaternion.Slerp(anim0[2], handL.localRotation, w);
            }

            if (knobs != null)
            {
                if (checking)
                    for (int i = 0; i < knobs.Length; i++)
                        if (FeriInput.NumberKey(i + 1)) SetSwitch(i, !switches[i]);
                if (checking && watchW >= 1f && FeriInput.Click())
                {
                    int hit = SwitchUnderCursor();
                    if (hit >= 0) SetSwitch(hit, !switches[hit]);
                }
                for (int i = 0; i < knobs.Length; i++)
                {
                    knobT[i] = Mathf.MoveTowards(knobT[i], switches[i] ? 1f : 0f, Time.deltaTime * 10f);
                    Vector3 kp = knobs[i].localPosition;
                    knobs[i].localPosition = new Vector3(Mathf.Lerp(knobTravel, -knobTravel, knobT[i]), kp.y, kp.z);
                }
            }

            if (anim != null && use != null && active && watchW == 0f && player.Hover != null && FeriInput.Interact() && !anim.IsPlaying(use))
            {
                anim.CrossFade(use, 0.1f);
                if (idle != null) anim.CrossFadeQueued(idle, 0.25f);
            }
        }

        // ------------------------------------------------------------------ on-screen switch panel
        Rect PanelRect()
        {
            float w = Screen.width * panelSize, h = Screen.height * panelSize;
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        Rect SwitchRect(Rect panel, int i, int n)
        {
            float slot = panel.width * 0.8f / n;
            float w = slot * 0.42f, h = panel.height * 0.5f;
            float x = panel.x + panel.width * 0.1f + slot * (i + 0.5f) - w * 0.5f;
            return new Rect(x, panel.y + panel.height * 0.22f, w, h);
        }

        int SwitchUnderCursor()
        {
            if (knobs == null) return -1;
            Rect panel = PanelRect();
            for (int i = 0; i < knobs.Length; i++)
            {
                Rect r = SwitchRect(panel, i, knobs.Length);
                if (new Rect(r.x - r.width * 0.3f, r.y, r.width * 1.6f, r.height).Contains(cursor)) return i;
            }
            return -1;
        }

        static void Box(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }

        GUIStyle labelStyle;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || watchW <= 0f || knobs == null || !player.enabled) return;
            GUI.depth = -1;
            float a = Mathf.SmoothStep(0f, 1f, watchW);
            Rect full = PanelRect();
            // grows out of the centre as the hand comes up
            Rect panel = new Rect(full.center.x - full.width * 0.5f * a, full.center.y - full.height * 0.5f * a, full.width * a, full.height * a);
            float b = Mathf.Max(4f, panel.height * 0.035f);

            // burnt rim, charred blotches, black screen
            Box(new Rect(panel.x - b, panel.y - b, panel.width + b * 2f, panel.height + b * 2f), new Color(0.22f, 0.07f, 0.03f, a));
            var rnd = new System.Random(13);
            for (int i = 0; i < 16; i++)
            {
                float t = (float)rnd.NextDouble(), sz = b * (1.5f + 2f * (float)rnd.NextDouble());
                Vector2 c = i % 4 == 0 ? new Vector2(panel.x + panel.width * t, panel.y) : i % 4 == 1 ? new Vector2(panel.x + panel.width * t, panel.yMax)
                          : i % 4 == 2 ? new Vector2(panel.x, panel.y + panel.height * t) : new Vector2(panel.xMax, panel.y + panel.height * t);
                Box(new Rect(c.x - sz * 0.5f, c.y - sz * 0.5f, sz, sz), i % 5 == 3 ? new Color(0.55f, 0.13f, 0.03f, a) : new Color(0.05f, 0.02f, 0.015f, a));
            }
            Box(panel, new Color(0.02f, 0.02f, 0.02f, a));
            Box(new Rect(panel.x + b, panel.y + b, panel.width - b * 2f, panel.height - b * 2f), new Color(0.03f, 0.035f, 0.045f, a));
            if (watchW < 1f) { GUI.color = Color.white; return; }

            if (labelStyle == null) { labelStyle = new GUIStyle(GUI.skin.label); labelStyle.alignment = TextAnchor.MiddleCenter; labelStyle.fontStyle = FontStyle.Bold; }
            labelStyle.fontSize = Mathf.RoundToInt(panel.height * 0.045f);
            int hover = SwitchUnderCursor();
            for (int i = 0; i < knobs.Length; i++)
            {
                Rect r = SwitchRect(panel, i, knobs.Length);
                if (i == hover) Box(new Rect(r.x - b * 0.6f, r.y - b * 0.6f, r.width + b * 1.2f, r.height + b * 1.2f), new Color(1f, 0.85f, 0.3f, 0.9f));
                Box(r, switches[i] ? new Color(0.2f, 0.9f, 0.35f) : new Color(0.35f, 0.04f, 0.03f));
                float kh = r.height * 0.45f;
                float ky = Mathf.Lerp(r.yMax - kh, r.y, knobT[i]);   // knob slides up when on
                Box(new Rect(r.x - r.width * 0.12f, ky, r.width * 1.24f, kh), new Color(0.85f, 0.85f, 0.8f));
                GUI.color = new Color(0.65f, 1f, 0.7f, 0.8f);
                GUI.Label(new Rect(r.center.x - r.width * 2.2f, r.yMax + b, r.width * 4.4f, panel.height * 0.1f), switchNames != null && i < switchNames.Length ? switchNames[i] + "  [" + (i + 1) + "]" : (i + 1).ToString(), labelStyle);
            }

            // cursor
            float s = Mathf.Max(3f, Screen.height / 180f);
            Box(new Rect(cursor.x - s, cursor.y - s, s * 2f, s * 2f), new Color(0, 0, 0, 0.8f));
            Box(new Rect(cursor.x - s * 0.5f, cursor.y - s * 0.5f, s, s), hover >= 0 ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(1f, 1f, 1f, 0.9f));
            GUI.color = Color.white;
        }

        public void SetSwitch(int i, bool on)
        {
            if (switches == null || i < 0 || i >= switches.Length || switches[i] == on) return;
            switches[i] = on;
            if (tracks != null && i < tracks.Length && trackOn != null) tracks[i].sharedMaterial = on ? trackOn : trackOff;
            SwitchToggled?.Invoke(i, on);
        }

        // ------------------------------------------------------------------ hand display
        // Back of the hand turned up towards the eyes, fingers pointing across to the right.
        Vector3 WatchBack => (arms.up * 0.5f - arms.forward * 0.85f).normalized;

        void PoseWatch()
        {
            Vector3 target = arms.TransformPoint(watchWrist);
            Vector3 hint = arms.TransformPoint(new Vector3(-0.35f, -0.35f, 0.1f));   // elbow down and out to the left
            TwoBoneIK(upperL, foreL, handL, target, hint);
            RollPalmUp(0.85f);

            // relax the wrist: hand mostly in line with the forearm
            Vector3 fore = (handL.position - foreL.position).normalized;
            Vector3 hand = fingerL.position - handL.position;
            handL.rotation = Quaternion.FromToRotation(hand, Vector3.Slerp(hand.normalized, fore, 0.6f)) * handL.rotation;
        }

        void RollPalmUp(float amount)
        {
            // roll the forearm so the palm (and the inside of the forearm) turns up towards the eyes
            Vector3 fore = (handL.position - foreL.position).normalized;
            Vector3 palm = Vector3.ProjectOnPlane(tipL.position - fingerL.position, fingerL.position - handL.position);   // relaxed fingers curl to the palm side
            palm = Vector3.ProjectOnPlane(palm, fore);
            Vector3 eye = Vector3.ProjectOnPlane(arms.position - handL.position, fore);
            if (palm.sqrMagnitude > 1e-8f && eye.sqrMagnitude > 1e-8f)
                foreL.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(palm, eye, fore) * amount, fore) * foreL.rotation;
        }

        static void TwoBoneIK(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint)
        {
            Vector3 a = upper.position, b = lower.position, c = end.position;
            float lab = (b - a).magnitude, lbc = (c - b).magnitude;
            float lat = Mathf.Clamp((target - a).magnitude, Mathf.Abs(lab - lbc) + 0.001f, lab + lbc - 0.001f);

            // elbow: open/close it until the shoulder-wrist distance matches
            float want = Mathf.Acos(Mathf.Clamp((lab * lab + lbc * lbc - lat * lat) / (2f * lab * lbc), -1f, 1f)) * Mathf.Rad2Deg;
            float have = Vector3.Angle(a - b, c - b);
            Vector3 n = Vector3.Cross(a - b, c - b);
            if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(a - b, hint - b);
            n.Normalize();
            lower.rotation = Quaternion.AngleAxis(have - want, n) * lower.rotation;
            if (Mathf.Abs(Vector3.Angle(a - b, end.position - b) - want) > 0.5f)
                lower.rotation = Quaternion.AngleAxis(2f * (want - have), n) * lower.rotation;

            // shoulder: aim the wrist at the target, then roll the elbow towards the hint
            upper.rotation = Quaternion.FromToRotation(end.position - a, target - a) * upper.rotation;
            Vector3 axis = (target - a).normalized;
            Vector3 e0 = Vector3.ProjectOnPlane(lower.position - a, axis), e1 = Vector3.ProjectOnPlane(hint - a, axis);
            if (e0.sqrMagnitude > 1e-8f && e1.sqrMagnitude > 1e-8f)
                upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(e0, e1, axis), axis) * upper.rotation;
        }

        // Black touch display burnt into the left forearm, on the side facing the eyes: charred skin round it, 5 toggle switches (no colliders).
        void BuildDisplay(Material baseMat)
        {
            // build it with the hand raised, so the screen ends up facing the eyes
            var rest = new[] { upperL.localRotation, foreL.localRotation, handL.localRotation };
            PoseWatch();

            Vector3 h = foreL.position, f = handL.position;   // elbow -> wrist
            Vector3 axis = (f - h).normalized;
            float len = Vector3.Distance(h, f);
            Vector3 p = Vector3.Lerp(h, f, 0.5f);
            Vector3 up = Vector3.ProjectOnPlane(arms.position - p, axis).normalized;   // arms root = camera
            Vector3 side = Vector3.Cross(up, axis);

            // measure the forearm from the skinned mesh: surface height above the bone and width
            // surface height above the bone along the middle, the arm's width, then the highest point under the display's footprint
            float top = 0f, half = 0f, peak = 0f;
            float L = len * 0.55f, W = 0f;
            var smr = arms.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                var baked = new Mesh();
                smr.BakeMesh(baked, true);
                Vector3 sp = smr.transform.position; Quaternion sr = smr.transform.rotation;
                var verts = baked.vertices;
                for (int pass = 0; pass < 3; pass++)
                {
                    if (pass == 2) W = Mathf.Min((half > 0f ? half : len * 0.14f) * 1.5f, L * 0.6f);
                    foreach (var v in verts)
                    {
                        Vector3 d = sp + sr * v - p;
                        float along = Mathf.Abs(Vector3.Dot(d, axis));
                        float lat = Mathf.Abs(Vector3.Dot(d, side)), hgt = Vector3.Dot(d, up);
                        if (hgt < 0f || lat > len * 0.4f) continue;
                        if (pass == 0 && along < len * 0.2f && lat < len * 0.06f) top = Mathf.Max(top, hgt);
                        if (pass == 1 && along < len * 0.2f && hgt > top * 0.5f) half = Mathf.Max(half, lat);
                        if (pass == 2 && along < L * 0.6f && lat < W * 0.6f) peak = Mathf.Max(peak, hgt);
                    }
                }
                Destroy(baked);
            }
            if (top <= 0f) top = len * 0.12f;
            if (W <= 0f) W = Mathf.Min(len * 0.14f * 1.5f, L * 0.6f);
            float T = L * 0.05f;
            float surface = Mathf.Max(top, peak);
            var root = new GameObject("HandDisplay").transform;
            // the black slab sits just proud of the skin; only the burnt rim below it sinks into the arm
            root.SetPositionAndRotation(p + up * (surface + T * 0.5f + displayLift), Quaternion.LookRotation(axis, up));   // +Z to the wrist, +Y out of the arm

            Material burnt = SurfaceMat(baseMat, new Color(0.13f, 0.05f, 0.03f), 0f);
            Material charred = SurfaceMat(baseMat, new Color(0.03f, 0.02f, 0.015f), 0f);
            Material ember = SurfaceMat(baseMat, new Color(0.55f, 0.13f, 0.03f), 0.6f);
            Material body = SurfaceMat(baseMat, new Color(0.02f, 0.02f, 0.02f), 0f);
            Material glass = SurfaceMat(baseMat, new Color(0.025f, 0.03f, 0.04f), 1f);
            Material knob = SurfaceMat(baseMat, new Color(0.85f, 0.85f, 0.8f), 1f);
            trackOn = SurfaceMat(baseMat, new Color(0.2f, 0.9f, 0.35f), 1f);
            trackOff = SurfaceMat(baseMat, new Color(0.35f, 0.04f, 0.03f), 1f);

            // burn: raw skin right round the edge, then charred, irregular patches and a few embers
            Part(PrimitiveType.Cube, root, new Vector3(0, -T * 0.25f, 0), new Vector3(W * 1.25f, T * 0.6f, L * 1.15f), burnt);
            var rnd = new System.Random(13);
            for (int i = 0; i < 12; i++)
            {
                float a = i / 12f * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.4f;
                var pos = new Vector3(Mathf.Cos(a) * W * 0.58f, -T * (0.15f + 0.1f * (float)rnd.NextDouble()), Mathf.Sin(a) * L * 0.56f);
                var size = new Vector3(W * (0.2f + 0.2f * (float)rnd.NextDouble()), T * 0.6f, L * (0.12f + 0.15f * (float)rnd.NextDouble()));
                var patch = Part(PrimitiveType.Cube, root, pos, size, i % 4 == 1 ? ember : charred);
                patch.localRotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 90f, 0);
                if (i % 4 == 1) patch.localScale *= 0.5f;
            }

            // display: black slab sunk into the hand, dark glass, 5 switches along it
            Part(PrimitiveType.Cube, root, Vector3.zero, new Vector3(W, T, L), body);
            Part(PrimitiveType.Cube, root, new Vector3(0, T * 0.5f, 0), new Vector3(W * 0.88f, T * 0.1f, L * 0.92f), glass);
            int n = switches != null && switches.Length > 0 ? switches.Length : 5;
            if (switches == null || switches.Length != n) switches = new bool[n];
            knobs = new Transform[n]; tracks = new Renderer[n]; knobT = new float[n];
            knobTravel = W * 0.14f;
            float step = L * 0.88f / n;
            for (int i = 0; i < n; i++)
            {
                float z = -L * 0.44f + step * (i + 0.5f);
                var track = Part(PrimitiveType.Cube, root, new Vector3(0, T * 0.58f, z), new Vector3(W * 0.55f, T * 0.08f, step * 0.5f), switches[i] ? trackOn : trackOff);
                tracks[i] = track.GetComponent<Renderer>();
                knobs[i] = Part(PrimitiveType.Cube, root, new Vector3(0, T * 0.66f, z), new Vector3(W * 0.22f, T * 0.12f, step * 0.58f), knob);
                knobT[i] = switches[i] ? 1f : 0f;
            }

            root.SetParent(foreL, true);
            displayRoot = root;
            displayLength = L;
            upperL.localRotation = rest[0]; foreL.localRotation = rest[1]; handL.localRotation = rest[2];
            armsR = root.GetComponentsInChildren<Renderer>().Concat(armsR).ToArray();
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());   // never block the interact ray
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        static Material SurfaceMat(Material baseMat, Color color, float glow)
        {
            if (baseMat == null) return null;
            var m = new Material(baseMat);
            foreach (var n in new[] { "_MainTex", "_BaseMap" }) if (m.HasProperty(n)) m.SetTexture(n, null);
            foreach (var n in new[] { "_Color", "_BaseColor" }) if (m.HasProperty(n)) m.SetColor(n, color);
            if (m.HasProperty("_Emission")) m.SetFloat("_Emission", glow);   // FERI/PS1: unlit, readable in the dark
            if (glow > 0f && m.HasProperty("_EmissionColor") && !m.HasProperty("_Emission"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * glow);
            }
            return m;
        }

        // ------------------------------------------------------------------ legs
        void Legs()
        {
            if (legs == null || left == null || right == null) return;
            float dt = Time.deltaTime;
            Vector3 v = player.Velocity; v.y = 0;
            float speed = player.Grounded ? v.magnitude : 0f;
            if (speed > 0.1f) moveDir = Vector3.Slerp(moveDir, v / v.magnitude, 1f - Mathf.Exp(-dt * 10f));

            amp = Mathf.Lerp(amp, stride * Mathf.Clamp(speed / Mathf.Max(player.walkSpeed, 0.01f), 0f, 1.3f), 1f - Mathf.Exp(-dt * 8f));
            phase += dt * speed / Mathf.Max(cycleLength, 0.1f) * Mathf.PI * 2f;
            if (amp < 0.5f) phase = Mathf.MoveTowards(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, dt * 4f);   // settle with feet together

            // crouch: the camera drops by this much, so does the pelvis
            float drop = Mathf.Max(0f, player.standHeight - cc.height);
            if (pelvis != null)
            {
                pelvis.localPosition = pelvisRest;
                pelvis.position += Vector3.down * drop;
            }

            Vector3 swingAxis = Vector3.Cross(Vector3.up, moveDir);
            Vector3 bendAxis = transform.right;
            Pose(left, phase, drop, swingAxis, bendAxis);
            Pose(right, phase + Mathf.PI, drop, swingAxis, bendAxis);
        }

        void Pose(Leg leg, float ph, float drop, Vector3 swingAxis, Vector3 bendAxis)
        {
            // 2-bone IK: hip straight above the foot, hip-foot distance shortened by the crouch
            float l = leg.l1 + leg.l2;
            float d = Mathf.Clamp(l - drop, l * 0.35f, l - 0.001f);
            float hip = Mathf.Acos(Mathf.Clamp((leg.l1 * leg.l1 + d * d - leg.l2 * leg.l2) / (2f * leg.l1 * d), -1f, 1f)) * Mathf.Rad2Deg;
            float knee = 180f - Mathf.Acos(Mathf.Clamp((leg.l1 * leg.l1 + leg.l2 * leg.l2 - d * d) / (2f * leg.l1 * leg.l2), -1f, 1f)) * Mathf.Rad2Deg;

            // walking: swing forward/back, bend the knee while the leg travels forward
            float swing = Mathf.Sin(ph) * amp;
            knee += Mathf.Max(0f, Mathf.Cos(ph)) * amp * 1.4f;

            leg.thigh.localRotation = leg.thighRest;
            leg.thigh.rotation = Quaternion.AngleAxis(-hip, bendAxis) * Quaternion.AngleAxis(-swing, swingAxis) * leg.thigh.rotation;
            leg.calf.localRotation = leg.calfRest;
            leg.calf.rotation = Quaternion.AngleAxis(knee, bendAxis) * leg.calf.rotation;
        }

        // Look-down limit: the angle at which the toes (standing) reach the bottom edge of the view, plus feetVisible.
        void LimitLookDown()
        {
            var cam = player.cameraPivot != null ? player.cameraPivot.GetComponentInChildren<Camera>() : null;
            Transform toeL = Find(legs, "ball_leaf_l") ?? Find(legs, "ball_l"), toeR = Find(legs, "ball_leaf_r") ?? Find(legs, "ball_r");
            if (cam == null || toeL == null || toeR == null) return;
            Vector3 eye = transform.position + Vector3.up * (player.standHeight - player.eyeOffset);
            Vector3 toe = transform.InverseTransformVector((toeL.position + toeR.position) * 0.5f - eye);
            float below = Mathf.Atan2(-toe.y, toe.z) * Mathf.Rad2Deg;   // degrees below the horizon, can be > 90 if the toes are behind the eye
            player.maxLookDown = Mathf.Clamp(below - cam.fieldOfView * 0.5f + feetVisible, 20f, 85f);
        }

        Leg MakeLeg(string side)
        {
            Transform thigh = Find(legs, "thigh_" + side), calf = Find(legs, "calf_" + side), foot = Find(legs, "foot_" + side);
            if (thigh == null || calf == null || foot == null) return null;
            return new Leg
            {
                thigh = thigh, calf = calf,
                thighRest = thigh.localRotation, calfRest = calf.localRotation,
                l1 = Vector3.Distance(thigh.position, calf.position),
                l2 = Vector3.Distance(calf.position, foot.position),
            };
        }

        // ------------------------------------------------------------------ helpers
        static Renderer[] Setup(Transform root, bool ps1)
        {
            var part = root.GetComponent<FeriPlayerBodyPart>();
            Material mat = part == null ? null : (ps1 && part.ps1Material != null ? part.ps1Material : part.litMaterial);
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled) continue;   // eyes / eyebrows switched off by the setup
                if (mat != null) r.sharedMaterial = mat;
                list.Add(r);
            }
            return list.ToArray();
        }

        static void Show(Renderer[] rs, bool on)
        {
            if (rs == null) return;
            foreach (var r in rs) if (r != null && r.enabled != on) r.enabled = on;
        }

        string FindClip(string n)
        {
            foreach (AnimationState s in anim)
                if (s.name == n || s.name.EndsWith("|" + n)) return s.name;
            return null;
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }
    }
}
