// Prologue mission box, top left - same look as "Fix the lights in Alfa" in the FERI map (FeriBlackout).
// It waits until every object in "Wait For" has been shown and then hidden again
// (the intro canvas and the "I should go to sleep" text), then shows the mission.
// Pressing Tab (the device on your hand) completes it: the box turns green for a few seconds and fades out.
using UnityEngine;
using UnityEngine.SceneManagement;
using FeriMap;

namespace Prologue
{
    public class PrologueObjective : MonoBehaviour
    {
        [Tooltip("The mission appears once all of these have disappeared (e.g. the intro canvas and the sleep text).")]
        public GameObject[] waitFor;
        [Tooltip("Extra seconds after the texts are gone before the mission appears.")]
        public float delay = 1f;

        [Header("Text")]
        public string title = "Check out the device";
        public string sub = "Press [Tab]";
        public string doneTitle = "Device checked";

        bool[] seen;
        float shownAt = -1f, doneAt = -1f, goneAt = -1f;
        GUIStyle titleStyle, textStyle;

        public bool IsShown => shownAt >= 0f && doneAt < 0f;
        public bool IsDone => doneAt >= 0f;

        void Start()
        {
            if (waitFor == null || waitFor.Length == 0) FindTexts();
            seen = new bool[waitFor != null ? waitFor.Length : 0];
        }

        // ------------------------------------------------------------------ puts itself into the Prologue scene
        // No need to add this to the scene: whenever a scene called "Prologue" loads, it creates itself
        // and waits for the texts of the `text` script (its intro canvas and sleep text).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Prologue") return;
            if (FindAnyObjectByType<PrologueObjective>(FindObjectsInactive.Include) != null) return;
            var go = new GameObject("PrologueObjective");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<PrologueObjective>();
        }

        /// <summary>Reads the intro canvas and the sleep text from the `text` script (without changing it).</summary>
        void FindTexts()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb.GetType().Name != "text") continue;
                foreach (var field in new[] { "introCanvas", "txtSleep" })
                {
                    var f = mb.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    var v = f != null ? f.GetValue(mb) : null;
                    if (v is GameObject g && g) list.Add(g);
                    else if (v is Component c && c) list.Add(c.gameObject);
                }
            }
            if (list.Count == 0) Debug.LogWarning("PrologueObjective: couldn't find the `text` script's texts - the mission shows straight away.", this);
            waitFor = list.ToArray();
        }

        void Update()
        {
            if (shownAt < 0f)
            {
                bool allGone = true;
                for (int i = 0; i < seen.Length; i++)
                {
                    var go = waitFor[i];
                    if (go == null) continue;
                    if (go.activeInHierarchy) { seen[i] = true; allGone = false; }
                    else if (!seen[i]) allGone = false;          // not shown yet - wait for it to appear first
                }
                if (!allGone) { goneAt = -1f; return; }
                if (goneAt < 0f) goneAt = Time.time;
                if (Time.time - goneAt >= delay) shownAt = Time.time;
                return;
            }
            if (doneAt < 0f && FeriInput.Watch()) doneAt = Time.time;
        }

        /// <summary>Show the mission right now (skips the waiting).</summary>
        public void Show() { if (shownAt < 0f) shownAt = Time.time; }

        /// <summary>Mark the mission done from another script.</summary>
        public void Complete() { if (shownAt >= 0f && doneAt < 0f) doneAt = Time.time; }

        void OnGUI()
        {
            // above PS1Camera's game picture (it draws at depth 100)
            GUI.depth = -10;
            if (Event.current.type != EventType.Repaint || shownAt < 0f) return;

            bool done = doneAt >= 0f;
            float fade = done ? Mathf.Clamp01((4f - (Time.time - doneAt)) / 1f) : 1f;   // "done" stays up a few seconds
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

            string line = done ? doneTitle : title;
            string small = done ? null : sub;
            Color accent = done ? new Color(0.45f, 1f, 0.5f) : new Color(1f, 0.8f, 0.3f);
            if (!done && Time.time - shownAt < 2f && Mathf.Repeat(Time.time * 4f, 1f) < 0.5f) accent.a = 0.3f;   // blinks as it appears

            float w = u * 130f, h = small != null ? u * 26f : u * 15f;
            var box = new Rect(m, m, w, h);
            Box(new Rect(box.x, box.y, u * 2f, box.height), new Color(accent.r, accent.g, accent.b, 0.9f * fade * accent.a));   // accent bar
            Box(new Rect(box.x + u * 2f, box.y, box.width, box.height), new Color(0f, 0f, 0f, 0.55f * fade));
            Label(new Rect(box.x + u * 6f, box.y + u * 2f, w, u * 11f), line, textStyle, new Color(1f, 1f, 1f, fade), u);
            if (small != null)
                Label(new Rect(box.x + u * 6f, box.y + u * 13f, w, u * 11f), small, titleStyle, new Color(0.8f, 0.8f, 0.8f, fade), u);
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
    }
}
