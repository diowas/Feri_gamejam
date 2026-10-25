// The escape. Reading the left whiteboard in Alfa ("IF YOU WANT TO ESCAPE, GET TO THE FIFTH FLOOR. YOU HAVE 10 MINUTES.")
// starts a 10 minute timer (top right) and the player says "I have to hurry.". Then:
//   1. the elevator has a new button, 5 (the roof), that only works with a key in the keyhole next to it
//   2. a note on the desk in P02 says the key is in the parking garage, lowest floor (on the roof of the blue car)
//   3. the first time the elevator gets down there, it dies: lights out, buttons dead
//   4. the breaker on the shaft wall on parking floor -1 brings it back
//   5. key in, press 5: the doors open onto the empty, dark roof (built by FERI > Ponovno zgradi jasek in streho)
// Progress survives dying (the scene reloads); running out of time starts everything over.
// The objectives show in FeriBlackout's mission box. FeriAbilities adds this to the player.
using System.Collections;
using UnityEngine;

namespace FeriMap
{
    public class FeriEscape : MonoBehaviour
    {
        [Tooltip("Seconds to get to the fifth floor.")]
        public float timeLimit = 10f * 60f;
        [Tooltip("Reading the board from this far away counts (metres).")]
        public float readDistance = 14f;
        public Vector3 notePosition = new Vector3(37.55f, 0.762f, 32.3f);     // P02, the desk at the front
        public Vector3 keyPosition = new Vector3(4.25f, -5.945f, 27.0f);       // parking -2, roof of the blue car
        public Vector3 breakerPosition = new Vector3(18.5f, -2.6f, 9.0f);      // parking -1, elevator shaft, east side
        public Vector3 breakerFacing = Vector3.right;
        [Tooltip("Height of the roof stop (top of the roof slab).")]
        public float roofY = 22.4f;

        // progress, kept through the scene reload when you die
        static bool started, noteRead, hasKey, keyInserted, powerCut, powerFixed, escaped;
        static float timeLeft;

        public static bool TimerShown => started && !escaped;

        FeriPlayer player;
        FeriElevator elevator;
        int roofIndex = -1, lowestIndex = -1;
        float cutAt = -1f;
        GameObject key, keyInSlot;
        Material keyGlow;
        GameObject roofBlocker;   // invisible wall in the cabin doorway once you're on the roof
        /// <summary>The key lying in the garage (null once taken), so XRAY can show it.</summary>
        public static GameObject Key { get; private set; }
        Transform breakerHandle;
        Material breakerLamp;
        bool reading;
        float readingSince;
        GUIStyle timerStyle, noteStyle;
        static Font font;

        void Awake() { player = GetComponent<FeriPlayer>(); }

        IEnumerator Start()
        {
            yield return null;   // let the elevator set itself up first
            BuildNote();
            if (!hasKey) BuildKey();
            BuildBreaker();
            SetupElevator();
        }

        // ------------------------------------------------------------------ progress
        void Update()
        {
            if (!started && SeesBoard())
            {
                started = true;
                timeLeft = timeLimit;
                FeriThought.Say("I have to hurry.");
            }
            if (started && !escaped)
            {
                timeLeft -= Time.deltaTime;
                if (timeLeft <= 0f) { TimeUp(); return; }
            }
            if (cutAt > 0f && Time.time >= cutAt) { cutAt = -1f; CutPower(); }
            if (roofBlocker != null && roofBlocker.activeSelf != escaped) roofBlocker.SetActive(escaped);
            if (keyGlow != null)   // the key on the car pulses a little so you can spot it
                keyGlow.SetColor("_EmissionColor", new Color(0.55f, 0.4f, 0.1f) * (0.6f + 0.4f * Mathf.Sin(Time.time * 3f)));
            if (reading && (Time.time - readingSince > 8f || Vector3.Distance(transform.position, notePosition) > 2.5f)) reading = false;
            if (breakerLamp != null)
            {
                bool dead = powerCut && !powerFixed;
                Color c = dead ? (Mathf.Repeat(Time.time * 2f, 1f) < 0.5f ? new Color(1f, 0.1f, 0.05f) : new Color(0.25f, 0.02f, 0.02f))
                               : new Color(0.2f, 1f, 0.3f);
                breakerLamp.SetColor("_Color", c);
                if (breakerLamp.HasProperty("_EmissionColor")) breakerLamp.SetColor("_EmissionColor", c);
            }
        }

