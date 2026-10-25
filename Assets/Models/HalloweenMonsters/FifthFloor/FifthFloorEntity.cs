// THE FIFTH FLOOR - the last thing in the building, waiting on the floor that isn't on any sign.
// 2.9 m of black: coat-hanger-thin limbs, knees bending backwards, hunched under its own ceiling with its head
// hanging lower than its shoulders. It is black on black, so the only things you ever see are two pale eyes
// (one a little higher than the other) and a grin far too wide for its face.
// Built and animated in code (rigid segments, PS1 style, posed at 12 fps), so there is nothing to import.
// FeriEnding spawns it behind the elevator doors on floor 5. It also works dropped on any empty object.
using System.Collections.Generic;
using UnityEngine;

public class FifthFloorEntity : MonoBehaviour
{
    public Shader shader;                          // empty -> FERI/PS1
    public Transform lookTarget;                   // its head follows this (usually the player's camera)
    [Range(0, 1)] public float grin;               // 0 = thin smile, 1 = splits past its cheeks
    [Range(0, 1)] public float reach;              // 0 = arms hanging, 1 = one hand on reachTarget
    public Vector3 reachTarget, reachElbow;        // world space; the elbow lets the arm bend through a doorway
    public int reachSide = 1;                      // +1 = the arm on its local +X, -1 = the other one
    public float poseFps = 12f;

    [Header("Glow")]
    public Color eyeColor = new Color(1.05f, 1.1f, 1.25f);
    public Color teethColor = new Color(0.92f, 0.86f, 0.7f);
    public Color mouthColor = new Color(0.3f, 0.01f, 0.02f);

    public Vector3 FacePoint => head ? head.TransformPoint(0f, 0f, 0.1f) : transform.position + Vector3.up * 2f;

    // ---- skeleton (local space, faces +Z, feet at 0)
    const int HIP = 0, CHEST = 1, NECK = 2, SH_L = 3, EL_L = 4, HA_L = 5, SH_R = 6, EL_R = 7, HA_R = 8,
              HIP_L = 9, KN_L = 10, FT_L = 11, HIP_R = 12, KN_R = 13, FT_R = 14;
    static readonly Vector3[] Rest = {
        new Vector3(0f, 1.55f, 0f), new Vector3(0f, 2.3f, 0.32f), new Vector3(0f, 2.12f, 0.6f),
        new Vector3(0.19f, 2.27f, 0.3f), new Vector3(0.34f, 1.3f, 0.45f), new Vector3(0.36f, 0.3f, 0.7f),
        new Vector3(-0.19f, 2.27f, 0.3f), new Vector3(-0.34f, 1.3f, 0.45f), new Vector3(-0.36f, 0.3f, 0.7f),
        new Vector3(0.08f, 1.55f, 0f), new Vector3(0.12f, 0.82f, -0.14f), new Vector3(0.15f, 0.02f, 0.05f),
        new Vector3(-0.08f, 1.55f, 0f), new Vector3(-0.12f, 0.82f, -0.14f), new Vector3(-0.15f, 0.02f, 0.05f) };

    class Seg { public Transform t; public int a, b; public float w, d; }
    readonly List<Seg> segs = new List<Seg>();
    readonly Vector3[] pos = new Vector3[Rest.Length];
    readonly Transform[] fingers = new Transform[8];
    Transform head, mouth;
    readonly List<Transform> eyes = new List<Transform>();
    Material black;
    float poseTimer, roll = 14f, nextTwitch, blinkUntil, nextBlink = 3f;
    bool built;

    void Awake() { Build(); }

