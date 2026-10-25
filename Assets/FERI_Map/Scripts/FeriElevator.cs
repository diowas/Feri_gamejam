using System.Collections.Generic;
using UnityEngine;

namespace FeriMap
{

    public class FeriElevator : MonoBehaviour
    {
        [System.Serializable]
        public class Floor
        {
            public string label;
            public float y;
            public bool enabled;
            public Transform landingDoorA, landingDoorB;
            public Renderer landingDisplay;
            [Tooltip("UV pravokotnik stevilke v atlasu (x0, y0, x1, y1).")]
            public Vector4 ledRect;
            public FeriElevatorButton cabinButton;
        }

        public enum State { Idle, Closing, Moving, Opening }

        public List<Floor> floors = new List<Floor>();
        public Transform cabin;
        public Transform cabinDoorA, cabinDoorB;
        public Renderer cabinDisplay;
        public BoxCollider cabinVolume;
        [Tooltip("Smer odpiranja vrat (lokalna os) za vrata A; vrata B gredo v nasprotno smer.")]
        public Vector3 doorSlideAxis = Vector3.forward;
        public float doorTravel = 0.48f;
        public float doorSpeed = 0.55f;
        public float maxSpeed = 2.4f;
        public float acceleration = 1.6f;
        public float autoCloseAfter = 0f;
        public int startFloor;
        public AudioSource audioSource;

        public State state = State.Idle;
        [Tooltip("Brez elektrike: gumbi ne delajo, vrata in kabina stojijo (FeriEscape).")]
        public bool powered = true;
        /// <summary>The cabin stopped at a floor (index into floors).</summary>
        public event System.Action<int> Arrived;
        public float DoorOpenAmount { get { return doorT; } }
        public float DoorGoal { get { return doorGoal; } }
        public int currentFloor;
        public int targetFloor = -1;

        float doorT = 1f;
        float doorGoal = 1f;
        float velocity;
        float idleTimer;
        Vector3 cabDoorAClosed, cabDoorBClosed;
        readonly List<Vector3> landAClosed = new List<Vector3>();
        readonly List<Vector3> landBClosed = new List<Vector3>();
        readonly Queue<int> queue = new Queue<int>();
        AudioClip ding, beep, hum;
        AudioSource humSource;
        MaterialPropertyBlock mpb;
        int shownFloor = -1;
        static readonly int StId = Shader.PropertyToID("_MainTex_ST");

        bool started;

        /// <summary>Adds a floor at runtime (e.g. the roof); returns its index.</summary>
        public int AddFloor(Floor f)
        {
            floors.Add(f);
            if (started)
            {
                landAClosed.Add(f.landingDoorA != null ? f.landingDoorA.localPosition : Vector3.zero);
                landBClosed.Add(f.landingDoorB != null ? f.landingDoorB.localPosition : Vector3.zero);
            }
            return floors.Count - 1;
        }

        /// <summary>Power off: cabin light out, buttons dead, everything stops where it is.</summary>
        public void SetPowered(bool on)
        {
            powered = on;
            if (cabin != null)
                foreach (var l in cabin.GetComponentsInChildren<PS1Light>(true)) l.enabled = on;
            if (!on && humSource != null) humSource.Stop();
            if (on && state == State.Moving && humSource != null) humSource.Play();
        }

        void Start()
        {
            started = true;
            cabDoorAClosed = cabinDoorA != null ? cabinDoorA.localPosition : Vector3.zero;
            cabDoorBClosed = cabinDoorB != null ? cabinDoorB.localPosition : Vector3.zero;
            foreach (var f in floors)
            {
                landAClosed.Add(f.landingDoorA != null ? f.landingDoorA.localPosition : Vector3.zero);
                landBClosed.Add(f.landingDoorB != null ? f.landingDoorB.localPosition : Vector3.zero);
            }
            currentFloor = Mathf.Clamp(startFloor, 0, floors.Count - 1);
            Vector3 p = cabin.position; p.y = floors[currentFloor].y; cabin.position = p;
            doorT = doorGoal = 1f;
            MakeSounds();
            ApplyDoors();
            UpdateDisplays(currentFloor);
        }

