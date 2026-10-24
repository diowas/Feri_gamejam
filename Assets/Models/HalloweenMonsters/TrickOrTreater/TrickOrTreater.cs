// TRICK-OR-TREATER - the main killer.   "it's not a bug, it's a feature."
// A 3.3 m thing on huge legs that bend the wrong way, under a pumpkin-orange bedsheet ghost costume,
// with ribs, bone spikes, knives, a wooden stake and an extra hand sticking out of it. There is no body under the sheet,
// only the missing texture. It walks the halls with a kitchen knife and a blood-soaked trick-or-treat
// pillowcase. Its animation runs at 12 fps while walking and drops to 6 fps when it chases you, so its
// limbs snap between poses, and its bones sometimes come apart for a frame. Not a bug.
//
// The model is SEGMENTED (rigid parts, like PS1 games). This script builds the skeleton at runtime and
// animates it in code, so no animation files or rigging are needed.
//
// Setup: drag TrickOrTreater.obj into the scene, add this script to the ROOT object (a NavMeshAgent is
// added automatically), bake a NavMesh for your level, press Play. See README.txt.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NavMeshAgent))]
public class TrickOrTreater : MonoBehaviour
{
    public enum State { Patrol, Investigate, Spotting, Chase, Search, Kill }

    [Header("Player")]
    public Camera playerCamera;                      // empty -> Camera.main
    public Transform playerRoot;                     // empty -> the camera's root object
    public MonoBehaviour[] disableOnKill = new MonoBehaviour[0];   // your player movement / mouse-look scripts

    [Header("Patrol")]
    public Transform[] waypoints = new Transform[0];   // empty -> wanders to random points on the NavMesh
    public float wanderRadius = 25f;
    public float patrolSpeed = 1.3f;
    public float waypointPause = 2.5f;

    [Header("Senses")]
    public float viewDistance = 18f;
    [Range(10, 360)] public float viewAngle = 110f;
    public float closeSenseDistance = 2.5f;          // it feels you this close, even behind it
    public float timeToNotice = 0.6f;                // at mid range; almost instant up close
    public float hearRadius = 11f;
    public float noisySpeed = 3.5f;                  // a player moving faster than this (m/s) can be heard
    public LayerMask sightBlockers = ~0;

    [Header("Chase")]
    public float chaseSpeed = 4.8f;                  // keep it a little below your player's sprint speed
    public float spotFreezeTime = 0.9f;              // the pause after it sees you, head snapped sideways
    public float loseSightTime = 4f;
    public float searchTime = 7f;
    public float killDistance = 2.0f;               // its arms are very long

    [Header("Animation")]
    public float walkAnimFps = 12f;
    public float chaseAnimFps = 6f;
    public float strideLength = 1.85f;
    [Range(0, 1)] public float boneDesyncChance = 0.12f;

    [Header("Audio (optional)")]
    public AudioSource voice;                        // looping 3D whisper, e.g. "trick... or treat..."
    public AudioSource sfx;                          // 3D source for one-shots
    public AudioClip[] footstepClips;
    public AudioClip spotSting, killScream;

    [Header("Eyes")]
    public Color eyeColor = new Color(1f, 0.55f, 0.12f);
    public float calmEyeGlow = 1.2f, huntingEyeGlow = 4f;

    [Header("Events")]
    public UnityEvent onSpotted = new UnityEvent(), onLostPlayer = new UnityEvent(), onKilledPlayer = new UnityEvent();   // nothing on onKilledPlayer -> reloads the scene

    public State CurrentState => state;

