// Prologue - the bed.
// Press E on the bed -> lie down, eyes close, eyes open and you're falling through the map,
// then the FERI map loads.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using FeriMap;

namespace Prologue
{
    public class PrologueBed : FeriInteractable
    {
        public string prompt = "[E] Go to sleep";
        [Tooltip("Scene loaded at the end of the fall (must be in Build Settings).")]
        public string nextScene = "FERI_Map";

        [Header("Timing")]
        public float lieDownTime = 1.2f;
        public float eyesClosedTime = 1.2f;
        public float fallTime = 4f;
        public float fallGravity = 12f;
        [Tooltip("Slow spin while falling, degrees per second.")]
        public float spin = 25f;

        public AudioSource glitchEffect;

        [Tooltip("Camera pose when lying in bed. Created automatically on the pillow if empty.")]
        public Transform sleepPoint;

        bool sleeping;
        float black;   // 0 = clear, 1 = black

        public override string Prompt => sleeping ? "" : prompt;

        public override void Interact(FeriPlayer player)
        {
            if (sleeping || player == null || player.cameraPivot == null) return;
            sleeping = true;
            StartCoroutine(Sleep(player));
        }

        IEnumerator Sleep(FeriPlayer player)
        {
            Transform cam = player.cameraPivot;
            player.enabled = false;                                   // no walking, no crosshair

            // lie down, eyes close
            Vector3 p0 = cam.position; Quaternion r0 = cam.rotation;
            for (float t = 0; t < 1f; t += Time.deltaTime / lieDownTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                cam.SetPositionAndRotation(Vector3.Lerp(p0, sleepPoint.position, k), Quaternion.Slerp(r0, sleepPoint.rotation, k));
                black = Mathf.Clamp01((t - 0.5f) * 2f);
                yield return null;
            }
            cam.SetPositionAndRotation(sleepPoint.position, sleepPoint.rotation);
            black = 1f;
            yield return new WaitForSeconds(eyesClosedTime);

            // eyes open: falling through the bed, the floor, the ground...
            cam.SetParent(null, true);
            glitchEffect.Play();
            if (PrologueMusic.Instance) PrologueMusic.Instance.FadeOut(fallTime);
            float speed = 0f;
            for (float t = 0; t < fallTime; t += Time.deltaTime)
            {
                speed += fallGravity * Time.deltaTime;
                cam.position += Vector3.down * speed * Time.deltaTime;
                cam.Rotate(0f, 0f, spin * Time.deltaTime, Space.Self);
                float fadeIn = Mathf.Clamp01(1f - t / 0.6f);                 // eyes open
                float fadeOut = Mathf.Clamp01((t - (fallTime - 1f)) / 1f);  // black again at the end
                black = Mathf.Max(fadeIn, fadeOut);
                yield return null;
            }
            black = 1f;
            SceneManager.LoadScene(nextScene);
        }

        void OnGUI()
        {
            if (black <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, black);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ setup
        void Reset() { EnsureCollider(); EnsureSleepPoint(); }
        void Awake() { EnsureCollider(); EnsureSleepPoint(); }

        /// <summary>Adds a BoxCollider around the bed if it has none, so the player's look-ray can hit it.</summary>
        public void EnsureCollider()
        {
            if (GetComponentInChildren<Collider>() != null) return;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1.06f, 1.08f, 2.06f));
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.center = b.center;
            bc.size = b.size;
        }

        /// <summary>Head on the pillow, looking up at the ceiling. The headboard is at local -Z.</summary>
        public void EnsureSleepPoint()
        {
            if (sleepPoint) return;
            var t = transform.Find("SleepPoint");
            if (t == null)
            {
                t = new GameObject("SleepPoint").transform;
                t.SetParent(transform, false);
                t.localPosition = new Vector3(0f, 0.78f, -0.66f);
                t.localRotation = Quaternion.LookRotation(new Vector3(0f, 1f, 0.45f), Vector3.back);
            }
            sleepPoint = t;
        }
    }
}
