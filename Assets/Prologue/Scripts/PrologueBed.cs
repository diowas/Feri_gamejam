// Prologue - interactable bed.
// Look at the bed and press E (or left click) -> the player lies down, the screen fades to black,
// then either a new scene loads or the player wakes up again.
// Works with the existing player (FeriPlayer): it already shows the prompt and calls Interact().
//
// Hooks for later:
//   - Animator + Sleep Trigger: plays your sleep animation when the player lies down.
//   - Move Camera: turn off once the animation moves the camera itself.
//   - On Lie Down / On Asleep / On Wake Up: UnityEvents for sounds, story steps, cutscenes...
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using FeriMap;

namespace Prologue
{
    public class PrologueBed : FeriInteractable
    {
        [Header("Prompt")]
        public string sleepPrompt = "[E] Sleep";
        [Tooltip("Shown while Can Sleep is off.")]
        public string notTiredPrompt = "I'm not tired yet...";
        [Tooltip("Turn off if the player must do something first, then call AllowSleep() (e.g. from a UnityEvent).")]
        public bool canSleep = true;

        [Header("Lying down (placeholder until the real animation)")]
        [Tooltip("Camera pose when lying in bed. Created automatically on the pillow if empty.")]
        public Transform sleepPoint;
        [Tooltip("Off = the animation moves the camera itself.")]
        public bool moveCamera = true;
        public float lieDownTime = 1.6f;
        public float fadeOutTime = 1.5f;
        public float blackHoldTime = 1.5f;
        public float fadeInTime = 2f;

        [Header("Animation (optional)")]
        public Animator animator;
        public string sleepTrigger = "Sleep";
        public string wakeTrigger = "WakeUp";

        [Header("What happens next")]
        [Tooltip("Scene to load when the screen is black. Must be in Build Settings. Leave empty to wake up again.")]
        public string nextScene = "";
        public UnityEvent onLieDown;
        public UnityEvent onAsleep;
        public UnityEvent onWakeUp;

        public bool IsSleeping { get; private set; }

        float black;              // 0 = clear, 1 = black screen
        FeriPlayer sleeper;
        Vector3 camLocalPos;
        Quaternion camLocalRot;

        public override string Prompt => canSleep ? sleepPrompt : notTiredPrompt;

        public override void Interact(FeriPlayer player)
        {
            if (IsSleeping || !canSleep || player == null) return;
            StartCoroutine(SleepRoutine(player));
        }

        /// <summary>Start sleeping without pressing E (e.g. from a cutscene).</summary>
        public void Sleep()
        {
            if (FeriPlayer.All.Count > 0) Interact(FeriPlayer.All[0]);
        }

        public void AllowSleep() { canSleep = true; }
        public void BlockSleep() { canSleep = false; }

        // ------------------------------------------------------------------
        void Reset()
        {
            EnsureCollider();
            EnsureSleepPoint();
        }

        void Awake()
        {
            EnsureCollider();
            EnsureSleepPoint();
        }

        IEnumerator SleepRoutine(FeriPlayer player)
        {
            IsSleeping = true;
            sleeper = player;
            Transform cam = player.cameraPivot;
            if (cam) { camLocalPos = cam.localPosition; camLocalRot = cam.localRotation; }
            player.enabled = false;                       // no walking, no crosshair while in bed

            onLieDown.Invoke();
            if (animator && !string.IsNullOrEmpty(sleepTrigger)) animator.SetTrigger(sleepTrigger);

            if (moveCamera && cam && sleepPoint)
                yield return MoveCamera(cam, cam.position, cam.rotation, sleepPoint.position, sleepPoint.rotation, lieDownTime);

            yield return Fade(0f, 1f, fadeOutTime);
            onAsleep.Invoke();
            yield return new WaitForSeconds(blackHoldTime);

            if (!string.IsNullOrEmpty(nextScene))
            {
                SceneManager.LoadScene(nextScene);
                yield break;
            }

            // ---- wake up again
            if (animator && !string.IsNullOrEmpty(wakeTrigger)) animator.SetTrigger(wakeTrigger);
            yield return Fade(1f, 0f, fadeInTime);
            yield return new WaitForSeconds(0.4f);
            if (moveCamera && cam && sleepPoint)
            {
                Vector3 standPos = cam.parent ? cam.parent.TransformPoint(camLocalPos) : camLocalPos;
                Quaternion standRot = cam.parent ? cam.parent.rotation * camLocalRot : camLocalRot;
                yield return MoveCamera(cam, cam.position, cam.rotation, standPos, standRot, lieDownTime);
            }
            WakeUpNow();
        }

        /// <summary>Gives control back to the player straight away.</summary>
        public void WakeUpNow()
        {
            StopAllCoroutines();
            black = 0f;
            if (sleeper)
            {
                if (sleeper.cameraPivot) { sleeper.cameraPivot.localPosition = camLocalPos; sleeper.cameraPivot.localRotation = camLocalRot; }
                sleeper.enabled = true;
            }
            bool was = IsSleeping;
            IsSleeping = false;
            sleeper = null;
            if (was) onWakeUp.Invoke();
        }

        IEnumerator MoveCamera(Transform cam, Vector3 p0, Quaternion r0, Vector3 p1, Quaternion r1, float time)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, time))
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                // dip a little on the way down, like sitting on the edge first
                Vector3 p = Vector3.Lerp(p0, p1, k) + Vector3.down * Mathf.Sin(k * Mathf.PI) * 0.15f;
                cam.SetPositionAndRotation(p, Quaternion.Slerp(r0, r1, k));
                yield return null;
            }
            cam.SetPositionAndRotation(p1, r1);
        }

        IEnumerator Fade(float from, float to, float time)
        {
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, time))
            {
                black = Mathf.Lerp(from, to, t);
                yield return null;
            }
            black = to;
        }

        void OnGUI()
        {
            if (black <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.depth = -1000;
            GUI.color = new Color(0f, 0f, 0f, black);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ------------------------------------------------------------------ setup helpers
        /// <summary>Adds a BoxCollider around the whole bed if it has none, so the player's look-ray can hit it.</summary>
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

        /// <summary>Head on the pillow, looking up at the ceiling. The bed's headboard is at local -Z.</summary>
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

        void OnDrawGizmosSelected()
        {
            if (!sleepPoint) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(sleepPoint.position, 0.08f);
            Gizmos.DrawRay(sleepPoint.position, sleepPoint.forward * 0.6f);
        }
    }
}