    // ---- rig: joint names, parents and pivot positions (model space, metres) - must match the .obj
    static readonly string[] JN = { "hips", "torso", "head", "arm_upper_L", "arm_lower_L", "hand_L", "arm_upper_R", "arm_lower_R",
                                    "hand_R", "sack", "leg_upper_L", "leg_lower_L", "foot_L", "leg_upper_R", "leg_lower_R", "foot_R" };
    static readonly int[] JP = { -1, 0, 1, 1, 3, 4, 1, 6, 7, 5, 0, 10, 11, 0, 13, 14 };
    static readonly Vector3[] JPOS = {
        new Vector3(0.000f, 1.782f, 0.000f),
        new Vector3(0.000f, 1.782f, 0.000f),
        new Vector3(0.000f, 2.783f, 0.552f),
        new Vector3(0.230f, 2.737f, 0.437f),
        new Vector3(0.414f, 2.070f, 0.483f),
        new Vector3(0.460f, 1.438f, 0.598f),
        new Vector3(-0.230f, 2.737f, 0.437f),
        new Vector3(-0.483f, 1.897f, 0.460f),
        new Vector3(-0.552f, 0.632f, 0.575f),
        new Vector3(0.460f, 1.260f, 0.646f),
        new Vector3(0.149f, 1.782f, 0.023f),
        new Vector3(0.172f, 0.977f, -0.149f),
        new Vector3(0.149f, 0.161f, 0.023f),
        new Vector3(-0.149f, 1.782f, 0.023f),
        new Vector3(-0.172f, 0.977f, -0.149f),
        new Vector3(-0.149f, 0.161f, 0.023f) };
    static readonly Vector3 HEAD_CENTER = new Vector3(0.000f, 2.415f, 1.242f);   // centre of the hooded head
    const int HIPS = 0, TORSO = 1, HEAD = 2, AUL = 3, ALL = 4, HL = 5, AUR = 6, ALR = 7, HR = 8, SACK = 9,
              LUL = 10, LLL = 11, FL = 12, LUR = 13, LLR = 14, FR = 15;

    NavMeshAgent agent;
    State state = State.Patrol;
    readonly Transform[] joints = new Transform[16];
    readonly Vector3[] restLocal = new Vector3[16];
    float mirror = 1f;
    Renderer headRenderer;
    MaterialPropertyBlock mpb;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    Vector3 lastKnown, lastPlayerPos, playerVelocity;
    float notice, stateTimer, lastSeenTime = -999f, pauseTimer, repathTimer;
    int waypointIndex;
    float phase, lastSin, animTimer, gait;
    float headRoll = 12f, headRollTarget = 12f, nextTwitch;
    Quaternion headLook = Quaternion.identity;
    float glow;
    Vector2 sackAngle, sackVel; Vector3 sackLastPos, sackLastVel;
    bool killing; float startFov;

