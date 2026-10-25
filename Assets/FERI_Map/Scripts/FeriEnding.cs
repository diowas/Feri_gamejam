// THE ENDING. You did everything FeriEscape asked: read the board, found the key, fixed the power, pressed 5.
// The doors open onto the dark roof, and it is standing right there: two pale eyes and a grin, a little above yours.
// It stares. Then its arm comes in past you (a black silhouette in the cabin light), presses 0, the doors close
// on its face, and the game is over.
// Starts when the elevator arrives at the "5" floor that FeriEscape adds. The entity (FifthFloorEntity) is built
// in code when the doors open. Added to the elevator by itself.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FeriMap
{
    public class FeriEnding : MonoBehaviour
    {
        [Tooltip("How far beyond the doors it stands (metres).")]
        public float entityDistance = 1.3f;
        public float stareTime = 4.5f;
        public float reachTime = 2.4f;
        public string endText = "THE END";
        [Tooltip("Scene loaded after the end card. Empty = the first scene in Build Settings.")]
        public string nextScene = "";
        public float endHold = 7f;
        [Tooltip("Starts the moment it's revealed. Empty = Resources/EndSound (Assets/Soundtracks/Resources/EndSound.mp3).")]
        public AudioClip endSound;
        [Range(0f, 1f)] public float endSoundVolume = 1f;

        FeriElevator elevator;
        Shader shader;
        FifthFloorEntity entity;
        AudioSource drone;
        bool started;
        Transform cam;
        Quaternion look;
        float fade, textAlpha;
        GUIStyle style;

        void Start()
        {
            elevator = GetComponent<FeriElevator>();
            if (elevator == null || elevator.cabin == null) { enabled = false; return; }
            var anyRenderer = elevator.cabin.GetComponentInChildren<Renderer>();
            shader = anyRenderer ? anyRenderer.sharedMaterial.shader : Shader.Find("FERI/PS1");
            elevator.Arrived += OnArrived;
        }

        void OnDestroy() { if (elevator != null) elevator.Arrived -= OnArrived; }

        void OnArrived(int floor)
        {
            if (started || floor < 0 || floor >= elevator.floors.Count || elevator.floors[floor].label != "5") return;
            started = true;
            SpawnEntity(elevator.floors[floor].y);   // the doors are only starting to open
            PlayEndSound();
            StartCoroutine(Ending());
        }

        void Awake()
        {
            if (!endSound) endSound = Resources.Load<AudioClip>("EndSound");
            if (endSound) endSound.LoadAudioData();   // ready to start on the very frame the doors open
        }

        // not on the entity: a 2D source that keeps playing through the fade and the end card
        void PlayEndSound()
        {
            if (!endSound) return;
            var a = gameObject.AddComponent<AudioSource>();
            a.clip = endSound; a.volume = endSoundVolume;
            a.spatialBlend = 0f; a.playOnAwake = false; a.loop = false;
            a.Play();
        }

        void SpawnEntity(float floorY)
        {
            // where the doors are, from the cabin's own door panels (still shut)
            Bounds doors = elevator.cabinDoorA.GetComponent<Renderer>().bounds;
            doors.Encapsulate(elevator.cabinDoorB.GetComponent<Renderer>().bounds);
            Vector3 o = elevator.transform.rotation * Vector3.left;      // the builder puts the doors on the cabin's -X side
            Vector3 door = doors.center; door.y = floorY;

            var go = new GameObject("FifthFloorEntity");
            go.transform.position = door + o * entityDistance;
            go.transform.rotation = Quaternion.LookRotation(-o);
            entity = go.AddComponent<FifthFloorEntity>();
            entity.shader = shader;
            entity.Build();
            if (Camera.main) entity.lookTarget = Camera.main.transform;

            drone = go.AddComponent<AudioSource>();
            drone.clip = Drone();
            drone.loop = true; drone.spatialBlend = 0.6f; drone.volume = 0f; drone.playOnAwake = false;
        }

        IEnumerator Ending()
        {
            StopTheRest();
            FeriPlayer player = FeriPlayer.All.Count > 0 ? FeriPlayer.All[0] : null;
            if (player) player.inputEnabled = false;
            cam = Camera.main ? Camera.main.transform : null;
            if (cam) { entity.lookTarget = cam; look = cam.rotation; }
            var cabinLight = elevator.cabin.GetComponentInChildren<PS1Light>();
            if (cabinLight) cabinLight.flicker = 0.55f;
            drone.Play();

            // the doors open on it. it stares, and the grin keeps widening
            for (float t = 0f; t < stareTime; t += Time.deltaTime)
            {
                drone.volume = Mathf.Clamp01(t / 2f) * 0.8f;
                entity.grin = Mathf.SmoothStep(0f, 1f, t / stareTime);
                LookAt(entity.FacePoint, 1.6f);
                yield return null;
            }

            // its arm comes in past you, bending through the doorway, to the panel
            int floor0 = elevator.floors.FindIndex(f => f.label == "0");
            var b0 = floor0 >= 0 ? elevator.floors[floor0].cabinButton : null;
            if (b0 != null)
            {
                Vector3 target = b0.buttonRenderer.bounds.center;
                Vector3 o = elevator.transform.rotation * Vector3.left;
                Vector3 door = entity.transform.position - o * entityDistance;
                Vector3 side = Vector3.Project(target - door, Vector3.Cross(Vector3.up, o));
                entity.reachSide = entity.transform.InverseTransformPoint(target).x >= 0f ? 1 : -1;
                entity.reachTarget = target;
                entity.reachElbow = door + side.normalized * 0.35f + Vector3.up * 1.95f - o * 0.15f;
                for (float t = 0f; t < reachTime; t += Time.deltaTime)
                {
                    float k = Mathf.SmoothStep(0f, 1f, t / reachTime);
                    entity.reach = k;
                    LookAt(Vector3.Lerp(entity.FacePoint, target, k * 0.75f), 2.5f);
                    yield return null;
                }

                // click
                elevator.Press(b0);
                AudioSource.PlayClipAtPoint(Click(), target, 1f);
                yield return new WaitForSeconds(0.35f);

                // the arm snaps back and the doors close on its face
                for (float t = 0f; t < 0.4f; t += Time.deltaTime)
                {
                    entity.reach = 1f - t / 0.4f;
                    LookAt(entity.FacePoint, 6f);
                    yield return null;
                }
                entity.reach = 0f;
                for (float t = 0f; t < 4f && elevator.state != FeriElevator.State.Moving; t += Time.deltaTime)
                {
                    LookAt(entity.FacePoint, 6f);
                    yield return null;
                }
            }

            // going down, into black
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                fade = t / 2.5f;
                drone.volume = 0.8f * (1f - fade);
                if (cam) cam.rotation = look;
                yield return null;
            }
            fade = 1f;
            drone.Stop();
            yield return new WaitForSeconds(1f);
            for (float t = 0f; t < 1.5f; t += Time.deltaTime) { textAlpha = t / 1.5f; yield return null; }
            textAlpha = 1f;
            yield return new WaitForSeconds(endHold);

            // a fresh start: the escape's progress, the power and the jumpscare darkness all survive a scene load
            FeriEscape.ResetForNewGame();
            FeriBlackout.ResetState();
            JumpscareDirector.ResetDarkness();
            if (!string.IsNullOrEmpty(nextScene)) SceneManager.LoadScene(nextScene);
            else SceneManager.LoadScene(0);
        }

        // the player's head is turned for them: it overrides FeriPlayer's mouse look every frame
        void LookAt(Vector3 p, float speed)
        {
            if (!cam) return;
            look = Quaternion.Slerp(look, Quaternion.LookRotation(p - cam.position), 1f - Mathf.Exp(-speed * Time.deltaTime));
            cam.rotation = look;
        }

        // nothing else gets to happen during the ending
        static void StopTheRest()
        {
            foreach (var m in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (m is TrickOrTreater || m is NullRefEntity || m is FloatingEyes || m is CeilingWatcher || m is JumpscareDirector)
                    m.enabled = false;
            foreach (var a in FindObjectsByType<UnityEngine.AI.NavMeshAgent>(FindObjectsSortMode.None))
                if (a.isOnNavMesh) a.isStopped = true;
        }

        void OnGUI()
        {
            GUI.depth = -100;   // above PS1Camera's picture (100) and the mission / timer HUD (-10)
            if (fade <= 0f || Event.current.type != EventType.Repaint) return;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            if (textAlpha > 0f && !string.IsNullOrEmpty(endText))
            {
                if (style == null)
                {
                    style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                    var font = Resources.Load<Font>("FeriFont");
                    if (font) style.font = font;
                }
                style.fontSize = Mathf.RoundToInt(Screen.height / 18f);
                GUI.color = new Color(0.62f, 0.58f, 0.47f, textAlpha);   // the Prologue's warm grey
                GUI.Label(new Rect(0, 0, Screen.width, Screen.height), endText, style);
            }
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- sounds, generated like the elevator's
        static AudioClip Drone()
        {
            const int sr = 22050; int n = sr * 4;
            var data = new float[n];
            var r = new System.Random(3); float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                lp = lp * 0.995f + ((float)r.NextDouble() * 2f - 1f) * 0.005f;
                // two low tones a hair apart beat against each other; the loop is 4 s so they line up again
                data[i] = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 41.5f * t) * 0.25f + lp * 4f;
            }
            var c = AudioClip.Create("FifthFloorDrone", n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip Click()
        {
            const int sr = 22050; int n = sr / 8;
            var data = new float[n];
            var r = new System.Random(9);
            for (int i = 0; i < n; i++) data[i] = ((float)r.NextDouble() * 2f - 1f) * Mathf.Exp(-i / (sr * 0.006f)) * 0.9f;
            var c = AudioClip.Create("Click", n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }

        // ---------------------------------------------------------------- auto-setup
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SceneManager.sceneLoaded -= OnSceneLoaded; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TryAdd();
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m) => TryAdd();

        static void TryAdd()
        {
            var e = FindAnyObjectByType<FeriElevator>();
            if (e && !e.GetComponent<FeriEnding>()) e.gameObject.AddComponent<FeriEnding>();
        }
    }
}