    public void Build()
    {
        if (built) return;
        if (!shader) shader = Shader.Find("FERI/PS1");
        if (!shader) { Debug.LogError("FifthFloorEntity: no FERI/PS1 shader", this); return; }
        built = true;
        black = Mat(Color.black, false);

        AddSeg(HIP, CHEST, 0.16f, 0.3f, 0.09f, 0.11f);
        AddSeg(CHEST, NECK, 0.05f, 0.04f, 0.05f, 0.04f);
        AddSeg(SH_L, SH_R, 0.04f, 0.04f, 0.04f, 0.04f);
        AddSeg(SH_L, EL_L, 0.045f, 0.03f, 0.045f, 0.03f);
        AddSeg(EL_L, HA_L, 0.03f, 0.02f, 0.03f, 0.02f);
        AddSeg(SH_R, EL_R, 0.045f, 0.03f, 0.045f, 0.03f);
        AddSeg(EL_R, HA_R, 0.03f, 0.02f, 0.03f, 0.02f);
        AddSeg(HIP_L, HIP_R, 0.05f, 0.05f, 0.05f, 0.05f);
        AddSeg(HIP_L, KN_L, 0.06f, 0.04f, 0.06f, 0.04f);
        AddSeg(KN_L, FT_L, 0.04f, 0.02f, 0.04f, 0.02f);
        AddSeg(HIP_R, KN_R, 0.06f, 0.04f, 0.06f, 0.04f);
        AddSeg(KN_R, FT_R, 0.04f, 0.02f, 0.04f, 0.02f);
        var finger = Taper(0.6f, 0.6f);
        for (int i = 0; i < fingers.Length; i++) fingers[i] = Part("Finger", transform, finger, black);

        // the head: long, narrow, hanging forward; everything you can actually see lives on it
        head = new GameObject("Head").transform;
        head.SetParent(transform, false);
        BoxPart(head, Vector3.zero, new Vector3(0.19f, 0.3f, 0.2f), black);
        var eyeMat = Mat(eyeColor, true);
        eyes.Add(BoxPart(head, new Vector3(0.048f, 0.046f, 0.1f), new Vector3(0.036f, 0.016f, 0.008f), eyeMat));
        eyes.Add(BoxPart(head, new Vector3(-0.05f, 0.028f, 0.1f), new Vector3(0.036f, 0.016f, 0.008f), eyeMat));
        BoxPart(eyes[0], new Vector3(0f, 0f, 1.15f), new Vector3(0.22f, 0.7f, 0.3f), black);   // pinpoint pupils, just in front
        BoxPart(eyes[1], new Vector3(0.1f, 0f, 1.15f), new Vector3(0.22f, 0.7f, 0.3f), black);

        mouth = new GameObject("Mouth").transform;
        mouth.SetParent(head, false);
        mouth.localPosition = new Vector3(0f, -0.075f, 0.101f);
        var teeth = Mat(teethColor, true);
        var gums = Mat(mouthColor, true);
        var rng = new System.Random(5);
        const int N = 15;
        for (int i = 0; i < N; i++)
        {
            float u = i / (N - 1f) * 2f - 1f;
            float x = u * 0.11f, y = 0.024f * u * u;                                  // the corners curl up
            float hTop = 0.014f + (float)rng.NextDouble() * 0.014f, hBot = 0.012f + (float)rng.NextDouble() * 0.012f;
            BoxPart(mouth, new Vector3(x, y - 0.001f, -0.004f), new Vector3(0.016f, 0.022f, 0.003f), gums);
            BoxPart(mouth, new Vector3(x - 0.003f, y + 0.009f - hTop * 0.5f, 0f), new Vector3(0.008f, hTop, 0.005f), teeth);
            BoxPart(mouth, new Vector3(x + 0.003f, y - 0.011f + hBot * 0.5f, 0f), new Vector3(0.008f, hBot, 0.005f), teeth);
        }
        Pose(0f);
    }

    void Update()
    {
        if (!built) return;
        poseTimer += Time.deltaTime;
        if (poseTimer < 1f / poseFps) return;
        Pose(poseTimer);
        poseTimer = 0f;
    }

    void Pose(float dt)
    {
        float t = Time.time;
        for (int i = 0; i < Rest.Length; i++) pos[i] = Rest[i];
        // slow, deep breathing and a sway it never quite stops
        Vector3 sway = new Vector3(Mathf.Sin(t * 0.7f) * 0.02f, Mathf.Sin(t * 1.1f) * 0.012f, 0f);
        for (int i = CHEST; i <= HA_R; i++) pos[i] += sway * (i == HA_L || i == HA_R ? 0.3f : 1f);
        // the arm that reaches: straight lines from the hanging pose to the target, bent through reachElbow
        if (reach > 0f)
        {
            int el = reachSide >= 0 ? EL_L : EL_R, ha = reachSide >= 0 ? HA_L : HA_R;
            pos[el] = Vector3.Lerp(pos[el], transform.InverseTransformPoint(reachElbow), reach);
            pos[ha] = Vector3.Lerp(pos[ha], transform.InverseTransformPoint(reachTarget), reach);
        }

        foreach (var s in segs) Place(s.t, pos[s.a], pos[s.b], s.w, s.d);
        Fingers(0, pos[EL_L], pos[HA_L]);
        Fingers(4, pos[EL_R], pos[HA_R]);

        // head: hangs off the neck and turns to watch you, rolled at a wrong angle that twitches now and then
        if (Time.time > nextTwitch) { roll = Random.Range(-28f, 28f); nextTwitch = Time.time + Random.Range(0.6f, 3f); }
        Vector3 neckDir = (pos[NECK] - pos[CHEST]).normalized;
        head.localPosition = pos[NECK] + neckDir * 0.08f + new Vector3(0f, -0.1f, 0.05f);
        Quaternion rest = Quaternion.Euler(14f, 0f, 0f);
        Quaternion look = rest;
        if (lookTarget)
        {
            Vector3 d = transform.InverseTransformDirection(lookTarget.position - transform.TransformPoint(head.localPosition));
            if (d.sqrMagnitude > 0.01f) look = Quaternion.Slerp(rest, Quaternion.LookRotation(d), 0.8f);
        }
        head.localRotation = look * Quaternion.Euler(0f, 0f, roll);
        mouth.localScale = new Vector3(Mathf.Lerp(0.8f, 1.45f, grin), Mathf.Lerp(0.8f, 1.15f, grin), 1f);

        // blinks: rare, and both eyes don't always shut together
        if (Time.time > nextBlink) { blinkUntil = Time.time + 0.12f; nextBlink = Time.time + Random.Range(2.5f, 7f); }
        bool blink = Time.time < blinkUntil;
        for (int i = 0; i < eyes.Count; i++)
        {
            Vector3 s = eyes[i].localScale;
            eyes[i].localScale = new Vector3(s.x, blink && (i == 0 || Random.value < 0.7f) ? 0.002f : 0.016f, s.z);
        }
    }

