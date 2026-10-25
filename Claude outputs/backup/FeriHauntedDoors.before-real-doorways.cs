// The doors stand open onto pitch black rooms. Once the lights go out (FeriBlackout), every 12-30 s a random one slams
// shut, stays shut about a minute and then slowly creaks open again (only the slam makes a sound). The map's doors are only flat panels baked into the wall
// geometry, so each one gets a
// door leaf on a hinge laid over it, with a black "doorway" behind it that shows when it swings open.
// The leaves have no collider: they can't push the player or block the monsters. The slam is synthesised.
// FeriAbilities adds this to the player.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FeriMap
{
    public class FeriHauntedDoors : MonoBehaviour
    {
        [Tooltip("Only start once the lights have gone out.")]
        public bool onlyAfterBlackout = true;
        [Tooltip("Seconds between two doors, picked at random in this range.")]
        public Vector2 interval = new Vector2(12f, 30f);
        [Tooltip("How long a slammed door stays shut before it creaks open again (seconds, random in this range).")]
        public Vector2 closedTime = new Vector2(50f, 70f);
        [Tooltip("Prefer doors this close to the player, so you actually notice them (metres).")]
        public float nearRadius = 25f;
        [Range(0f, 1f)] public float volume = 0.8f;

        // centre, size, locked: the "door" / "door_locked" boxes from FERI_Map/Data/feri_layout.json (world space)
        struct DoorSpot
        {
            public Vector3 c, s; public bool locked;
            public DoorSpot(float x, float y, float z, float sx, float sy, float sz, bool l) { c = new Vector3(x, y, z); s = new Vector3(sx, sy, sz); locked = l; }
        }
        static readonly DoorSpot[] Spots =
        {
            new DoorSpot(22.02f, 1.05f, 27.39f, 0.04f, 2.1f, 0.98f, false),   // Hall
            new DoorSpot(32.52f, 1.05f, 27.39f, 0.04f, 2.1f, 0.98f, false),
            new DoorSpot(46.02f, 1.05f, 27.39f, 0.04f, 2.1f, 0.98f, false),
            new DoorSpot(21.62f, 1.05f, 10.41f, 0.04f, 2.1f, 0.98f, false),
            new DoorSpot(35.02f, 1.05f, 10.41f, 0.04f, 2.1f, 0.98f, false),
            new DoorSpot(46.52f, 1.05f, 10.41f, 0.04f, 2.1f, 0.98f, false),
            new DoorSpot(54.0f, 3.55f, 33.425f, 1.6f, 2.1f, 0.04f, true),     // HallNorth
            new DoorSpot(59.15f, 1.05f, 38.5f, 0.04f, 2.1f, 1.0f, true),
            new DoorSpot(15.46f, 3.05f, 5.3f, 0.04f, 2.1f, 1.0f, true),       // Passage
            new DoorSpot(0.22f, 3.05f, 24.39f, 0.04f, 2.1f, 1.18f, false),    // Alfa
            new DoorSpot(13.32f, 3.05f, 24.39f, 0.04f, 2.1f, 1.18f, false),
            new DoorSpot(1.22f, 3.05f, 16.54f, 0.04f, 2.1f, 0.98f, false),    // Beta
            new DoorSpot(9.62f, 3.05f, 15.26f, 0.04f, 2.1f, 0.98f, false),
            new DoorSpot(-0.15f, -2.95f, 9.5f, 0.04f, 2.1f, 1.0f, true),      // ParkingL1
            new DoorSpot(-0.15f, -6.45f, 9.5f, 0.04f, 2.1f, 1.0f, true),      // ParkingL2
            new DoorSpot(7.15f, 19.05f, 12.0f, 0.04f, 2.1f, 1.0f, true),      // Cafeteria
            new DoorSpot(7.15f, 19.05f, 19.8f, 0.04f, 2.1f, 1.0f, true),
        };

        class Door { public Transform hinge; public float sign, open, w, h; public AudioSource audio; public bool busy; }

        readonly List<Door> doors = new List<Door>();
        FeriBlackout blackout;
        float next = -1f;
        static AudioClip slam;

        void Start()
        {
            blackout = GetComponent<FeriBlackout>();
            if (slam == null) slam = MakeSlam();
            Material wood = DoorMaterial("door"), locked = DoorMaterial("door_locked");
            Material black = wood != null ? new Material(wood) : null;
            if (black != null)
            {
                black.name = "Doorway (runtime)";
                if (black.HasProperty("_MainTex")) black.SetTexture("_MainTex", null);
                if (black.HasProperty("_Color")) black.SetColor("_Color", Color.black);
                if (black.HasProperty("_EmissionColor")) black.SetColor("_EmissionColor", Color.black);
            }
            var root = new GameObject("HauntedDoors").transform;
            foreach (var s in Spots) doors.Add(Build(s, root, s.locked && locked != null ? locked : wood, black));
        }

        void Update()
        {
            if (doors.Count == 0) return;
            if (onlyAfterBlackout && blackout != null && !blackout.IsOut) return;
            if (next < 0f) next = Time.time + Random.Range(0.5f, 1.5f);   // the first one soon after the lights die
            if (Time.time < next) return;
            next = Time.time + Random.Range(interval.x, interval.y);

            var near = new List<Door>();
            var free = new List<Door>();
            foreach (var d in doors)
            {
                if (d.busy) continue;
                free.Add(d);
                if (Vector3.Distance(d.hinge.position, transform.position) < nearRadius) near.Add(d);
            }
            var pick = near.Count > 0 && Random.value < 0.8f ? near : free;
            if (pick.Count > 0) StartCoroutine(Haunt(pick[Random.Range(0, pick.Count)]));
        }

        IEnumerator Haunt(Door d)
        {
            d.busy = true;
            float t = 0f, dur = Random.Range(0.18f, 0.32f);
            while (t < dur)   // slams: accelerates into the frame
            {
                t += Time.deltaTime;
                float k = t / dur;
                d.hinge.localRotation = Quaternion.Euler(0f, d.open * (1f - k * k), 0f);
                yield return null;
            }
            d.hinge.localRotation = Quaternion.identity;
            d.audio.pitch = Random.Range(0.9f, 1.1f);
            d.audio.PlayOneShot(slam, volume);
            // a little bounce off the frame
            for (t = 0f; t < 0.15f; t += Time.deltaTime)
            {
                d.hinge.localRotation = Quaternion.Euler(0f, Mathf.Sin(t / 0.15f * Mathf.PI) * 4f * d.sign, 0f);
                yield return null;
            }
            d.hinge.localRotation = Quaternion.identity;

            yield return new WaitForSeconds(Random.Range(closedTime.x, closedTime.y));

            // then it slowly creaks back open, stuttering on the hinge
            d.open = ClearAngle(d, Random.Range(55f, 95f));
            t = 0f; dur = Random.Range(2.5f, 4.5f);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                float jitter = Mathf.Sin(t * 23f) * 1.5f * (1f - k);
                d.hinge.localRotation = Quaternion.Euler(0f, d.open * k + jitter * d.sign, 0f);
                yield return null;
            }
            d.hinge.localRotation = Quaternion.Euler(0f, d.open, 0f);
            d.busy = false;
        }

        Door Build(DoorSpot s, Transform root, Material leafMat, Material blackMat)
        {
            bool alongZ = s.s.x < s.s.z;                       // thin in x: the door faces +-x and is s.z wide
            Vector3 n = alongZ ? Vector3.right : Vector3.forward;
            float w = alongZ ? s.s.z : s.s.x, h = s.s.y;
            n *= OpenSide(s.c, n, h);                           // the side you walk on
            Quaternion rot = Quaternion.LookRotation(-n, Vector3.up);   // local +Z goes into the wall
            Vector3 right = rot * Vector3.right;

            Transform frame = new GameObject("Door").transform;
            frame.SetParent(root, false);
            frame.SetPositionAndRotation(s.c, rot);

            // black void over the painted door, only seen when the leaf swings away
            var back = Box(frame, new Vector3(0f, 0f, -0.025f), new Vector3(w + 0.02f, h + 0.02f, 0.01f), blackMat);
            back.name = "Doorway";

            var d = new Door { sign = Random.value < 0.5f ? 1f : -1f };
            // hinge on the left or the right edge; the leaf swings out towards you
            d.hinge = new GameObject("Hinge").transform;
            d.hinge.SetParent(frame, false);
            d.hinge.localPosition = new Vector3(-d.sign * w * 0.5f, 0f, -0.055f);
            Box(d.hinge, new Vector3(d.sign * w * 0.5f, 0f, 0f), new Vector3(w - 0.02f, h - 0.01f, 0.04f), leafMat).name = "Leaf";
            d.w = w; d.h = h;
            d.open = ClearAngle(d, Random.Range(55f, 95f));   // doors stand open until one slams
            d.hinge.localRotation = Quaternion.Euler(0f, d.open, 0f);

            d.audio = frame.gameObject.AddComponent<AudioSource>();
            d.audio.spatialBlend = 1f;
            d.audio.rolloffMode = AudioRolloffMode.Linear;
            d.audio.minDistance = 2f;
            d.audio.maxDistance = 30f;
            d.audio.playOnAwake = false;
            return d;
        }

        // The widest angle up to `want` (degrees, signed for this door) at which the leaf hits nothing: no swinging
        // through walls, desks or the frame. The swept boxes skip the bit by the hinge, where it touches the wall.
        float ClearAngle(Door d, float want)
        {
            Transform frame = d.hinge.parent;
            Vector3 hingePos = d.hinge.position;
            for (float a = want; a > 10f; a -= 5f)
            {
                bool hit = false;
                for (float step = Mathf.Min(a, 15f); step <= a + 0.01f && !hit; step = Mathf.Min(step + 15f, a + 0.02f))
                {
                    Quaternion r = frame.rotation * Quaternion.Euler(0f, step * d.sign, 0f);
                    Vector3 along = r * Vector3.right * d.sign;
                    Vector3 centre = hingePos + along * (d.w * 0.5f + 0.06f);
                    var half = new Vector3(Mathf.Max(0.05f, d.w * 0.5f - 0.12f), d.h * 0.5f - 0.12f, 0.03f);
                    hit = Physics.CheckBox(centre, half, r, ~0, QueryTriggerInteraction.Ignore);
                    if (step >= a) break;
                }
                if (!hit) return a * d.sign;
            }
            return 12f * d.sign;   // no room at all: barely ajar
        }

        // +1 or -1: which side of the wall has floor (NavMesh) in front of the door
        static float OpenSide(Vector3 c, Vector3 n, float h)
        {
            Vector3 feet = c - Vector3.up * (h * 0.5f);
            float a = NavMesh.SamplePosition(feet + n * 0.7f, out NavMeshHit ha, 0.6f, NavMesh.AllAreas) ? ha.distance : 99f;
            float b = NavMesh.SamplePosition(feet - n * 0.7f, out NavMeshHit hb, 0.6f, NavMesh.AllAreas) ? hb.distance : 99f;
            if (a < 99f || b < 99f) return a <= b ? 1f : -1f;
            // no NavMesh there: the side that isn't straight into a wall
            bool wallA = Physics.Raycast(c + n * 0.06f, n, 0.5f, ~0, QueryTriggerInteraction.Ignore);
            return wallA ? -1f : 1f;
        }

        static Transform Box(Transform parent, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(go.GetComponent<Collider>());   // now, so ClearAngle doesn't hit our own boxes
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            t.localScale = size;
            var r = go.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return t;
        }

        // the map's own door material, lit by the dynamic lights instead of baked vertex colours (a cube has none)
        static Material DoorMaterial(string name)
        {
            var go = GameObject.Find(name);
            var r = go != null ? go.GetComponent<Renderer>() : null;
            if (r == null || r.sharedMaterial == null) return null;
            var m = new Material(r.sharedMaterial) { name = name + " leaf (runtime)" };
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 0f);
            // the PS1 affine texture warp smears a big two-triangle leaf as it swings: keep the texture straight
            if (m.HasProperty("_Affine")) m.SetFloat("_Affine", 0f);
            return m;
        }

        // ------------------------------------------------------------------ sounds
        const int Rate = 22050;

        // a low thump, a crack of noise and a short rattle
        static AudioClip MakeSlam()
        {
            int n = (int)(Rate * 0.7f);
            var data = new float[n];
            var rnd = new System.Random(5);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)rnd.NextDouble() * 2f - 1f;
                lp += (noise - lp) * 0.25f;   // darker noise
                float thump = Mathf.Sin(t * 2f * Mathf.PI * (62f - 20f * t)) * Mathf.Exp(-t * 9f);
                float crack = lp * Mathf.Exp(-t * 35f);
                float rattle = lp * Mathf.Exp(-t * 10f) * (Mathf.Sin(t * 2f * Mathf.PI * 28f) > 0.3f ? 0.35f : 0f);
                data[i] = Mathf.Clamp(thump * 0.9f + crack * 1.2f + rattle, -1f, 1f);
            }
            var clip = AudioClip.Create("DoorSlam", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