        bool SeesBoard()
        {
            Transform cam = player != null ? player.cameraPivot : null;
            if (cam == null) return false;
            Vector3 to = FeriAlfaWhiteboard.LeftBoard - cam.position;
            float d = to.magnitude;
            if (d > readDistance || Vector3.Angle(cam.forward, to) > 22f) return false;
            // nothing in the way
            return !Physics.Raycast(cam.position, to / d, d - 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        void TimeUp()
        {
            ResetProgress();
            FeriBlackout.ResetState();
            UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        static void ResetProgress()
        {
            started = noteRead = hasKey = keyInserted = powerCut = powerFixed = escaped = false;
            timeLeft = 0f;
        }

        /// <summary>FeriEnding, before going back to the start: the next run begins from scratch.</summary>
        public static void ResetForNewGame() { ResetProgress(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ResetProgress(); }

        /// <summary>What the mission box (top left) should say, or false when the escape hasn't started / is over.</summary>
        public static bool Objective(out string line, out string sub)
        {
            line = sub = null;
            if (!started || escaped) return false;
            line = "Get to the fifth floor";
            if (!hasKey) sub = noteRead ? "The key is in the parking garage, lowest floor" : "The elevator needs a key";
            else if (!keyInserted) sub = "Put the key in the elevator panel";
            else sub = "Press 5 in the elevator";
            return true;
        }

        /// <summary>The extra mission while the elevator has no power (shown under the escape's).</summary>
        public static bool PowerObjective(out string line, out string sub)
        {
            line = "Get the elevator running again";
            sub = "Fix the power on parking floor -1";
            return started && !escaped && powerCut && !powerFixed;
        }

        // ------------------------------------------------------------------ elevator
        void SetupElevator()
        {
            elevator = FindAnyObjectByType<FeriElevator>();
            if (elevator == null || elevator.cabin == null) return;
            float low = float.MaxValue;
            for (int i = 0; i < elevator.floors.Count; i++)
                if (elevator.floors[i].enabled && elevator.floors[i].y < low) { low = elevator.floors[i].y; lowestIndex = i; }

            roofIndex = elevator.AddFloor(new FeriElevator.Floor
            {
                label = "5", y = roofY, enabled = keyInserted,
                ledRect = new Vector4(0.75f, 0.4375f, 0.84375f, 0.5f),   // "ledblank" in the atlas: no digit for 5
            });
            elevator.Arrived += OnArrived;
            BuildRoofBlocker();
            if (powerCut && !powerFixed) elevator.SetPowered(false);

            // button 5 above 4, keyhole above 3 (copies of those buttons, with their own little textures)
            Transform b4 = Find(elevator.cabin, "Btn_4"), b3 = Find(elevator.cabin, "Btn_3");
            if (b4 != null)
            {
                var b5 = Instantiate(b4.gameObject, b4.parent);
                b5.name = "Btn_5";
                b5.transform.localPosition = b4.localPosition + Vector3.up * 0.1f;
                var btn = b5.GetComponent<FeriElevatorButton>();
                btn.floorIndex = roofIndex;
                btn.held = false;
                elevator.floors[roofIndex].cabinButton = btn;
                // Resources/FeriButton5.png: the atlas's "4" button with a 5 on it
                var t5 = Resources.Load<Texture2D>("FeriButton5");
                if (t5 != null) { t5.filterMode = FilterMode.Point; t5.wrapMode = TextureWrapMode.Clamp; }
                Retexture(b5.GetComponent<Renderer>(), t5 != null ? t5 : ButtonTexture(true), new Vector4(0.6875f, 0.5625f, 0.75f, 0.625f));   // btn4's atlas rect
            }
            // the old "-3" button reads S now (Resources/FeriButtonS.png: the atlas's "-3" button with an S on it)
            Transform bm3 = Find(elevator.cabin, "Btn_-3");
            var tS = Resources.Load<Texture2D>("FeriButtonS");
            if (bm3 != null && tS != null)
            {
                tS.filterMode = FilterMode.Point; tS.wrapMode = TextureWrapMode.Clamp;
                Retexture(bm3.GetComponent<Renderer>(), tS, new Vector4(0.25f, 0.5625f, 0.3125f, 0.625f));   // btn-3's atlas rect
            }
            if (b3 != null)
            {
                var hole = Instantiate(b3.gameObject, b3.parent);
                hole.name = "Keyhole";
                hole.transform.localPosition = b3.localPosition + Vector3.up * 0.1f;
                Destroy(hole.GetComponent<FeriElevatorButton>());
                Retexture(hole.GetComponent<Renderer>(), ButtonTexture(false), new Vector4(0.625f, 0.5625f, 0.6875f, 0.625f));   // btn3's
                var q = hole.AddComponent<FeriQuestInteractable>();
                q.prompt = () => keyInserted ? "" : "[E] Insert the key";
                q.action = () =>
                {
                    if (!hasKey) return;
                    keyInserted = true;
                    elevator.floors[roofIndex].enabled = true;
                    if (keyInSlot != null) keyInSlot.SetActive(true);
                };
                keyInSlot = MakeKey(hole.transform, 0.35f);
                keyInSlot.transform.localPosition = new Vector3(0f, 0f, 0.02f);
                keyInSlot.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);   // blade into the panel, ring sticking out
                keyInSlot.SetActive(keyInserted);
            }
        }

        void OnArrived(int floor)
        {
            if (floor == lowestIndex && started && !powerCut) cutAt = Time.time + 2.2f;   // once the doors are open
            if (floor == roofIndex) escaped = true;
        }

        // the roof is the end: the doors open onto it, but you stay in the cabin. The two cabin doors slide apart
        // symmetrically, so their midpoint is always the middle of the doorway, whether they're open or not.
        void BuildRoofBlocker()
        {
            Transform a = elevator.cabinDoorA, b = elevator.cabinDoorB;
            if (a == null || b == null || a.parent == null) return;
            roofBlocker = new GameObject("RoofDoorBlocker");
            roofBlocker.transform.SetParent(a.parent, false);
            roofBlocker.transform.localPosition = (a.localPosition + b.localPosition) * 0.5f;
            Vector3 slide = elevator.doorSlideAxis.normalized, across = Vector3.Cross(slide, Vector3.up).normalized;
            Vector3 size = slide * 1.6f + Vector3.up * 3f + across * 0.2f;
            roofBlocker.AddComponent<BoxCollider>().size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            roofBlocker.SetActive(escaped);
        }

        void CutPower()
        {
            powerCut = true;
            powerFixed = false;
            if (elevator != null) elevator.SetPowered(false);
            FeriThought.Say("The elevator died... I have to get the power back on.");
        }

        // ------------------------------------------------------------------ the note (P02)
        void BuildNote()
        {
            var note = FeriScrawl.Decal("ElevatorNote", new[] { "THE ELEVATOR KEY", "IS IN THE PARKING", "GARAGE,", "LOWEST FLOOR." },
                notePosition, Quaternion.Euler(90f, 0f, 8f), 0.04f, 2, new Color(0.08f, 0.08f, 0.14f), Color.black, false, 3,
                "birch", new Color(0.92f, 0.9f, 0.82f));
            var col = note.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(1f, 1f, 0.2f);
            var q = note.AddComponent<FeriQuestInteractable>();
            q.prompt = () => reading ? "[E] Put it down" : "[E] Read the note";
            q.action = () =>
            {
                reading = !reading;
                readingSince = Time.time;
                noteRead = true;
            };
        }

        // ------------------------------------------------------------------ the key (parking -2)
        void BuildKey()
        {
            key = MakeKey(null, 1.4f);
            key.transform.position = keyPosition;
            Key = key;
            keyGlow = key.GetComponentInChildren<Renderer>().sharedMaterial;
            if (!keyGlow.HasProperty("_EmissionColor")) keyGlow = null;
            key.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            var col = key.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = new Vector3(0.35f, 0.15f, 0.35f);
            var q = key.AddComponent<FeriQuestInteractable>();
            q.prompt = () => "[E] Take the elevator key";
            q.action = () => { hasKey = true; Key = null; keyGlow = null; Destroy(key); };
        }

        // a chunky brass key lying flat: ring, blade, teeth; scale 1 is about 12 cm long
        GameObject MakeKey(Transform parent, float scale)
        {
            var root = new GameObject("ElevatorKey");
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * scale;
            Material brass = Tinted("steel", new Color(0.95f, 0.75f, 0.3f), new Color(0.25f, 0.18f, 0.04f));   // faint glint in the dark
            Part(root.transform, PrimitiveType.Cylinder, new Vector3(-0.045f, 0.004f, 0f), new Vector3(0.05f, 0.004f, 0.05f), brass);   // ring
            Part(root.transform, PrimitiveType.Cube, new Vector3(0.02f, 0.004f, 0f), new Vector3(0.08f, 0.008f, 0.014f), brass);       // blade
            Part(root.transform, PrimitiveType.Cube, new Vector3(0.04f, 0.004f, 0.012f), new Vector3(0.012f, 0.008f, 0.012f), brass);  // teeth
            Part(root.transform, PrimitiveType.Cube, new Vector3(0.055f, 0.004f, 0.01f), new Vector3(0.01f, 0.008f, 0.008f), brass);
            return root;
        }

        // ------------------------------------------------------------------ the breaker (parking -1)
        void BuildBreaker()
        {
            var root = new GameObject("ElevatorBreaker").transform;
            root.SetPositionAndRotation(breakerPosition, Quaternion.LookRotation(breakerFacing, Vector3.up));   // local +z = out of the wall
            Material grey = Tinted("dark", new Color(0.3f, 0.32f, 0.34f), Color.black);
            Material yellow = Tinted(null, new Color(0.95f, 0.75f, 0.1f), Color.black);
            Material red = Tinted("red", new Color(0.8f, 0.1f, 0.08f), Color.black);
            breakerLamp = Tinted(null, Color.red, Color.red);
            if (breakerLamp.HasProperty("_EmissionMap")) breakerLamp.SetTexture("_EmissionMap", Texture2D.whiteTexture);

            Part(root, PrimitiveType.Cube, new Vector3(0f, 0f, 0.08f), new Vector3(0.5f, 0.7f, 0.16f), grey);         // box
            Part(root, PrimitiveType.Cube, new Vector3(0f, 0.27f, 0.165f), new Vector3(0.4f, 0.08f, 0.01f), yellow);  // warning strip
            Part(root, PrimitiveType.Sphere, new Vector3(0.17f, 0.27f, 0.17f), Vector3.one * 0.05f, breakerLamp);     // lamp
            Part(root, PrimitiveType.Cube, new Vector3(0f, -0.05f, 0.165f), new Vector3(0.05f, 0.3f, 0.01f), Tinted(null, Color.black, Color.black)); // slot
            breakerHandle = new GameObject("Handle").transform;
            breakerHandle.SetParent(root, false);
            breakerHandle.localPosition = new Vector3(0f, -0.05f, 0.17f);
            Part(breakerHandle, PrimitiveType.Cube, new Vector3(0f, 0f, 0.06f), new Vector3(0.03f, 0.03f, 0.12f), grey);   // arm
            Part(breakerHandle, PrimitiveType.Cube, new Vector3(0f, 0f, 0.12f), new Vector3(0.12f, 0.04f, 0.04f), red);    // grip
            SetBreaker(!(powerCut && !powerFixed));

            var col = root.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0f, 0.15f);
            col.size = new Vector3(0.6f, 0.8f, 0.35f);
            var q = root.gameObject.AddComponent<FeriQuestInteractable>();
            q.prompt = () => powerCut && !powerFixed ? "[E] Turn the elevator power back on" : "";
            q.action = () =>
            {
                powerFixed = true;
                SetBreaker(true);
                if (elevator != null) elevator.SetPowered(true);
            };
        }

        void SetBreaker(bool on)
        {
            if (breakerHandle != null) breakerHandle.localRotation = Quaternion.Euler(on ? -50f : 50f, 0f, 0f);   // up = on
        }

        // ------------------------------------------------------------------ HUD
        void OnGUI()
        {
            GUI.depth = -10;   // above PS1Camera's game picture (depth 100)
            if (Event.current.type != EventType.Repaint) return;
            if (font == null) font = Resources.Load<Font>("FeriFont");
            if (timerStyle == null)
            {
                timerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Bold };
                noteStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                if (font != null) { timerStyle.font = font; noteStyle.font = font; }
            }
            float u = Mathf.Max(2f, Screen.height / 360f);
            if (TimerShown)
            {
                // top right, the time left
                int s = Mathf.Max(0, Mathf.CeilToInt(timeLeft));
                string text = (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
                timerStyle.fontSize = Mathf.RoundToInt(u * 12f);
                Color c = timeLeft < 60f ? new Color(1f, 0.25f, 0.2f) : new Color(0.62f, 0.58f, 0.47f);
                if (timeLeft < 60f && Mathf.Repeat(Time.time * 2f, 1f) < 0.3f) c.a = 0.4f;
                var r = new Rect(Screen.width - u * 10f - u * 80f, u * 6f, u * 80f, u * 16f);
                GUI.color = new Color(0f, 0f, 0f, 0.9f * c.a);
                GUI.Label(new Rect(r.x + u * 0.8f, r.y + u * 0.8f, r.width, r.height), text, timerStyle);
                GUI.color = c;
                GUI.Label(r, text, timerStyle);
            }
            if (reading)
            {
                // the note, held up in front of you
                float w = Screen.height * 0.8f, h = Screen.height * 0.55f;
                var paper = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(paper.x + u * 3f, paper.y + u * 3f, w, h), Texture2D.whiteTexture);
                GUI.color = new Color(0.92f, 0.9f, 0.82f);
                GUI.DrawTexture(paper, Texture2D.whiteTexture);
                noteStyle.fontSize = Mathf.RoundToInt(u * 16f);
                GUI.color = new Color(0.1f, 0.1f, 0.16f);
                GUI.Label(new Rect(paper.x + w * 0.1f, paper.y, w * 0.8f, h), "The elevator key is in the parking garage, lowest floor.", noteStyle);
            }
            GUI.color = Color.white;
        }

        /// <summary>How far down the top-right HUD (FeriAbilities) has to start, below the timer.</summary>
        public static float HudTopOffset => TimerShown ? Mathf.Max(2f, Screen.height / 360f) * 22f : 0f;

        // ------------------------------------------------------------------ helpers
        static Texture2D ButtonTexture(bool five)
        {
            const int S = 16;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = five ? "Btn5" : "Keyhole" };
            string[] art = five
                ? new[] { "................", ".##############.", ".#............#.", ".#...######...#.", ".#...#........#.", ".#...#........#.", ".#...#####....#.", ".#........#...#.",
                          ".#........#...#.", ".#........#...#.", ".#...#####....#.", ".#............#.", ".#............#.", ".#............#.", ".##############.", "................" }
                : new[] { "................", ".##############.", ".#............#.", ".#.....##.....#.", ".#....####....#.", ".#....####....#.", ".#.....##.....#.", ".#.....##.....#.",
                          ".#....####....#.", ".#............#.", ".#............#.", ".#............#.", ".#............#.", ".#............#.", ".##############.", "................" };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    char ch = art[S - 1 - y][x];
                    Color c = new Color(0.22f, 0.22f, 0.24f);                               // button face
                    if (ch == '#') c = five ? new Color(0.9f, 0.9f, 0.85f) : new Color(0.05f, 0.05f, 0.05f);
                    if (x == 0 || y == 0 || x == S - 1 || y == S - 1) c = new Color(0.55f, 0.55f, 0.58f);   // steel rim
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        // the button meshes map a cell of the signs atlas: point the copy's texture at that cell instead
        static void Retexture(Renderer r, Texture2D tex, Vector4 cell)
        {
            if (r == null) return;
            var m = new Material(r.sharedMaterial) { name = tex.name + " (runtime)" };
            m.mainTexture = tex;
            Vector2 scale = new Vector2(1f / (cell.z - cell.x), 1f / (cell.w - cell.y));
            m.mainTextureScale = scale;
            m.mainTextureOffset = new Vector2(-cell.x * scale.x, -cell.y * scale.y);
            r.sharedMaterial = m;
        }

        static Material Tinted(string mapMaterial, Color tint, Color glow)
        {
            Renderer src = null;
            if (mapMaterial != null) { var g = GameObject.Find(mapMaterial); if (g != null) src = g.GetComponent<Renderer>(); }
            if (src == null) { var g = GameObject.Find("door"); if (g != null) src = g.GetComponent<Renderer>(); }
            Material m = src != null && src.sharedMaterial != null ? new Material(src.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "Escape (runtime)";
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 0f);
            if (glow.maxColorComponent > 0f)
            {
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", Texture2D.whiteTexture);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", glow);
                m.EnableKeyword("_EMISSION");
            }
            return m;
        }

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

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
            return null;
        }
    }
}