    void Reset()
    {
        var a = GetComponent<NavMeshAgent>();
        if (!a) return;
        a.radius = 0.55f; a.height = 3.4f; a.speed = patrolSpeed; a.angularSpeed = 260f; a.acceleration = 14f; a.stoppingDistance = 0.3f;
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!playerCamera) playerCamera = Camera.main;
        if (!playerRoot && playerCamera) playerRoot = playerCamera.transform.root;
        mpb = new MaterialPropertyBlock();
        BuildRig();
        if (playerCamera) lastPlayerPos = playerCamera.transform.position;
        sackLastPos = joints[SACK].position;
        agent.speed = patrolSpeed;
    }

    void Start() { PickNextPatrolPoint(); }

    // ================================================================ rig
    void BuildRig()
    {
        var parts = new Dictionary<string, Transform>();
        foreach (var r in GetComponentsInChildren<Renderer>(true)) parts[r.name.Trim().ToLowerInvariant()] = r.transform;
        // Unity mirrors .obj files on X when importing them - detect it so the joints line up either way
        if (parts.TryGetValue("arm_upper_l", out var probe))
            mirror = transform.InverseTransformPoint(probe.GetComponent<Renderer>().bounds.center).x < 0 ? -1f : 1f;
        for (int i = 0; i < JN.Length; i++)
        {
            var j = new GameObject("J_" + JN[i]).transform;
            j.SetParent(JP[i] < 0 ? transform : joints[JP[i]], false);
            j.localPosition = M(JPOS[i]) - (JP[i] < 0 ? Vector3.zero : M(JPOS[JP[i]]));
            j.localRotation = Quaternion.identity;
            joints[i] = j; restLocal[i] = j.localPosition;
        }
        for (int i = 0; i < JN.Length; i++)
        {
            if (parts.TryGetValue(JN[i].ToLowerInvariant(), out var part)) part.SetParent(joints[i], true);
            else if (i != HIPS) Debug.LogWarning($"TrickOrTreater: part '{JN[i]}' not found under '{name}'. Is the script on the model's root?");
        }
        if (parts.TryGetValue("head", out var h)) headRenderer = h.GetComponent<Renderer>();
    }
    Vector3 M(Vector3 v) => new Vector3(v.x * mirror, v.y, v.z);
    Vector3 HeadCenter() => joints[HEAD].TransformPoint(M(HEAD_CENTER - JPOS[HEAD]));
    // angles are authored in model space; mirroring flips the Y and Z rotations
    void Rot(int i, float x, float y = 0f, float z = 0f) => joints[i].localRotation = Quaternion.Euler(x, y * mirror, z * mirror);

    // ================================================================ AI
    void Update()
    {
        float dt = Time.deltaTime;
        if (!playerCamera || killing || !agent.isOnNavMesh) { Animate(dt); return; }
        Vector3 p = playerCamera.transform.position;
        playerVelocity = Vector3.Lerp(playerVelocity, (p - lastPlayerPos) / Mathf.Max(dt, 1e-4f), 10f * dt);
        lastPlayerPos = p;
        bool sees = CanSee(p, out float dist);
        if (sees) { lastKnown = p; lastSeenTime = Time.time; }
        stateTimer += dt;

        switch (state)
        {
            case State.Patrol:
            case State.Investigate:
            case State.Search:
                Notice(sees, dist, dt);
                if (state == State.Spotting) break;
                if (playerVelocity.magnitude > noisySpeed && dist < hearRadius) Investigate(p);   // it hears you running
                UpdateWander(dt);
                break;

            case State.Spotting:
                agent.isStopped = true;
                FaceTowards(p, 10f * dt);
                if (stateTimer > spotFreezeTime) SetState(State.Chase);
                break;

            case State.Chase:
                agent.isStopped = false; agent.speed = chaseSpeed;
                if (!sees && Time.time - lastSeenTime < 1.5f) lastKnown = p;     // it 'knows' for a moment around corners
                repathTimer -= dt;
                if (repathTimer <= 0f) { agent.SetDestination(Ground(lastKnown)); repathTimer = 0.2f; }
                if (sees && FlatDistance(p) < killDistance) { StartCoroutine(KillRoutine()); break; }
                if (Time.time - lastSeenTime > loseSightTime)
                {
                    SetState(State.Search); onLostPlayer?.Invoke();
                    agent.SetDestination(Ground(lastKnown));
                }
                break;
        }
        if (voice) voice.pitch = state == State.Chase ? 1.1f : 0.85f;
        Animate(dt);
    }

    void Notice(bool sees, float dist, float dt)
    {
        if (sees)
        {
            float rate = dist < closeSenseDistance * 2f ? 100f : (1f / timeToNotice) * Mathf.Lerp(1.6f, 0.5f, dist / viewDistance);
            if (state != State.Patrol) rate *= 2f;
            notice += rate * dt;
            if (notice >= 1f) { notice = 0f; SetState(State.Spotting); }
        }
        else notice = Mathf.Max(0f, notice - dt * 0.5f);
    }

    void UpdateWander(float dt)
    {
        agent.isStopped = false;
        agent.speed = state == State.Patrol ? patrolSpeed : patrolSpeed * 1.7f;
        if (agent.pathPending || agent.remainingDistance > agent.stoppingDistance + 0.2f) return;
        pauseTimer += dt;                                                 // arrived: stand there, listening
        if (state == State.Patrol) { if (pauseTimer > waypointPause) { pauseTimer = 0f; PickNextPatrolPoint(); } }
        else if (pauseTimer > searchTime) { SetState(State.Patrol); PickNextPatrolPoint(); }
        else if (state == State.Search && Random.value < dt * 0.6f) agent.SetDestination(RandomPoint(lastKnown, 4f));
    }

    void SetState(State s)
    {
        state = s; stateTimer = 0f; pauseTimer = 0f;
        switch (s)
        {
            case State.Spotting:
                agent.isStopped = true;
                headRollTarget = 65f;                                     // its neck snaps sideways when it sees you
                if (sfx && spotSting) sfx.PlayOneShot(spotSting);
                onSpotted?.Invoke();
                break;
            case State.Chase: headRollTarget = 28f; break;
            case State.Patrol: headRollTarget = 12f; break;
            default: headRollTarget = 18f; break;
        }
    }

    // other monsters (or doors, noises...) can call these
    public void Alert(Vector3 position) { if (state == State.Patrol || state == State.Search || state == State.Investigate) Investigate(position); }
    public void AlertAtPlayer() { if (playerCamera) Alert(playerCamera.transform.position); }
    // lets the CeilingWatcher's "Hunter" slot point at this monster: it comes to look instead of teleporting
    public void TeleportBehindPlayer() => AlertAtPlayer();

    void Investigate(Vector3 pos)
    {
        lastKnown = pos;
        if (state != State.Investigate) SetState(State.Investigate);
        if (agent.isOnNavMesh) agent.SetDestination(Ground(pos));
    }

    bool CanSee(Vector3 target, out float dist)
    {
        Vector3 eye = HeadCenter();
        Vector3 to = target - eye; dist = to.magnitude;
        if (dist > viewDistance) return false;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (dist > closeSenseDistance && Vector3.Angle(transform.forward, flat) > viewAngle * 0.5f) return false;
        if (Physics.Linecast(eye, target, out var hit, sightBlockers, QueryTriggerInteraction.Ignore))
            return hit.transform.IsChildOf(transform) || (playerRoot && hit.transform.IsChildOf(playerRoot));
        return true;
    }

    void PickNextPatrolPoint()
    {
        if (!agent.isOnNavMesh) return;
        if (waypoints != null && waypoints.Length > 0)
        {
            var w = waypoints[waypointIndex++ % waypoints.Length];
            if (w) agent.SetDestination(Ground(w.position));
        }
        else agent.SetDestination(RandomPoint(transform.position, wanderRadius));
    }

    Vector3 Ground(Vector3 p) => NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas) ? hit.position : p;
    Vector3 RandomPoint(Vector3 center, float radius)
    {
        for (int i = 0; i < 12; i++)
            if (NavMesh.SamplePosition(center + Random.insideUnitSphere * radius, out var hit, 2f, NavMesh.AllAreas)) return hit.position;
        return center;
    }
    float FlatDistance(Vector3 p) { Vector3 d = p - transform.position; d.y = 0f; return d.magnitude; }
    void FaceTowards(Vector3 p, float t)
    {
        Vector3 d = p - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), t);
    }

    // ================================================================ kill
    IEnumerator KillRoutine()
    {
        killing = true; state = State.Kill;
        agent.isStopped = true; agent.velocity = Vector3.zero;
        if (disableOnKill != null) foreach (var b in disableOnKill) if (b) b.enabled = false;
        Transform cam = playerCamera.transform;
        startFov = playerCamera.fieldOfView;
        Vector3 fwd = cam.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) { fwd = transform.position - cam.position; fwd.y = 0f; }
        fwd.Normalize();
        float d = 1.4f;    // appear right in your face, without going into a wall
        RaycastHit hit;
        if (Physics.Raycast(cam.position, fwd, out hit, 1.8f, sightBlockers, QueryTriggerInteraction.Ignore)
            && !(playerRoot && hit.transform.IsChildOf(playerRoot))) d = Mathf.Max(0.5f, hit.distance - 0.35f);
        agent.Warp(Ground(cam.position + fwd * d));
        transform.rotation = Quaternion.LookRotation(-fwd);
        headRollTarget = 40f;
        if (sfx && killScream) sfx.PlayOneShot(killScream);
        yield return new WaitForSeconds(1.7f);
        playerCamera.fieldOfView = startFov;
        if (onKilledPlayer.GetPersistentEventCount() > 0) onKilledPlayer.Invoke();
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ================================================================ procedural animation
    void Animate(float dt)
    {
        float speed = killing || !agent.isOnNavMesh ? 0f : agent.velocity.magnitude;
        bool chasing = state == State.Chase || killing;
        gait = Mathf.MoveTowards(gait, Mathf.Clamp01(speed / Mathf.Max(patrolSpeed, 0.1f)), dt * 3f);
        phase += speed / strideLength * Mathf.PI * dt;
        float s = Mathf.Sin(phase);
        if (Mathf.Sign(s) != Mathf.Sign(lastSin) && gait > 0.3f) Footstep();
        lastSin = s;

        if (Time.time > nextTwitch && state != State.Spotting && !killing)
        {
            headRoll += Random.Range(-35f, 35f);                             // the neck twitches
            nextTwitch = Time.time + Random.Range(1.5f, 5f);
        }
        headRoll = Mathf.Lerp(headRoll, headRollTarget, 1f - Mathf.Exp(-(state == State.Spotting ? 30f : 3f) * dt));

        animTimer += dt;
        if (animTimer >= 1f / (chasing ? chaseAnimFps : walkAnimFps)) { animTimer = 0f; Pose(chasing); }
    }

    void Pose(bool chasing)
    {
        float ph = phase, s = Mathf.Sin(ph), c = Mathf.Cos(ph), g = gait;
        float hipA = (chasing ? 42f : 26f) * g, kneeA = (chasing ? 70f : 42f) * g, armA = (chasing ? 30f : 16f) * g;
        float breathe = Mathf.Sin(Time.time * 1.3f);
        for (int side = 0; side < 2; side++)
        {
            float sg = side == 0 ? 1f : -1f, sp = s * sg, cp = c * sg;
            int lu = side == 0 ? LUL : LUR, ll = side == 0 ? LLL : LLR, ft = side == 0 ? FL : FR;
            int au = side == 0 ? AUL : AUR, al = side == 0 ? ALL : ALR, hd = side == 0 ? HL : HR;
            float hip = -hipA * sp, knee = 6f + kneeA * Mathf.Max(0f, cp);
            Rot(lu, hip, 0f, 1.5f * sg); Rot(ll, -knee); Rot(ft, -(hip - knee) * 0.8f);     // knees bend backwards
            Rot(au, -8f + armA * sp + breathe * 1.5f, 0f, 6f * sg);
            Rot(al, -18f - 0.5f * armA * (1f + sp));
            Rot(hd, -6f);
        }
        if (chasing && !killing)
        {
            float k = Mathf.Repeat(Time.time * 1.1f, 1f);                      // huge knife arm: slow raise, fast low sweep
            float sweep = k < 0.75f ? Mathf.Lerp(-10f, -85f, k / 0.75f) : Mathf.Lerp(-85f, 20f, (k - 0.75f) / 0.25f);
            Rot(AUR, sweep, 0f, -12f); Rot(ALR, -20f); Rot(HR, -15f);
            Rot(AUL, -72f + 10f * s, 0f, 10f); Rot(ALL, -12f);              // the sack hand reaches for you
        }
        if (killing)
        {
            Rot(AUL, -125f + Random.Range(-8f, 8f), 0f, 25f); Rot(ALL, -20f);
            Rot(AUR, -95f + Random.Range(-8f, 8f), 0f, -25f); Rot(ALR, -30f);
        }
        Rot(TORSO, (chasing ? 14f : 0f) + 3f * Mathf.Sin(2f * ph) * g + breathe * 1.2f, 4f * s * g, 3f * s * g);
        joints[HIPS].localPosition = restLocal[HIPS] + Vector3.down * (0.05f * Mathf.Abs(c) * g);

        // head: wrong angle, scans while searching, locks onto you while hunting
        Quaternion look = Quaternion.identity;
        if (state == State.Chase || state == State.Spotting || killing) look = LookAtPlayer();
        else if (state == State.Search || state == State.Investigate) look = Quaternion.Euler(0f, Mathf.Sin(Time.time * 0.9f) * 65f, 0f);
        headLook = Quaternion.Slerp(headLook, look, 0.5f);
        joints[HEAD].localRotation = headLook * Quaternion.Euler(6f, 0f, headRoll * mirror);

        // bones come apart for a frame
        for (int i = 1; i < joints.Length; i++) joints[i].localPosition = restLocal[i];
        float chance = killing ? 0.7f : (chasing ? boneDesyncChance : boneDesyncChance * 0.25f);
        if (Random.value < chance)
        {
            int i = Random.Range(2, joints.Length);
            joints[i].localPosition = restLocal[i] + Random.insideUnitSphere * (killing ? 0.09f : 0.045f);
        }
    }

    Quaternion LookAtPlayer()
    {
        if (!playerCamera) return Quaternion.identity;
        Vector3 local = joints[TORSO].InverseTransformDirection(playerCamera.transform.position - HeadCenter());
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -75f, 75f);
        float pitch = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, -60f, 50f);
        return Quaternion.Euler(pitch, yaw, 0f);
    }

    void Footstep()
    {
        if (!sfx || footstepClips == null || footstepClips.Length == 0) return;
        var clip = footstepClips[Random.Range(0, footstepClips.Length)];
        if (clip) sfx.PlayOneShot(clip, state == State.Chase ? 1f : 0.6f);
    }

    void LateUpdate()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        // the pillowcase swings like a pendulum from its hand
        var sack = joints[SACK];
        Vector3 v = (sack.position - sackLastPos) / dt; sackLastPos = sack.position;
        Vector3 a = Vector3.ClampMagnitude((v - sackLastVel) / dt, 30f); sackLastVel = v;
        sackVel += new Vector2(a.z, -a.x) * 60f * dt;
        sackVel -= (sackAngle * 40f + sackVel * 4f) * dt;
        sackAngle = Vector2.ClampMagnitude(sackAngle + sackVel * dt, 50f);
        sack.rotation = Quaternion.Euler(sackAngle.x, 0f, sackAngle.y) * Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        if (killing && playerCamera)            // force the player to look up into its face
        {
            Transform cam = playerCamera.transform;
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(HeadCenter() - cam.position), 1f - Mathf.Exp(-18f * dt));
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, startFov * 0.65f, 1f - Mathf.Exp(-6f * dt));
        }

        // pinprick eyes: dim while calm, burning while it hunts you, strobing during the kill
        bool hunting = state == State.Chase || state == State.Spotting || killing;
        glow = Mathf.Lerp(glow, (hunting ? huntingEyeGlow : calmEyeGlow) * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 4f, 0.5f)), 1f - Mathf.Exp(-5f * dt));
        float shown = killing && Random.value < 0.25f ? (Random.value < 0.5f ? 0f : glow * 2f) : glow;
        if (headRenderer)
        {
            headRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, eyeColor * shown);
            headRenderer.SetPropertyBlock(mpb);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.35f); Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, viewDistance);
        Gizmos.color = new Color(1f, 0f, 0f, 0.35f); Gizmos.DrawWireSphere(transform.position, hearRadius);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(transform.position, killDistance);
    }
}