        public void Press(FeriElevatorButton b)
        {
            if (!powered) return;
            int f = b.floorIndex;
            if (f < 0 || f >= floors.Count || !floors[f].enabled)
            {
                b.Blink(false);
                Play(beep, 0.6f);
                return;
            }
            b.Blink(true);
            Play(beep, 0.25f);
            if (f == currentFloor && state != State.Moving)
            {
                doorGoal = 1f;
                if (state == State.Closing) state = State.Opening;
                idleTimer = 0f;
                return;
            }
            if (targetFloor == f || queue.Contains(f)) return;
            if (targetFloor < 0) { targetFloor = f; SetHeld(f, true); }
            else { queue.Enqueue(f); SetHeld(f, true); }
        }

        void SetHeld(int f, bool on)
        {
            if (f >= 0 && f < floors.Count && floors[f].cabinButton != null) floors[f].cabinButton.held = on;
        }

        void Update()
        {
            if (!powered) return;
            float dt = Time.deltaTime;
            switch (state)
            {
                case State.Idle:
                    idleTimer += dt;
                    if (targetFloor < 0 && queue.Count > 0) targetFloor = queue.Dequeue();
                    if (targetFloor >= 0 && targetFloor != currentFloor) { doorGoal = 0f; state = State.Closing; }
                    else if (targetFloor == currentFloor) { SetHeld(targetFloor, false); targetFloor = -1; }
                    else if (autoCloseAfter > 0 && idleTimer > autoCloseAfter) doorGoal = 0f;
                    break;
                case State.Closing:
                    if (doorT <= 0f) { state = State.Moving; velocity = 0f; if (humSource != null) humSource.Play(); }
                    break;
                case State.Moving:
                    MoveCabin(dt);
                    break;
                case State.Opening:
                    if (doorT >= 1f) { state = State.Idle; idleTimer = 0f; }
                    break;
            }
            doorT = Mathf.MoveTowards(doorT, doorGoal, dt * doorSpeed / Mathf.Max(0.01f, doorTravel));
            ApplyDoors();
        }

        void MoveCabin(float dt)
        {
            float goal = floors[targetFloor].y;
            float y = cabin.position.y;
            float dist = goal - y;
            float dir = Mathf.Sign(dist);
            float stopDist = velocity * velocity / (2f * acceleration);
            if (Mathf.Abs(dist) <= stopDist + 0.01f) velocity = Mathf.Max(0.15f, velocity - acceleration * dt);
            else velocity = Mathf.Min(maxSpeed, velocity + acceleration * dt);
            float step = dir * velocity * dt;
            if (Mathf.Abs(step) >= Mathf.Abs(dist)) step = dist;
            Vector3 delta = new Vector3(0, step, 0);
            cabin.position += delta;
            CarryPassengers(delta);
            if (humSource != null) humSource.pitch = 0.8f + 0.3f * velocity / maxSpeed;

            int nearest = currentFloor; float best = float.MaxValue;
            for (int i = 0; i < floors.Count; i++)
            {
                float d = Mathf.Abs(floors[i].y - cabin.position.y);
                if (d < best) { best = d; nearest = i; }
            }
            UpdateDisplays(nearest);

            if (Mathf.Abs(goal - cabin.position.y) < 0.0005f)
            {
                SetHeld(targetFloor, false);
                currentFloor = targetFloor;
                targetFloor = -1;
                state = State.Opening;
                doorGoal = 1f;
                if (humSource != null) humSource.Stop();
                Play(ding, 0.8f);
                UpdateDisplays(currentFloor);
                Arrived?.Invoke(currentFloor);
            }
        }

