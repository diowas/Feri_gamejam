// The player thinking out loud: a line at the bottom centre, typed out a letter at a time like the Prologue's
// "I should go to sleep" text, then it fades. FeriBlackout draws it (in the Prologue's font); anything can Say().
using UnityEngine;

namespace FeriMap
{
    public static class FeriThought
    {
        const float TypeSpeed = 0.1f, Hold = 4f;

        static string line;
        static float startedAt = -99f;

        public static void Say(string text)
        {
            line = text;
            startedAt = Time.time;
        }

        /// <summary>True while a line is still being typed.</summary>
        public static bool Typing => line != null && Time.time - startedAt < line.Length * TypeSpeed;

        public static void Draw(GUIStyle style)
        {
            if (line == null) return;
            float t = Time.time - startedAt, typed = line.Length * TypeSpeed;
            if (t < 0f || t > typed + Hold + 1f) return;
            int n = Mathf.Clamp(Mathf.FloorToInt(t / TypeSpeed), 0, line.Length);
            float a = Mathf.Clamp01(typed + Hold + 1f - t);   // fades out
            style.fontSize = Mathf.RoundToInt(Screen.height / 28f);
            var r = new Rect(0f, Screen.height * 0.8f, Screen.width, Screen.height * 0.1f);
            float u = Screen.height / 360f;
            GUI.color = new Color(0f, 0f, 0f, 0.9f * a);
            GUI.Label(new Rect(r.x + u * 0.7f, r.y + u * 0.7f, r.width, r.height), line.Substring(0, n), style);
            GUI.color = new Color(0.65f, 0.65f, 0.65f, a);
            GUI.Label(r, line.Substring(0, n), style);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { line = null; startedAt = -99f; }
    }
}
