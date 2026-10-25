// FLOATING EYES – torn-out eyeballs drifting just under the ceiling, always looking at you.
// Put one or more eye models as CHILDREN of an empty object and add this script to that empty.
//   - Two eyes ~0.4 m apart = "someone" staring at you from the dark.
//   - One eye on its own, or a loose cluster, also works.
// Behaviour:
//   - Every eye always turns to face the player (with twitchy saccades and PS1-snapped rotation).
//   - They drift after you near the ceiling, keeping their distance, bobbing gently.
//   - While you look at them they freeze and their eyeshine dims.
//   - Stare straight at them too long and they blink out of existence... and reappear behind you.
//   - Walk right up to them and they back away into the dark.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class FloatingEyes : MonoBehaviour
{
    [Header("Target")]
    public Camera playerCamera;                    // empty -> Camera.main

    [Header("Floors")]
    [Tooltip("Floor heights (Y) where the eyes hunt. Default: floor 0 and floor 4 of the FERI building.")]
    public float[] huntFloors = { 2f, 18f };
    public float floorHeight = 4f;                 // floor-to-floor spacing on those floors

    [Header("Hovering")]
    public float ceilingGap = 0.35f;               // how far below the ceiling they float
    public float fallbackHeight = 2.6f;            // used if no ceiling is found above them
    public LayerMask ceilingMask = ~0;
    public float bobAmount = 0.06f, bobSpeed = 0.9f;

    [Header("Following")]
    public float preferredDistance = 6f;
    public float minDistance = 2f;                 // closer than this -> they retreat
    public float driftSpeed = 1.2f;
    public float retreatSpeed = 4f;

    [Header("Staring")]
    public float stareTimeToVanish = 1.5f;         // seconds of direct eye contact before they blink out
    public float reappearDistance = 5f;
    public float lookTurnSpeed = 8f;
    public float snapAngle = 3f;
    public float saccadeDegrees = 4f;

    [Header("Glow (pupils)")]
    public Color eyeshineTint = Color.white;
    public float peripheralGlow = 1.8f, directGlow = 0.3f;

    [Header("Events")]
    public UnityEvent onVanished, onReappeared;

    readonly List<Transform> eyes = new List<Transform>();
    readonly List<Renderer> renderers = new List<Renderer>();
    readonly List<Vector3> saccades = new List<Vector3>();
    MaterialPropertyBlock mpb;
    Vector3 velocity, baseScale;
    float stareTimer, glow, nextSaccade, bobPhase, sideBias;
    bool vanishing;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        if (!playerCamera) playerCamera = Camera.main;
        mpb = new MaterialPropertyBlock();
        foreach (Transform child in transform) { eyes.Add(child); saccades.Add(Vector3.zero); }
        renderers.AddRange(GetComponentsInChildren<Renderer>());
        baseScale = transform.localScale;
        bobPhase = Random.value * 10f;
        sideBias = Random.Range(-1f, 1f);
    }

    void Update()
    {
        if (!playerCamera || vanishing) return;
        float dt = FeriMap.FeriSlow.DeltaTime;
        Transform cam = playerCamera.transform;

        // ---- only on the hunt floors: elsewhere they hang still; on another floor than you they blink over to yours
        int playerFloor = FloorOf(cam.position.y);
        if (playerFloor < 0) { stareTimer = 0; return; }
        if (FloorOf(transform.position.y) != playerFloor) { StartCoroutine(BlinkOut(true)); return; }

        Vector3 toMe = transform.position - cam.position;
        float dist = toMe.magnitude;
        float facing = Vector3.Dot(cam.forward, toMe / Mathf.Max(dist, 0.001f));
        bool inView = facing > 0.55f && !Physics.Linecast(cam.position, transform.position, ceilingMask, QueryTriggerInteraction.Ignore);
        bool staredAt = inView && facing > 0.985f;

        // ---- movement
        Vector3 desired = transform.position;
        Vector3 flat = new Vector3(toMe.x, 0, toMe.z);
        if (dist < minDistance)
            desired = transform.position + flat.normalized * retreatSpeed;                      // back off into the dark
        else if (!inView)
        {
            // drift toward a point at the preferred distance, slightly off to one side of the player
            Vector3 dir = flat.sqrMagnitude > 0.01f ? flat.normalized : -cam.forward;
            dir = Quaternion.Euler(0, 25f * sideBias, 0) * dir;
            desired = cam.position + dir * preferredDistance;
        }
        desired.y = HoverHeight(desired) + Mathf.Sin((FeriMap.FeriSlow.Time + bobPhase) * bobSpeed) * bobAmount;
        float speed = dist < minDistance ? retreatSpeed : (inView ? 0f : driftSpeed);
        Vector3 next = Vector3.SmoothDamp(transform.position, desired, ref velocity, 0.8f, speed);
        if (inView && dist >= minDistance) next.y = desired.y;                                   // frozen, but still bobbing
        transform.position = next;

        // ---- every eye looks at you
        if (FeriMap.FeriSlow.Time > nextSaccade)
        {
            for (int i = 0; i < saccades.Count; i++) saccades[i] = Random.insideUnitSphere * Mathf.Tan(saccadeDegrees * Mathf.Deg2Rad);
            nextSaccade = FeriMap.FeriSlow.Time + Random.Range(0.4f, 1.8f);
        }
        for (int i = 0; i < eyes.Count; i++)
        {
            Vector3 d = (cam.position - eyes[i].position).normalized + saccades[i];
            Quaternion want = Quaternion.LookRotation(d.normalized, Vector3.up);
            Quaternion q = Quaternion.Slerp(eyes[i].rotation, want, 1f - Mathf.Exp(-lookTurnSpeed * dt));
            Vector3 e = q.eulerAngles;
            if (snapAngle > 0) e = new Vector3(Mathf.Round(e.x / snapAngle), Mathf.Round(e.y / snapAngle), Mathf.Round(e.z / snapAngle)) * snapAngle;
            eyes[i].rotation = Quaternion.Euler(e);
        }

        // ---- stare too long and they blink out
        stareTimer = staredAt ? stareTimer + dt : Mathf.Max(0, stareTimer - dt);
        if (stareTimer > stareTimeToVanish) StartCoroutine(BlinkOut());

        // ---- eyeshine: bright in peripheral vision, dim when looked at directly
        float target = inView ? Mathf.Lerp(peripheralGlow, directGlow, Mathf.InverseLerp(0.9f, 0.99f, facing)) : peripheralGlow;
        target *= 0.85f + 0.15f * Mathf.PerlinNoise(FeriMap.FeriSlow.Time * 2.5f, bobPhase);
        glow = Mathf.Lerp(glow, target, 1f - Mathf.Exp(-5f * dt));
        SetGlow(glow);
    }

    float HoverHeight(Vector3 at)
    {
        Vector3 from = new Vector3(at.x, playerCamera.transform.position.y, at.z);
        if (Physics.Raycast(from, Vector3.up, out var hit, 10f, ceilingMask, QueryTriggerInteraction.Ignore))
            return hit.point.y - ceilingGap;
        return fallbackHeight;
    }

    // index into huntFloors of the floor containing height y, or -1
    int FloorOf(float y)
    {
        for (int i = 0; i < huntFloors.Length; i++)
            if (y >= huntFloors[i] - 0.5f && y < huntFloors[i] + floorHeight) return i;
        return -1;
    }

    // relocating = just moving to the player's floor, not a stare-out, so the events stay quiet
    System.Collections.IEnumerator BlinkOut(bool relocating = false)
    {
        vanishing = true; stareTimer = 0;
        // squash shut like a blink, glitch-fast
        for (float t = 0; t < 0.08f; t += FeriMap.FeriSlow.DeltaTime)
        {
            transform.localScale = new Vector3(baseScale.x, baseScale.y * (1 - t / 0.08f), baseScale.z);
            yield return null;
        }
        transform.localScale = Vector3.zero; SetGlow(0);
        if (!relocating) onVanished?.Invoke();
        yield return new WaitForSeconds(Random.Range(1.5f, 4f));

        // reappear behind the player, under the ceiling (or right where they were, if the player left the hunt floors)
        Transform cam = playerCamera.transform;
        Vector3 p = transform.position;
        if (FloorOf(cam.position.y) >= 0)
        {
            Vector3 back = -cam.forward; back.y = 0; back.Normalize();
            p = cam.position + Quaternion.Euler(0, Random.Range(-50f, 50f), 0) * back * reappearDistance;
            if (Physics.Linecast(cam.position, p, out var wall, ceilingMask, QueryTriggerInteraction.Ignore))
                p = wall.point - back * 0.5f;                                                      // don't spawn inside walls
            p.y = HoverHeight(p);
        }
        transform.position = p; velocity = Vector3.zero;
        for (float t = 0; t < 0.5f; t += FeriMap.FeriSlow.DeltaTime)
        {
            transform.localScale = baseScale * Mathf.SmoothStep(0, 1, t / 0.5f);
            yield return null;
        }
        transform.localScale = baseScale;
        if (!relocating) onReappeared?.Invoke();
        vanishing = false;
    }

    void SetGlow(float g)
    {
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, eyeshineTint * g);
            r.SetPropertyBlock(mpb);
        }
    }
}
