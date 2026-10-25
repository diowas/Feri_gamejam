// The main menu: shown over the first scene (the Prologue) when the game starts, and again after the ending
// (FeriEnding loads the first scene). Everything behind it is frozen (time and sound) until you press Play.
// Escape opens it at any time as a pause menu (CONTINUE instead of PLAY), and Escape again closes it.
// Builds itself: nothing to put in a scene. Drawn with IMGUI in the Prologue's font and colours.
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FeriMap
{
    public class FeriMainMenu : MonoBehaviour
    {
        public string title = "FNAF";
        public string subtitle = "Last night at FERI";

        /// <summary>Set by FeriEnding just before it goes back to the start (the menu looks the same either way).</summary>
        public static bool AfterEnding;

        static FeriMainMenu open;
        bool again, paused;
        int openedFrame;
        float timeScaleBefore = 1f;
        float shownAt;
        Font font;
        GUIStyle titleStyle, subStyle, buttonStyle;
        static readonly Color Warm = new Color(0.62f, 0.58f, 0.47f);   // the Prologue's text colour

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            AfterEnding = false;
            open = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AddEscapeKey()
        {
            var watcher = new GameObject("FeriMenuKey");
            watcher.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(watcher);
            watcher.AddComponent<EscapeKey>();
        }

        // Escape: opens the menu as a pause menu, or closes it again
        class EscapeKey : MonoBehaviour
        {
            void Update()
            {
                if (!FeriInput.Escape()) return;
                if (open == null)
                {
                    var menu = new GameObject("FeriMainMenu").AddComponent<FeriMainMenu>();
                    menu.paused = true;
                }
                else if (open.openedFrame != Time.frameCount) open.Play();
            }
        }

        // the scene you start in: in the editor the opened scene is already loaded when Play starts, so sceneLoaded
        // doesn't always fire for it
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FirstScene() { OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single); }

        // the first scene in Build Settings = the start of the game: when you launch it, and after the ending
        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (m != LoadSceneMode.Single || s.buildIndex != 0 || open != null) return;
            var menu = new GameObject("FeriMainMenu").AddComponent<FeriMainMenu>();
            menu.again = AfterEnding;
            AfterEnding = false;
        }

        void Awake()
        {
            open = this;
            shownAt = Time.unscaledTime;
            openedFrame = Time.frameCount;
            timeScaleBefore = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            font = Resources.Load<Font>("FeriFont");
        }

        void OnDestroy()
        {
            if (open == this) open = null;
        }

        void LateUpdate()
        {
            // keep the mouse free for the buttons, whatever the scene underneath does with it
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void Play()
        {
            Time.timeScale = timeScaleBefore;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Destroy(gameObject);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnGUI()
        {
            GUI.depth = -100;   // above everything, PS1Camera's picture (100) and the HUDs (-10) included
            float u = Mathf.Max(2f, Screen.height / 360f);   // one PS1 pixel
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                buttonStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                if (font != null) titleStyle.font = subStyle.font = buttonStyle.font = font;
            }
            titleStyle.fontSize = Mathf.RoundToInt(u * 36f);
            subStyle.fontSize = Mathf.RoundToInt(u * 9f);
            buttonStyle.fontSize = Mathf.RoundToInt(u * 12f);

            float t = Time.unscaledTime - shownAt;
            float fadeIn = Mathf.Clamp01(t / (paused ? 0.25f : 1.2f));

            // black, with the faintest flicker
            GUI.color = new Color(0f, 0f, 0f, 0.96f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            float flick = Mathf.PerlinNoise(Time.unscaledTime * 7f, 0.5f) < 0.18f ? 0.45f : 1f;   // the title stutters now and then
            Shadowed(new Rect(0f, cy - u * 95f, Screen.width, u * 50f), title, titleStyle, new Color(Warm.r, Warm.g, Warm.b, fadeIn * flick), u);
            Shadowed(new Rect(0f, cy - u * 52f, Screen.width, u * 14f), subtitle, subStyle,
                new Color(Warm.r, Warm.g, Warm.b, fadeIn * 0.7f), u);

            if (Button(new Rect(cx - u * 60f, cy - u * 10f, u * 120f, u * 20f), paused ? "CONTINUE" : "PLAY", fadeIn, u) || (fadeIn >= 1f && Enter()))
                Play();
            if (Button(new Rect(cx - u * 60f, cy + u * 16f, u * 120f, u * 20f), "QUIT", fadeIn, u))
                Quit();
            GUI.color = Color.white;
        }

        bool Button(Rect r, string label, float alpha, float u)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            GUI.color = new Color(hover ? 0.18f : 0.08f, hover ? 0.16f : 0.07f, hover ? 0.12f : 0.06f, 0.9f * alpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(Warm.r, Warm.g, Warm.b, (hover ? 0.9f : 0.35f) * alpha);
            GUI.DrawTexture(new Rect(r.x, r.y, u * 2f, r.height), Texture2D.whiteTexture);   // accent bar, like the mission box
            Shadowed(r, label, buttonStyle, new Color(hover ? 1f : Warm.r, hover ? 0.95f : Warm.g, hover ? 0.8f : Warm.b, alpha), u);
            return alpha >= 1f && Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover;
        }

        static bool Enter()
        {
            var e = Event.current;
            return e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space);
        }

        static void Shadowed(Rect r, string s, GUIStyle st, Color c, float u)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.9f * c.a);
            GUI.Label(new Rect(r.x + u * 0.8f, r.y + u * 0.8f, r.width, r.height), s, st);
            GUI.color = c;
            GUI.Label(r, s, st);
        }
    }
}
