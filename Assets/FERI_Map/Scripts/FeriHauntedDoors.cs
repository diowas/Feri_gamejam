// The classroom doors (Hall, Alfa, Beta) stand open. Once the lights go out (FeriBlackout), every 12-30 s a random
// one slams shut, stays shut about a minute and then slowly creaks open again (only the slam makes a sound).
// The map bakes those doors into its merged meshes as leaves standing wide open (90 degrees, against the wall inside
// the room), so this hides the baked leaves and puts a hinged leaf in exactly the same place, which can swing into
// the doorway between the two steel jambs. The locked doors are painted on the walls and stay as they are.
// A shut door blocks the player (a collider that's only on while it's fully shut, so a swinging leaf never shoves you):
// the only way through is NOCLIP, and standing at one shows a hint for it top left (HintAlpha, drawn by FeriBlackout).
// Monsters walk the NavMesh and ignore it. The slam is synthesised.
// FeriAbilities adds this to the player.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

        // From the "door" boxes in FERI_Map/Data/feri_layout.json: the hinge (bottom-centre height of the leaf, at the
        // wall end of the open leaf), which way the open leaf points (+-z, into the room), and its width. Every doorway
        // runs +x from the hinge, to the second steel jamb.
        struct DoorSpot
        {
            public Vector3 hinge; public float openZ, width;
            public DoorSpot(float x, float y, float z, float oz, float w) { hinge = new Vector3(x, y, z); openZ = oz; width = w; }
        }
        static readonly DoorSpot[] Spots =
        {
            new DoorSpot(22.02f, 1.05f, 26.90f, 1f, 0.98f),    // Hall, south side
            new DoorSpot(32.52f, 1.05f, 26.90f, 1f, 0.98f),
            new DoorSpot(46.02f, 1.05f, 26.90f, 1f, 0.98f),
            new DoorSpot(21.62f, 1.05f, 10.90f, -1f, 0.98f),   // Hall, north side
            new DoorSpot(35.02f, 1.05f, 10.90f, -1f, 0.98f),
            new DoorSpot(46.52f, 1.05f, 10.90f, -1f, 0.98f),
            new DoorSpot(0.22f, 3.05f, 23.80f, 1f, 1.18f),     // Alfa
            new DoorSpot(13.32f, 3.05f, 23.80f, 1f, 1.18f),
            new DoorSpot(1.22f, 3.05f, 16.05f, 1f, 0.98f),     // Beta
            new DoorSpot(9.62f, 3.05f, 15.75f, -1f, 0.98f),
        };
        const float LeafHeight = 2.1f, LeafThickness = 0.04f;

        class Door { public Transform hinge; public float open; public AudioSource audio; public bool busy, shut; public Collider block; }

        readonly List<Door> doors = new List<Door>();
        FeriBlackout blackout;
        float next = -1f;
        static AudioClip slam;

        [Tooltip("Show the NOCLIP hint when the player is this close to a shut door (metres).")]
        public float hintDistance = 2.2f;
        [Tooltip("How long the hint stays up each time you walk up to a shut door (seconds).")]
        public float hintTime = 5f;
        public const string Hint = "The door won't open";
        public const string HintSub = "Hold Tab and turn on NOCLIP (3)";
        /// <summary>0..1, how much of the NOCLIP hint to show top left (FeriBlackout draws it under the missions).</summary>
        public static float HintAlpha { get; private set; }
        bool wasNear;
        float nearSince;
        FeriPlayer player;

        void Start()
        {
            blackout = GetComponent<FeriBlackout>();
            player = GetComponent<FeriPlayer>();
            HintAlpha = 0f;
            if (slam == null) slam = MakeSlam();
            Material wood = DoorMaterial("door");
            // hide the map's own (baked, wide open) leaves: ours stand in the same spot
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.gameObject.name == "door") r.enabled = false;
            var root = new GameObject("HauntedDoors").transform;
            foreach (var s in Spots) doors.Add(Build(s, root, wood));
        }

        void Update()
        {
            UpdateHint();
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
                d.hinge.localRotation = Quaternion.Euler(0f, Mathf.Sin(t / 0.15f * Mathf.PI) * 4f * Mathf.Sign(d.open), 0f);
                yield return null;
            }
            d.hinge.localRotation = Quaternion.identity;
            SetShut(d, true);

            yield return new WaitForSeconds(Random.Range(closedTime.x, closedTime.y));
            SetShut(d, false);

            // then it slowly creaks back open, stuttering on the hinge
            t = 0f; dur = Random.Range(2.5f, 4.5f);
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / dur);
                float jitter = Mathf.Sin(t * 23f) * 1.5f * (1f - k);
                d.hinge.localRotation = Quaternion.Euler(0f, d.open * k + jitter * Mathf.Sign(d.open), 0f);
                yield return null;
            }
            d.hinge.localRotation = Quaternion.Euler(0f, d.open, 0f);
            d.busy = false;
        }

        Door Build(DoorSpot s, Transform root, Material leafMat)
        {
            // hinge frame: local +x along the closed door (across the doorway, +x in the world), so angle 0 = shut
            Vector3 closedDir = Vector3.right, openDir = Vector3.forward * s.openZ;
            Transform frame = new GameObject("Door").transform;
            frame.SetParent(root, false);
            frame.SetPositionAndRotation(s.hinge, Quaternion.LookRotation(Vector3.Cross(closedDir, Vector3.up), Vector3.up));

            var d = new Door { open = Vector3.SignedAngle(closedDir, openDir, Vector3.up) };   // the baked leaf's angle
            d.hinge = new GameObject("Hinge").transform;
            d.hinge.SetParent(frame, false);
            Box(d.hinge, new Vector3(s.width * 0.5f, 0f, 0f), new Vector3(s.width, LeafHeight, LeafThickness), leafMat).name = "Leaf";
            d.hinge.localRotation = Quaternion.Euler(0f, d.open, 0f);   // stands open, like the map had it

            // the wall it makes while shut: across the doorway, a little thicker than the leaf
            var wall = new GameObject("Shut").transform;
            wall.SetParent(frame, false);
            wall.localPosition = new Vector3(s.width * 0.5f, 0f, 0f);
            var bc = wall.gameObject.AddComponent<BoxCollider>();
            bc.size = new Vector3(s.width, LeafHeight, 0.12f);
            bc.enabled = false;
            d.block = bc;

            d.audio = frame.gameObject.AddComponent<AudioSource>();
            d.audio.spatialBlend = 1f;
            d.audio.rolloffMode = AudioRolloffMode.Linear;
            d.audio.minDistance = 2f;
            d.audio.maxDistance = 30f;
            d.audio.playOnAwake = false;
            return d;
        }

        static void SetShut(Door d, bool shut)
        {
            d.shut = shut;
            if (d.block != null) d.block.enabled = shut;
        }

        // the NOCLIP hint: comes up when you walk up to a shut door (with NOCLIP off) and fades after a few seconds
        void UpdateHint()
        {
            bool near = false;
            if (player == null || !player.noclip)
                foreach (var d in doors)
                    if (d.shut && d.block != null && Vector3.Distance(d.block.ClosestPoint(transform.position), transform.position) < hintDistance)
                    { near = true; break; }
            if (near && !wasNear) nearSince = Time.time;
            wasNear = near;
            float target = near && Time.time - nearSince < hintTime ? 1f : 0f;
            HintAlpha = Mathf.MoveTowards(HintAlpha, target, Time.deltaTime * 3f);
        }

        static Transform Box(Transform parent, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
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
