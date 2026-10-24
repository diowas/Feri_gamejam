// NULLREF – "it's not a bug, it's a feature."  (pumpkin scarecrow edition)
// A T-posing entity that only moves when nobody is looking at it.
// Every glitch is intentional: no walk animation (it slides), choppy 8 fps movement,
// PS1 vertex wobble that gets worse the closer it is, and random vertex explosions.
//
// Setup: drop the NullRef model in the scene, add this script to the root.
// Optional: add a NavMeshAgent (bake a NavMesh) so it paths around your faculty's walls.
// Keep the entity on the "Ignore Raycast" layer (or give it no colliders) so it doesn't
// block its own line-of-sight checks.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

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
    public LayerMask occluders = ~0;            // walls/doors that block the player's view

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

    [Header("Pumpkin glow (optional)")]
    public Light headGlow;                      // orange point light placed inside the pumpkin
    public Renderer glowRenderer;               // material with _EmissionColor (the model itself)
    public Color glowColor = new Color(1f, 0.45f, 0.08f);
    public float glowIntensity = 1.5f;

    [Header("Events")]
    public UnityEvent onCaughtPlayer;           // nothing hooked up -> reloads the scene

    class MeshState { public Mesh mesh; public Vector3[] baseVerts, work; public Vector3 center; }
    readonly List<MeshState> meshes = new List<MeshState>();
    readonly HashSet<int> exploding = new HashSet<int>();
    Renderer[] renderers;
    NavMeshAgent agent;
    float unseenTimer, stepTimer, nextExplosion, explosionEnd;
    bool caught;

    void Awake()
    {
        if (!playerCamera) playerCamera = Camera.main;
        agent = GetComponent<NavMeshAgent>();
        if (agent) { agent.updatePosition = false; agent.updateRotation = false; agent.speed = slideSpeed; }
        renderers = GetComponentsInChildren<Renderer>();
        foreach (var mf in GetComponentsInChildren<MeshFilter>())
        {
            var m = Instantiate(mf.sharedMesh);          // never edit the imported asset
            m.MarkDynamic();
            var b = m.bounds; b.Expand(explosionStretch * 2f); m.bounds = b;   // exploded verts must not get culled
            mf.sharedMesh = m;
            meshes.Add(new MeshState { mesh = m, baseVerts = m.vertices, work = new Vector3[m.vertexCount], center = m.bounds.center });
        }
        nextExplosion = Time.time + Random.Range(1f, explosionInterval);
    }

    void Update()
    {
        if (caught || !playerCamera) return;
        Vector3 playerPos = playerCamera.transform.position;
        Vector3 flatTo = playerPos - transform.position; flatTo.y = 0;
        float dist = flatTo.magnitude;
        bool watched = IsWatched();

        if (dist < catchDistance) { Catch(); return; }

        // ---- movement: only when nobody is looking
        stepTimer += Time.deltaTime;
        if (watched)
        {
            unseenTimer = 0;
            if (agent) { agent.isStopped = true; agent.nextPosition = transform.position; }
        }
        else
        {
            unseenTimer += Time.deltaTime;
            if (agent) { agent.isStopped = false; agent.SetDestination(playerPos); }

            if (unseenTimer > teleportAfterUnseen && dist > teleportBehindDistance * 1.5f)
            {
                unseenTimer = 0;
                if (Random.value < teleportChance) TeleportBehindPlayer();
            }
            else if (stepTimer >= 1f / stepRate)      // choppy, low-framerate slide
            {
                float dt = stepTimer;
                if (agent) transform.position = agent.nextPosition;
                else transform.position = Vector3.MoveTowards(transform.position,
                    transform.position + flatTo.normalized * dist, slideSpeed * dt);
                FacePlayer(flatTo);
            }
        }
        if (stepTimer >= 1f / stepRate) stepTimer = 0;
        if (watched) FacePlayer(flatTo);              // it still turns to stare, in snapped steps

        // ---- glitch mesh
        if (Time.time >= nextExplosion) StartExplosion();
        float proximity = Mathf.InverseLerp(15f, catchDistance, dist);
        float jitter = watched ? watchedJitter * (0.3f + proximity * 2f) : 0f;
        UpdateMeshes(jitter, Time.time < explosionEnd);

        if (proximityHum) proximityHum.volume = proximity;

        // candle flicker; flares and dies on explosions, burns brighter the closer it gets
        float flicker = Mathf.PerlinNoise(Time.time * 9f, 0.3f) * 0.6f + 0.4f;
        if (Time.time < explosionEnd) flicker = Random.value < 0.5f ? 0f : 3f;
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
            if (!Physics.Linecast(cam, p, out var hit, occluders, QueryTriggerInteraction.Ignore)
                || hit.transform.IsChildOf(transform)) return true;
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
        Vector3 target = playerCamera.transform.position + back * teleportBehindDistance;
        target.y = transform.position.y;
        if (NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas)) target = hit.position;
        else if (agent) return;                        // no valid floor behind the player, skip
        transform.position = target;
        if (agent) agent.Warp(target);
        StartExplosion();
    }

    void StartExplosion()
    {
        exploding.Clear();
        int total = 0; foreach (var s in meshes) total += s.baseVerts.Length;
        int count = Mathf.Max(1, Mathf.RoundToInt(total * explosionVertexFraction));
        for (int i = 0; i < count; i++) exploding.Add(Random.Range(0, total));
        explosionEnd = Time.time + explosionLength;
        nextExplosion = Time.time + explosionInterval * Random.Range(0.5f, 1.5f);
        if (glitchAudio) { glitchAudio.pitch = Random.Range(0.7f, 1.4f); glitchAudio.Play(); }
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
        explosionStretch *= 3f; StartExplosion(); UpdateMeshes(watchedJitter * 4f, true);
        if (onCaughtPlayer != null && onCaughtPlayer.GetPersistentEventCount() > 0) onCaughtPlayer.Invoke();
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
