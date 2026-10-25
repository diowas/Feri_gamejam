// JUMPSCARE DIRECTOR - every so often the Trick-or-Treater's or NullRef's face flashes right in front of you.
// It never hurts you, but every scare leaves the building a little darker (PS1Lighting exposure), and that stays
// until you quit - dying (scene reload) keeps it, like the blackout does.
// Scares are rare on purpose: exactly one per `window` seconds, at a random moment inside it, never closer than
// `minGap` to the last one, and never while a monster is chasing or killing you.
//
// Setup: none. It adds itself to any scene with PS1Lighting and a Trick-or-Treater or NullRef in it, and clones
// their models for the faces. Drop it on an object yourself to change the settings or give it face prefabs.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class JumpscareDirector : MonoBehaviour
{
    [Header("Timing")]
    public float firstDelay = 90f;                 // calm period after spawning
    public float window = 180f;                    // one scare somewhere inside every window
    public float minGap = 75f;

    [Header("Faces")]
    public GameObject trickOrTreaterFace, nullRefFace;   // optional prefabs; empty -> clone the monsters in the scene
    public float faceDistance = 0.75f;             // metres in front of the camera
    public float showTime = 0.45f;
    public float faceBrightness = 0.8f;            // faces light themselves, so they show up in the blackout too
    public AudioClip[] screams;
    [Range(0, 1)] public float screamVolume = 1f;

    [Header("Darkening")]
    [Range(0.5f, 1f)] public float darkenPerScare = 0.88f;   // exposure is multiplied by this after every scare
    [Range(0.1f, 1f)] public float darkest = 0.45f;          // never below this fraction of the map's own exposure

    static int scares;                             // survives scene reloads

    class Face { public GameObject go; public Renderer head; }
    readonly List<Face> faces = new List<Face>();
    readonly Dictionary<FeriMap.PS1Lighting, float> baseExposure = new Dictionary<FeriMap.PS1Lighting, float>();
    MaterialPropertyBlock mpb;
    float windowStart, nextScare, lastScare = -999f, darkness = 1f;
    bool scaring;

    void Start()
    {
        mpb = new MaterialPropertyBlock();
        AddFace(trickOrTreaterFace ? trickOrTreaterFace : FindAnyObjectByType<TrickOrTreater>()?.gameObject);
        AddFace(nullRefFace ? nullRefFace : FindAnyObjectByType<NullRefEntity>()?.gameObject);
        foreach (var l in FindObjectsByType<FeriMap.PS1Lighting>(FindObjectsSortMode.None)) baseExposure[l] = l.exposure;
        darkness = Target();                       // already dark from earlier scares
        ApplyDarkness();
        windowStart = Time.time + firstDelay;
        nextScare = windowStart + Random.Range(0f, window);
    }

    void Update()
    {
        darkness = Mathf.MoveTowards(darkness, Target(), Time.deltaTime * 0.08f);   // eases darker over a few seconds
        ApplyDarkness();

        if (scaring || faces.Count == 0 || Time.time < nextScare) return;
        if (MonsterBusy()) { nextScare = Time.time + 15f; return; }                  // don't pile on while you're running
        StartCoroutine(Scare());
        lastScare = Time.time;
        windowStart += window;
        if (windowStart < Time.time) windowStart = Time.time;
        nextScare = Mathf.Max(windowStart + Random.Range(0f, window), lastScare + minGap);
    }

    float Target() => Mathf.Max(darkest, Mathf.Pow(darkenPerScare, scares));

    void ApplyDarkness()
    {
        foreach (var kv in baseExposure) if (kv.Key) kv.Key.exposure = kv.Value * darkness;
    }

    void OnDestroy()   // play mode only, but don't leave the map dark for the next scene's director to read as "normal"
    {
        foreach (var kv in baseExposure) if (kv.Key) kv.Key.exposure = kv.Value;
    }

    static bool MonsterBusy()
    {
        foreach (var t in FindObjectsByType<TrickOrTreater>(FindObjectsSortMode.None))
            if (t.CurrentState == TrickOrTreater.State.Chase || t.CurrentState == TrickOrTreater.State.Kill) return true;
        foreach (var n in FindObjectsByType<NullRefEntity>(FindObjectsSortMode.None))
            if (n.HasCaught) return true;
        return false;
    }

    // a copy with only its meshes left: built under an inactive parent so none of the monster's scripts ever wake up
    void AddFace(GameObject source)
    {
        if (!source) return;
        var holder = new GameObject("JumpscareFace");
        holder.transform.SetParent(transform, false);
        holder.SetActive(false);
        var go = Instantiate(source, holder.transform);
        foreach (var b in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(b);
        foreach (var c in go.GetComponentsInChildren<Component>(true))
            if (!(c is Transform || c is MeshFilter || c is Renderer)) DestroyImmediate(c);
        go.SetActive(false);
        holder.SetActive(true);

        var face = new Face { go = go };
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            MakeSelfLit(r, mpb, faceBrightness);
            if (!face.head && r.name.ToLowerInvariant().Contains("head")) face.head = r;
        }
        faces.Add(face);
    }

    IEnumerator Scare()
    {
        scaring = true;
        Camera camera = Camera.main;
        if (!camera) { scaring = false; yield break; }
        Transform cam = camera.transform;
        Face face = faces[Random.Range(0, faces.Count)];

        float d = faceDistance;                    // don't push the face into a wall you're standing at
        if (Physics.Raycast(cam.position, cam.forward, out var hit, faceDistance + 0.2f, ~0, QueryTriggerInteraction.Ignore))
            d = Mathf.Max(0.35f, hit.distance - 0.15f);
        Vector3 flat = cam.forward; flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) flat = cam.up;
        Transform t = face.go.transform;
        t.rotation = Quaternion.LookRotation(-flat.normalized);   // models face +Z: turn it towards you
        t.position = Vector3.zero;
        face.go.SetActive(true);
        Vector3 anchor = face.head ? face.head.bounds.center : t.position + Vector3.up * 2.4f;
        Vector3 basePos = t.position + (cam.position + cam.forward * d - anchor);

        if (screams != null && screams.Length > 0)
        {
            var clip = screams[Random.Range(0, screams.Length)];
            if (clip) AudioSource.PlayClipAtPoint(clip, cam.position, screamVolume);
        }
        float fov = camera.fieldOfView;
        for (float s = 0f; s < showTime; s += Time.deltaTime)
        {
            t.position = basePos + Random.insideUnitSphere * 0.02f;   // twitching, like the PS1 vertex wobble
            camera.fieldOfView = fov - 8f * (1f - s / showTime);      // a punch in, easing back
            yield return null;
        }
        camera.fieldOfView = fov;
        face.go.SetActive(false);
        scares++;
        scaring = false;
    }

    // light a renderer through the emission map with its own texture: the blackout turns off every other kind of light
    // FeriEnding calls this before a new game starts, so the next run isn't born dark
    public static void ResetDarkness() { scares = 0; }

    public static void MakeSelfLit(Renderer r, MaterialPropertyBlock mpb, float brightness)
    {
        r.GetPropertyBlock(mpb);
        var tex = r.sharedMaterial ? r.sharedMaterial.mainTexture : null;
        if (tex) mpb.SetTexture("_EmissionMap", tex);
        mpb.SetColor("_EmissionColor", Color.white * brightness);
        r.SetPropertyBlock(mpb);
    }

    // ---------------------------------------------------------------- auto-setup
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ResetDarkness(); SceneManager.sceneLoaded -= OnSceneLoaded; }

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
        if (FindAnyObjectByType<JumpscareDirector>() != null) return;
        if (FindAnyObjectByType<FeriMap.PS1Lighting>() == null) return;          // only the FERI building
        if (FindAnyObjectByType<TrickOrTreater>() == null && FindAnyObjectByType<NullRefEntity>() == null) return;
        new GameObject("JumpscareDirector").AddComponent<JumpscareDirector>();
    }
}