    void Fingers(int first, Vector3 elbow, Vector3 hand)
    {
        Vector3 dir = (hand - elbow).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        side.Normalize();
        for (int i = 0; i < 4; i++)
        {
            float spread = (i - 1.5f) * 9f;
            Vector3 fd = Quaternion.AngleAxis(spread, Vector3.Cross(side, dir)) * Quaternion.AngleAxis(18f, side) * dir;   // long, curled
            Place(fingers[first + i], hand, hand + fd * (0.3f + 0.04f * (i == 1 || i == 2 ? 1f : 0f)), 0.014f, 0.014f);
        }
    }

    // stretches a unit segment mesh from a to b (local space)
    void Place(Transform t, Vector3 a, Vector3 b, float w, float d)
    {
        Vector3 v = b - a; float len = v.magnitude;
        if (len < 1e-4f) { t.localScale = Vector3.zero; return; }
        Vector3 dir = v / len;
        t.localPosition = a;
        t.localRotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
        t.localScale = new Vector3(w, d, len);
    }

    void AddSeg(int a, int b, float w0, float w1, float d0, float d1)
    {
        var t = Part("Bone", transform, Taper(w1 / w0, d1 / d0), black);
        segs.Add(new Seg { t = t, a = a, b = b, w = w0, d = d0 });
    }

    Transform Part(string n, Transform parent, Mesh mesh, Material m)
    {
        var go = new GameObject(n);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    static Mesh unitBox;
    Transform BoxPart(Transform parent, Vector3 center, Vector3 size, Material m)
    {
        if (!unitBox) unitBox = Taper(1f, 1f);
        var t = Part("Box", parent, unitBox, m);
        t.localPosition = center - new Vector3(0f, 0f, size.z * 0.5f);
        t.localScale = size;
        return t;
    }

    Material Mat(Color c, bool glow)
    {
        var m = new Material(shader);
        m.SetTexture("_MainTex", Texture2D.whiteTexture);
        m.SetColor("_Color", Color.black);
        m.SetFloat("_UseVertexColor", 0f);
        m.SetFloat("_Emission", 0f);
        m.SetFloat("_Cull", 0f);
        // the emission map is the one kind of light the blackout can't turn off
        m.SetTexture("_EmissionMap", Texture2D.whiteTexture);
        m.SetColor("_EmissionColor", glow ? c : Color.black);
        return m;
    }

    // a box from z = 0 to z = 1, 1 x 1 at the start and rx x ry at the end
    static Mesh Taper(float rx, float ry)
    {
        Vector3[] c = {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(-0.5f * rx, -0.5f * ry, 1f), new Vector3(0.5f * rx, -0.5f * ry, 1f),
            new Vector3(0.5f * rx, 0.5f * ry, 1f), new Vector3(-0.5f * rx, 0.5f * ry, 1f) };
        int[,] faces = { { 0, 3, 2, 1 }, { 4, 5, 6, 7 }, { 0, 1, 5, 4 }, { 1, 2, 6, 5 }, { 2, 3, 7, 6 }, { 3, 0, 4, 7 } };
        var v = new Vector3[24]; var uv = new Vector2[24]; var tris = new int[36];
        for (int f = 0; f < 6; f++)
        {
            for (int k = 0; k < 4; k++) { v[f * 4 + k] = c[faces[f, k]]; uv[f * 4 + k] = new Vector2(k == 1 || k == 2 ? 1 : 0, k >= 2 ? 1 : 0); }
            int o = f * 4, ti = f * 6;
            tris[ti] = o; tris[ti + 1] = o + 1; tris[ti + 2] = o + 2; tris[ti + 3] = o; tris[ti + 4] = o + 2; tris[ti + 5] = o + 3;
        }
        var m = new Mesh { name = "Taper" };
        m.vertices = v; m.uv = uv; m.triangles = tris;
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }
}
