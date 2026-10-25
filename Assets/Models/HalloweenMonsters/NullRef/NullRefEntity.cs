// NULLREF – "it's not a bug, it's a feature."  (pumpkin scarecrow edition)
// A T-posing entity that only moves when nobody is looking at it.
// Every glitch is intentional: no walk animation (it slides), choppy 8 fps movement,
// PS1 vertex wobble that gets worse the closer it is, and random vertex explosions.
//
// It only hunts you after it has SEEN you (clear line, same floor). Lose it and it goes to where it last
// saw you, then gives up.
//
// Setup: drop the NullRef model in the scene, add this script to the root and bake a NavMesh
// (a NavMeshAgent is added for you) so it paths around walls and up the stairs.
// Keep the entity on the "Ignore Raycast" layer (or give it no colliders) so it doesn't
// block its own line-of-sight checks.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NavMeshAgent))]
public class NullRefEntity : MonoBehaviour
{
    [Header("Target")]
    public Camera playerCamera;                 // leave empty -> Camera.main

    [Header("Movement (only while unobserved)")]
    public float slideSpeed = 4f;
    public float stepRate = 8f;                 // position updates per second -> choppy "missing frames"
    public float catchDistance = 1.3f;
    public float teleportAfterUnseen = 7f;      // seconds unseen before it may jump behind you
    public float teleportBehindDistance = 5f;
    [Range(0, 1)] public float teleportChance = 0.4f;
    public LayerMask occluders = ~0;            // walls/doors that block the player's view (and its own)

    [Header("Senses")]
    public float maxHeightDifference = 2.5f;    // player further above/below than this is on another floor: it can't see you

    [Header("Glitch look")]
    public float vertexSnap = 0.012f;           // PS1 vertex precision (object space, metres)
    public float watchedJitter = 0.03f;         // wobble while watched, scaled up with proximity
    public float explosionInterval = 2.5f;
    public float explosionLength = 0.12f;
    public float explosionStretch = 6f;
    [Range(0, 1)] public float explosionVertexFraction = 0.02f;
    public float rotationStep = 22.5f;          // turns to face you in snapped increments

    [Header("Audio (optional)")]
    public AudioSource glitchAudio;             // short bitcrushed burst, played on explosions
    public AudioSource proximityHum;            // looping drone, volume rises as it gets closer
    public AudioClip nearYouClip;               // looped on proximityHum (made for you if empty)
    public AudioClip[] glitchClips;             // a random one plays on glitchAudio at every explosion
    public float hearDistance = 15f;            // it's silent further away than this, or on another floor

    [Header("Pumpkin glow (optional)")]
    public Light headGlow;                      // orange point light placed inside the pumpkin
    public Renderer glowRenderer;               // material with _EmissionColor (the model itself)
    public Color glowColor = new Color(1f, 0.45f, 0.08f);
    public float glowIntensity = 1.5f;

    [Header("Catch jumpscare")]
    public MonoBehaviour[] disableOnCatch = new MonoBehaviour[0];   // extra movement / mouse-look scripts (FeriPlayer is handled for you)
    public float jumpscareTime = 1.6f;
    public AudioClip catchScream;

    [Header("Events")]
    public UnityEvent onCaughtPlayer;           // nothing hooked up -> reloads the scene

    public bool HasCaught => caught;

    class MeshState { public Mesh mesh; public Vector3[] baseVerts, work; public Vector3 center; }
    readonly List<MeshState> meshes = new List<MeshState>();
    readonly HashSet<int> exploding = new HashSet<int>();
    Renderer[] renderers;
    Renderer headPart;
    NavMeshAgent agent;
    Transform playerBody;
    Vector3 lastKnown;
    float unseenTimer, stepTimer, nextExplosion, explosionEnd;
    bool caught, hunting;
    static readonly RaycastHit[] sightHits = new RaycastHit[16];

    void Reset()
    {
        var a = GetComponent<NavMeshAgent>();
        if (a) { a.radius = 0.4f; a.height = 2f; a.acceleration = 40f; a.angularSpeed = 0f; a.stoppingDistance = 0.2f; }
    }

