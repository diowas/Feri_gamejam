// Prologue soundtrack: fades in when the scene starts, loops,
// and fades out while the player falls (PrologueBed calls FadeOut).
using System.Collections;
using UnityEngine;

namespace Prologue
{
    [RequireComponent(typeof(AudioSource))]
    public class PrologueMusic : MonoBehaviour
    {
        public static PrologueMusic Instance { get; private set; }

        [Range(0f, 1f)] public float volume = 0.6f;
        public float fadeInTime = 3f;

        AudioSource src;

        void Awake()
        {
            Instance = this;
            src = GetComponent<AudioSource>();
            src.loop = true;
            src.spatialBlend = 0f;      // 2D, same volume everywhere
            src.playOnAwake = false;
            src.volume = 0f;
        }

        void Start()
        {
            src.Play();
            StartCoroutine(FadeTo(volume, fadeInTime));
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void FadeOut(float time)
        {
            StopAllCoroutines();
            StartCoroutine(FadeTo(0f, time));
        }

        IEnumerator FadeTo(float target, float time)
        {
            float start = src.volume;
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, time))
            {
                src.volume = Mathf.Lerp(start, target, t);
                yield return null;
            }
            src.volume = target;
        }
    }
}