        void CarryPassengers(Vector3 delta)
        {
            if (cabinVolume == null) return;
            Vector3 hs = cabinVolume.size * 0.5f;
            Vector3 wc = cabinVolume.transform.TransformPoint(cabinVolume.center);
            Vector3 ax2 = cabinVolume.transform.TransformVector(new Vector3(hs.x, 0, 0));
            Vector3 az2 = cabinVolume.transform.TransformVector(new Vector3(0, 0, hs.z));
            Vector3 half = new Vector3(Mathf.Abs(ax2.x) + Mathf.Abs(az2.x), hs.y, Mathf.Abs(ax2.z) + Mathf.Abs(az2.z));
            Bounds b = new Bounds(wc, half * 2f);
            b.Expand(new Vector3(0, 0.6f, 0));
            foreach (var p in FeriPlayer.All)
            {
                Vector3 pos = p.transform.position + Vector3.up * 0.3f;
                if (b.Contains(pos + delta) || b.Contains(pos)) p.ShiftBy(delta);
            }
        }

        void ApplyDoors()
        {
            float s = Mathf.SmoothStep(0f, 1f, doorT) * doorTravel;
            Vector3 ax = doorSlideAxis.normalized;
            if (cabinDoorA != null) cabinDoorA.localPosition = cabDoorAClosed - ax * s;
            if (cabinDoorB != null) cabinDoorB.localPosition = cabDoorBClosed + ax * s;
            for (int i = 0; i < floors.Count; i++)
            {
                float k = (i == currentFloor && state != State.Moving) ? s : 0f;
                if (floors[i].landingDoorA != null) floors[i].landingDoorA.localPosition = landAClosed[i] - ax * k;
                if (floors[i].landingDoorB != null) floors[i].landingDoorB.localPosition = landBClosed[i] + ax * k;
            }
        }

        void UpdateDisplays(int floorIndex)
        {
            if (floorIndex == shownFloor) return;
            shownFloor = floorIndex;
            Vector4 r = floors[floorIndex].ledRect;
            Vector4 st = new Vector4(r.z - r.x, r.w - r.y, r.x, r.y);
            if (mpb == null) mpb = new MaterialPropertyBlock();
            SetSt(cabinDisplay, st);
            foreach (var f in floors) SetSt(f.landingDisplay, st);
        }

        void SetSt(Renderer r, Vector4 st)
        {
            if (r == null) return;
            r.GetPropertyBlock(mpb);
            mpb.SetVector(StId, st);
            r.SetPropertyBlock(mpb);
        }

        void Play(AudioClip c, float vol)
        {
            if (audioSource != null && c != null) audioSource.PlayOneShot(c, vol);
        }

        void MakeSounds()
        {
            if (audioSource == null) return;
            const int sr = 22050;
            ding = Tone("ding", sr, 1.2f, (t) =>
                (Mathf.Sin(2 * Mathf.PI * 880f * t) * Mathf.Exp(-t * 4f) * (t < 0.35f ? 1f : 0f)
               + Mathf.Sin(2 * Mathf.PI * 698f * t) * Mathf.Exp(-(t - 0.35f) * 3f) * (t >= 0.35f ? 1f : 0f)) * 0.5f);
            beep = Tone("beep", sr, 0.12f, (t) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 1200f * t)) * 0.15f);
            System.Random r = new System.Random(7);
            float lp = 0f;
            hum = Tone("hum", sr, 2f, (t) =>
            {
                lp = lp * 0.97f + ((float)r.NextDouble() * 2f - 1f) * 0.03f;
                return Mathf.Sin(2 * Mathf.PI * 55f * t) * 0.18f + lp * 1.5f;
            });
            humSource = audioSource.gameObject.AddComponent<AudioSource>();
            humSource.clip = hum; humSource.loop = true; humSource.volume = 0.35f;
            humSource.spatialBlend = audioSource.spatialBlend; humSource.playOnAwake = false;
        }

        static AudioClip Tone(string name, int sr, float seconds, System.Func<float, float> f)
        {
            int n = Mathf.RoundToInt(sr * seconds);
            float[] data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f(i / (float)sr);
            AudioClip c = AudioClip.Create(name, n, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }
    }
}