    void Awake()
    {
        if (!playerCamera) playerCamera = Camera.main;
        if (playerCamera)
        {
            var body = playerCamera.GetComponentInParent<CharacterController>();
            playerBody = body ? body.transform : playerCamera.transform;
        }
        agent = GetComponent<NavMeshAgent>();
        if (!agent)    // older instances have none: without it, it slid through walls and hovered under the player
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.4f; agent.height = 2f; agent.acceleration = 40f; agent.angularSpeed = 0f; agent.stoppingDistance = 0.2f;
        }
        agent.updatePosition = false; agent.updateRotation = false; agent.speed = slideSpeed;
        renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers) if (r.name.ToLowerInvariant().Contains("head")) { headPart = r; break; }
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            var m = Instantiate(mf.sharedMesh);          // never edit the imported asset
            m.MarkDynamic();
            var b = m.bounds; b.Expand(explosionStretch * 2f); m.bounds = b;   // exploded verts must not get culled
            mf.sharedMesh = m;
            meshes.Add(new MeshState { mesh = m, baseVerts = m.vertices, work = new Vector3[m.vertexCount], center = m.bounds.center });
        }
        nextExplosion = FeriMap.FeriSlow.Time + Random.Range(1f, explosionInterval);

        // clips without a source never play: make the sources if nobody set them up
        if (!proximityHum && nearYouClip) proximityHum = MakeSource("NearYou", true, 0.6f);
        if (proximityHum)
        {
            if (!proximityHum.clip) proximityHum.clip = nearYouClip;
            proximityHum.loop = true; proximityHum.volume = 0f;
            if (proximityHum.clip) proximityHum.Play();
        }
        if (!glitchAudio && glitchClips != null && glitchClips.Length > 0) { glitchAudio = MakeSource("Glitch", false, 1f); glitchAudio.volume = 0.6f; }
        if (catchScream) catchScream.LoadAudioData();   // its import has preloading off: don't let the scream start late
    }

    void OnDisable() { if (proximityHum) proximityHum.Stop(); }   // e.g. FeriEnding switching it off
    void OnEnable() { if (proximityHum && proximityHum.clip && !caught) proximityHum.Play(); }

    AudioSource MakeSource(string n, bool loop, float spatial)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.6f;
        var a = go.AddComponent<AudioSource>();
        a.playOnAwake = false; a.loop = loop;
        a.spatialBlend = spatial; a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = 1.5f; a.maxDistance = hearDistance; a.dopplerLevel = 0f;
        return a;
    }

    void Update()
    {
        if (caught || !playerCamera) return;
        Vector3 playerPos = playerCamera.transform.position;
        Vector3 flatTo = playerPos - transform.position; flatTo.y = 0;
        float dist = flatTo.magnitude;
        bool watched = IsWatched();
        bool seesPlayer = SeesPlayer(playerPos);
        if (seesPlayer) { hunting = true; lastKnown = playerPos; }

        if (seesPlayer && dist < catchDistance) { Catch(); return; }

        // ---- movement: only when nobody is looking, and only toward where it last saw you
        stepTimer += FeriMap.FeriSlow.DeltaTime;
        if (watched || !hunting || !agent.isOnNavMesh)
        {
            unseenTimer = 0;
            if (agent.isOnNavMesh) { agent.isStopped = true; agent.nextPosition = transform.position; }
        }
        else
        {
            unseenTimer += FeriMap.FeriSlow.DeltaTime;
            agent.isStopped = false;
            agent.SetDestination(NavMesh.SamplePosition(lastKnown, out var goal, 3f, NavMesh.AllAreas) ? goal.position : lastKnown);
            // reached the spot where it lost you: it gives up and waits there
            if (!seesPlayer && !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f) hunting = false;

            if (seesPlayer && unseenTimer > teleportAfterUnseen && dist > teleportBehindDistance * 1.5f)
            {
                unseenTimer = 0;
                if (Random.value < teleportChance) TeleportBehindPlayer();
            }
            else if (stepTimer >= 1f / stepRate)      // choppy, low-framerate slide
            {
                transform.position = agent.nextPosition;
                FacePlayer(flatTo);
            }
        }
        if (stepTimer >= 1f / stepRate) stepTimer = 0;
        if (watched) FacePlayer(flatTo);              // it still turns to stare, in snapped steps

        // ---- glitch mesh
        if (FeriMap.FeriSlow.Time >= nextExplosion) StartExplosion();
        float proximity = Mathf.InverseLerp(hearDistance, catchDistance, dist);
        if (Mathf.Abs(playerPos.y - (transform.position.y + 1.6f)) > maxHeightDifference) proximity = 0f;   // another floor
        float jitter = watched ? watchedJitter * (0.3f + proximity * 2f) : 0f;
        UpdateMeshes(jitter, FeriMap.FeriSlow.Time < explosionEnd);

        if (proximityHum) proximityHum.volume = Mathf.MoveTowards(proximityHum.volume, proximity, Time.deltaTime);

        // candle flicker; flares and dies on explosions, burns brighter the closer it gets
        float flicker = Mathf.PerlinNoise(FeriMap.FeriSlow.Time * 9f, 0.3f) * 0.6f + 0.4f;
        if (FeriMap.FeriSlow.Time < explosionEnd) flicker = Random.value < 0.5f ? 0f : 3f;
        float glow = glowIntensity * flicker * (0.6f + proximity);
        if (headGlow) headGlow.intensity = glow;
        if (glowRenderer) glowRenderer.material.SetColor("_EmissionColor", glowColor * glow);
    }

    bool IsWatched()
    {
        bool anyVisible = false;
        foreach (var r in renderers) if (r.isVisible) { anyVisible = true; break; }
        if (!anyVisible) return false;                 // outside frustum (or culled)
        Vector3 cam = playerCamera.transform.position;
        // sample feet / chest / head: watched if any is on-screen and not behind a wall
        foreach (float h in new[] { 0.3f, 1.6f, 2.6f })
        {
            Vector3 p = transform.position + Vector3.up * h;
            Vector3 vp = playerCamera.WorldToViewportPoint(p);
            if (vp.z <= 0 || vp.x < 0 || vp.x > 1 || vp.y < 0 || vp.y > 1) continue;
            if (Clear(cam, p)) return true;
        }
        return false;
    }

    // it sees the player: same floor and nothing solid between its head and the player's eyes
    bool SeesPlayer(Vector3 playerEye)
    {
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        return Mathf.Abs(playerEye.y - eye.y) < maxHeightDifference && Clear(eye, playerEye);
    }

    // check both ways: floor/ceiling meshes are one-sided, a ray from below passes straight through them
    bool Clear(Vector3 a, Vector3 b) => !Blocked(a, b) && !Blocked(b, a);
    bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; float len = d.magnitude;
        int n = Physics.RaycastNonAlloc(from, d / Mathf.Max(len, 1e-4f), sightHits, len, occluders, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Transform t = sightHits[i].transform;
            if (!t.IsChildOf(transform) && !(playerBody && t.IsChildOf(playerBody))) return true;
        }
        return false;
    }

    void FacePlayer(Vector3 flatTo)
    {
        if (flatTo.sqrMagnitude < 0.001f) return;
        float yaw = Mathf.Atan2(flatTo.x, flatTo.z) * Mathf.Rad2Deg;
        yaw = Mathf.Round(yaw / rotationStep) * rotationStep;
        transform.rotation = Quaternion.Euler(0, yaw, 0);
    }

    void TeleportBehindPlayer()
    {
        Vector3 back = -playerCamera.transform.forward; back.y = 0; back.Normalize();
        // the player's spot on the NavMesh; none (elevator, mid-jump) -> skip
        if (!NavMesh.SamplePosition(playerCamera.transform.position, out var me, 2.5f, NavMesh.AllAreas)) return;
        if (!NavMesh.SamplePosition(me.position + back * teleportBehindDistance, out var hit, 1f, NavMesh.AllAreas)) return;
        if (Mathf.Abs(hit.position.y - me.position.y) > 1f) return;                 // different floor behind you
        if (NavMesh.Raycast(me.position, hit.position, out _, NavMesh.AllAreas)) return;   // a wall between you and that spot
        transform.position = hit.position;
        agent.Warp(hit.position);
        StartExplosion();
    }

    void StartExplosion()
    {
        exploding.Clear();
        int total = 0; foreach (var s in meshes) total += s.baseVerts.Length;
        int count = Mathf.Max(1, Mathf.RoundToInt(total * explosionVertexFraction));
        for (int i = 0; i < count; i++) exploding.Add(Random.Range(0, total));
        explosionEnd = FeriMap.FeriSlow.Time + explosionLength;
        nextExplosion = FeriMap.FeriSlow.Time + explosionInterval * Random.Range(0.5f, 1.5f);
        if (glitchAudio)
        {
            if (glitchClips != null && glitchClips.Length > 0) glitchAudio.clip = glitchClips[Random.Range(0, glitchClips.Length)];
            glitchAudio.pitch = Random.Range(0.7f, 1.4f);
            if (glitchAudio.clip) glitchAudio.Play();
        }
    }

    void UpdateMeshes(float jitter, bool explode)
    {
        int offset = 0;
        foreach (var s in meshes)
        {
            for (int i = 0; i < s.baseVerts.Length; i++)
            {
                Vector3 v = s.baseVerts[i];
                if (jitter > 0) v += Random.insideUnitSphere * jitter;
                if (explode && exploding.Contains(offset + i)) v = s.center + (v - s.center) * explosionStretch;
                if (vertexSnap > 0)
                    v = new Vector3(Mathf.Round(v.x / vertexSnap), Mathf.Round(v.y / vertexSnap), Mathf.Round(v.z / vertexSnap)) * vertexSnap;
                s.work[i] = v;
            }
            s.mesh.vertices = s.work;
            offset += s.baseVerts.Length;
        }
    }

    void Catch()
    {
        caught = true;
        StartCoroutine(CatchRoutine());
    }

    // it snaps right in front of you, your head is yanked up into the pumpkin, and it tears itself apart
    IEnumerator CatchRoutine()
    {
        if (agent.isOnNavMesh) agent.isStopped = true;
        if (disableOnCatch != null) foreach (var b in disableOnCatch) if (b) b.enabled = false;
        var player = playerBody ? playerBody.GetComponent<FeriMap.FeriPlayer>() : null;
        if (player) player.inputEnabled = false;

        Transform cam = playerCamera.transform;
        Vector3 fwd = cam.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) { fwd = transform.position - cam.position; fwd.y = 0f; }
        fwd.Normalize();
        Vector3 spot = cam.position + fwd * 0.9f;
        spot.y = playerBody ? playerBody.position.y : transform.position.y;
        transform.position = spot;
        transform.rotation = Quaternion.LookRotation(-fwd);

        var mpb = new MaterialPropertyBlock();
        foreach (var r in renderers) JumpscareDirector.MakeSelfLit(r, mpb, 0.7f);   // visible even in the blackout
        if (catchScream) AudioSource.PlayClipAtPoint(catchScream, cam.position);
        if (proximityHum) proximityHum.volume = 1f;

        explosionStretch *= 3f;
        float fov = playerCamera.fieldOfView, nextBurst = 0f;
        Quaternion look = cam.rotation;
        for (float t = 0f; t < jumpscareTime; t += Time.deltaTime)
        {
            if (t >= nextBurst) { StartExplosion(); nextBurst = t + Random.Range(0.08f, 0.2f); }
            UpdateMeshes(watchedJitter * 4f, Random.value < 0.6f);
            Vector3 head = headPart ? headPart.bounds.center : transform.position + Vector3.up * 2.4f;
            look = Quaternion.Slerp(look, Quaternion.LookRotation(head - cam.position), 1f - Mathf.Exp(-18f * Time.deltaTime));
            cam.rotation = look;
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, fov * 0.6f, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (headGlow) headGlow.intensity = Random.value < 0.5f ? 0f : glowIntensity * 4f;   // the candle strobes
            yield return null;
        }
        playerCamera.fieldOfView = fov;
        if (onCaughtPlayer != null && onCaughtPlayer.GetPersistentEventCount() > 0) onCaughtPlayer.Invoke();
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
