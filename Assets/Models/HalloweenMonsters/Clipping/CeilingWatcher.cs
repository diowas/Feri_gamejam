// CLIPPING – "it's not a bug, it's a feature": something grew through the ceiling geometry.
// Every eye follows the player, each at its own speed. A few always lag behind and catch up late.
// The pupils glow (eyeshine) - brightest in your peripheral vision, fading when you look straight at it.
// Stand under it too long and it "reports" you: all eyes snap onto you and it can summon NULLREF.
//
// Setup: put the model's origin flush with the ceiling (it is modelled to clip into it).
// Add this script to the root. Children whose name contains "eye" are found automatically.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class CeilingWatcher : MonoBehaviour
{
    [Header("Target")]
    public Camera playerCamera;                       // empty -> Camera.main

    [Header("Tracking")]
    public float turnSpeed = 7f;
    [Range(0, 1)] public float laggardChance = 0.3f;  // share of eyes that follow slowly
    public float maxAngle = 88f;                      // eyes can't roll back into the ceiling
    public float snapAngle = 3f;                      // PS1-style stepped rotation
    public float saccadeInterval = 1.4f;              // tiny twitchy re-aims, like real eyes
    public float saccadeDegrees = 5f;

    [Header("Spotting")]
    public float spotRadius = 3.5f;                   // horizontal distance that counts as 'under it'
    public float spotDelay = 1.5f;                    // seconds under it before it reacts
    public float spotCooldown = 12f;
    public LayerMask occluders = ~0;
    public AudioSource spotSound;                     // wet squelch / whisper sting
    public GameObject hunter;                         // e.g. the NULLREF object - it gets teleported behind you
    public UnityEvent onPlayerSpotted;

    [Header("Glow")]
    public Color eyeshineTint = Color.white;
    public float peripheralGlow = 1.6f;               // when you're NOT looking directly at it
    public float directGlow = 0.25f;                  // when you stare straight at it
    public float spottedGlow = 5f;

    class Eye { public Transform pivot; public float speed; public Quaternion current; public Vector3 saccade; public float nextSaccade; }
    readonly List<Eye> eyes = new List<Eye>();
    readonly List<Renderer> eyeRenderers = new List<Renderer>();
    MaterialPropertyBlock mpb;
    float underTimer, cooldownUntil, spottedUntil, glow;
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        if (!playerCamera) playerCamera = Camera.main;
        mpb = new MaterialPropertyBlock();
        foreach (var r in GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.name.ToLower().Contains("eye")) continue;
            // OBJ parts share one origin, so give each eye a pivot at its own centre
            var pivot = new GameObject("pivot_" + r.name).transform;
            pivot.SetParent(transform, false);
            pivot.position = r.bounds.center;
            r.transform.SetParent(pivot, true);
            eyes.Add(new Eye
            {
                pivot = pivot,
                speed = Random.value < laggardChance ? turnSpeed * Random.Range(0.12f, 0.3f) : turnSpeed * Random.Range(0.8f, 1.3f),
                current = Quaternion.identity,
                nextSaccade = FeriMap.FeriSlow.Time + Random.Range(0f, saccadeInterval)
            });
            eyeRenderers.Add(r);
        }
    }

    void Update()
    {
        if (!playerCamera) return;
        Vector3 target = playerCamera.transform.position;
        float dt = FeriMap.FeriSlow.DeltaTime;
        bool spotted = FeriMap.FeriSlow.Time < spottedUntil;

        foreach (var e in eyes)
        {
            if (FeriMap.FeriSlow.Time >= e.nextSaccade)
            {
                e.saccade = Random.insideUnitSphere * Mathf.Tan(saccadeDegrees * Mathf.Deg2Rad);
                e.nextSaccade = FeriMap.FeriSlow.Time + saccadeInterval * Random.Range(0.4f, 1.6f);
            }
            Vector3 dir = (target - e.pivot.position).normalized + (spotted ? Vector3.zero : e.saccade);
            Vector3 local = transform.InverseTransformDirection(dir.normalized);
            // irises point down (-Y) at rest; clamp so they never look up into the ceiling
            local = Vector3.RotateTowards(Vector3.down, local, maxAngle * Mathf.Deg2Rad, 0f);
            Quaternion desired = Quaternion.FromToRotation(Vector3.down, local);
            e.current = spotted ? desired : Quaternion.Slerp(e.current, desired, 1f - Mathf.Exp(-e.speed * dt));
            Vector3 eul = e.current.eulerAngles;
            if (snapAngle > 0)
                eul = new Vector3(Mathf.Round(eul.x / snapAngle), Mathf.Round(eul.y / snapAngle), Mathf.Round(eul.z / snapAngle)) * snapAngle;
            e.pivot.localRotation = Quaternion.Euler(eul);
        }

        // ---- spotting: player lingering underneath with line of sight
        Vector3 flat = target - transform.position; flat.y = 0;
        bool under = flat.magnitude < spotRadius && HasLineOfSight(target);
        underTimer = under ? underTimer + dt : Mathf.Max(0, underTimer - dt * 2f);
        if (underTimer > spotDelay && FeriMap.FeriSlow.Time > cooldownUntil) Spot();

        // ---- glow: fades when stared at directly, flares when it spots you
        Vector3 toMe = (transform.position - target).normalized;
        float stare = Mathf.InverseLerp(0.92f, 0.99f, Vector3.Dot(playerCamera.transform.forward, toMe));
        float targetGlow = FeriMap.FeriSlow.Time < spottedUntil ? spottedGlow : Mathf.Lerp(peripheralGlow, directGlow, stare);
        targetGlow *= 0.85f + 0.15f * Mathf.PerlinNoise(FeriMap.FeriSlow.Time * 3f, 0f);
        glow = Mathf.Lerp(glow, targetGlow, 1f - Mathf.Exp(-6f * dt));
        foreach (var r in eyeRenderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor(EmissionId, eyeshineTint * glow);
            r.SetPropertyBlock(mpb);
        }
    }

    bool HasLineOfSight(Vector3 target)
    {
        Vector3 from = transform.position + Vector3.down * 0.3f;
        return !Physics.Linecast(from, target, out var hit, occluders, QueryTriggerInteraction.Ignore)
               || hit.transform.IsChildOf(transform) || hit.transform == playerCamera.transform
               || playerCamera.transform.IsChildOf(hit.transform.root);
    }

    void Spot()
    {
        underTimer = 0;
        cooldownUntil = FeriMap.FeriSlow.Time + spotCooldown;
        spottedUntil = FeriMap.FeriSlow.Time + 2.5f;
        if (spotSound) spotSound.Play();
        if (hunter) hunter.SendMessage("TeleportBehindPlayer", SendMessageOptions.DontRequireReceiver);
        onPlayerSpotted?.Invoke();
    }
}
