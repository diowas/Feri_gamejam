// Player Body - first-person arms + legs (PSX First Person Arms + Universal Base Characters).
// 1) Import settings are applied automatically to everything inside the "PlayerBody" folder:
//    textures -> Point filter, no compression, no mip maps (crisp PS1 pixels)
//    models   -> Legacy rig, no imported materials, clips only from arms_rig.fbx
// 2) The prefabs are built automatically the first time the project opens (or Tools > Player Body > Build Prefabs):
//    PlayerBody/Resources/PlayerArms.prefab - arms lined up so the prefab root sits exactly on the camera
//    PlayerBody/Resources/PlayerLegs.prefab - full body standing on its root, upper body collapsed
//    FeriPlayerBody loads them at runtime, so nothing has to be placed in the scenes.
// This file must stay inside an "Editor" folder.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using FeriMap;

public class PlayerBodyImport : AssetPostprocessor
{
    bool Ours => assetPath.Replace('\\', '/').Contains("/PlayerBody/");

    void OnPreprocessTexture()
    {
        if (!Ours) return;
        var ti = (TextureImporter)assetImporter;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.maxTextureSize = assetPath.Contains("Superhero") ? 512 : 256;   // PS1-sized
    }

    void OnPreprocessModel()
    {
        if (!Ours) return;
        var mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importCameras = false;
        mi.importLights = false;
        mi.animationType = ModelImporterAnimationType.Legacy;
        mi.importAnimation = assetPath.Contains("arms_rig");
        mi.isReadable = assetPath.Contains("arms_rig");   // FeriPlayerBody measures the wrist from the mesh
    }
}

public static class PlayerBodySetup
{
    const float HandGap = 0.614f;     // hand.L <-> hand.R in the "relax" pose, metres (from arms_rig.glb)
    const float HandsBelow = 41.7f;   // in "relax" the wrists sit this many degrees below the rig camera's line of sight
    const float BodyHeight = 1.82f;   // the base character, top of head

    [MenuItem("Tools/Player Body/Build Prefabs")]
    public static void BuildMenu()
    {
        if (Build()) EditorUtility.DisplayDialog("Player Body", "PlayerArms and PlayerLegs prefabs built in PlayerBody/Resources.\nEvery FeriPlayer picks them up automatically.", "OK");
        else EditorUtility.DisplayDialog("Player Body", "Couldn't find arms_rig.fbx / Superhero_Male_FullBody.fbx. Keep this script at PlayerBody/Editor/.", "OK");
    }

    [InitializeOnLoadMethod]
    static void AutoBuild()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            string root = FindRoot();
            if (root != null && !File.Exists(root + "/Resources/PlayerArms.prefab")) Build();
        };
    }

    public static bool Build()
    {
        string root = FindRoot();
        if (root == null) return false;
        var armsFbx = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/arms_rig.fbx");
        var bodyFbx = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/Superhero_Male_FullBody.fbx");
        if (armsFbx == null || bodyFbx == null) return false;

        EnsureFolder(root, "Resources");
        EnsureFolder(root, "Materials");
        Shader ps1 = Shader.Find("FERI/PS1"), lit = LitShader();
        var armsTex = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/arms_01.png");
        var bodyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/T_Superhero_Male_Dark.png");

        BuildArms(root, armsFbx, MakeMat(root + "/Materials/Arms_PS1.mat", ps1, armsTex), MakeMat(root + "/Materials/Arms_Lit.mat", lit, armsTex));
        BuildLegs(root, bodyFbx, MakeMat(root + "/Materials/Body_PS1.mat", ps1, bodyTex), MakeMat(root + "/Materials/Body_Lit.mat", lit, bodyTex));
        AssetDatabase.SaveAssets();
        Debug.Log("Player Body: built PlayerArms + PlayerLegs in " + root + "/Resources");
        return true;
    }

    // ------------------------------------------------------------------ arms
    static void BuildArms(string root, GameObject fbx, Material ps1, Material lit)
    {
        var go = new GameObject("PlayerArms");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        model.transform.SetParent(go.transform, false);

        var relax = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(fbx)).OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == "relax" || c.name.EndsWith("|relax"));
        if (relax != null) relax.SampleAnimation(model, 0f);

        Transform cam = Find(model.transform, "camera"), hr = Find(model.transform, "hand.R"), hl = Find(model.transform, "hand.L");
        if (cam != null && hr != null && hl != null)
        {
            float gap = Vector3.Distance(hr.position, hl.position);
            if (gap > 1e-4f) model.transform.localScale *= HandGap / gap;

            // the rig's camera bone looks straight ahead; the relaxed hands hang HandsBelow under that line
            Vector3 c = cam.position, mid = (hr.position + hl.position) * 0.5f;
            Vector3 right = (hr.position - hl.position).normalized;
            Vector3 down = Vector3.ProjectOnPlane(mid - c, right).normalized;
            Vector3 fwd = Quaternion.AngleAxis(-HandsBelow, right) * down;
            Quaternion q = Quaternion.Inverse(Quaternion.LookRotation(fwd, Vector3.Cross(fwd, right)));
            model.transform.localPosition = q * (model.transform.localPosition - c);
            model.transform.localRotation = q * model.transform.localRotation;
        }
        else Debug.LogWarning("Player Body: arms_rig has no camera / hand.R / hand.L bone, arms are not lined up with the camera.");

        var anim = model.GetComponent<Animation>();
        if (anim != null) { anim.playAutomatically = false; anim.cullingType = AnimationCullingType.AlwaysAnimate; }

        SetupRenderers(model, lit);
        var part = go.AddComponent<FeriPlayerBodyPart>();
        part.ps1Material = ps1; part.litMaterial = lit;
        Save(go, root + "/Resources/PlayerArms.prefab");
    }

    // ------------------------------------------------------------------ legs
    static void BuildLegs(string root, GameObject fbx, Material ps1, Material lit)
    {
        var go = new GameObject("PlayerLegs");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        model.transform.SetParent(go.transform, false);
        Transform t = model.transform;

        // stand up (+Y), face +Z
        Transform pelvis = Find(t, "pelvis"), neck = Find(t, "neck_01"), foot = Find(t, "foot_l"), ball = Find(t, "ball_l");
        if (pelvis != null && neck != null && foot != null && ball != null)
        {
            Vector3 up = (neck.position - pelvis.position).normalized;
            Vector3 fwd = Vector3.ProjectOnPlane(ball.position - foot.position, up).normalized;
            Quaternion q = Quaternion.Inverse(Quaternion.LookRotation(fwd, up));
            t.localPosition = q * t.localPosition;
            t.localRotation = q * t.localRotation;
        }

        // only the body mesh (no eyes / eyebrows)
        var smrs = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var body = smrs.OrderByDescending(s => s.sharedMesh ? s.sharedMesh.vertexCount : 0).FirstOrDefault();
        foreach (var s in smrs) if (s != body) s.enabled = false;

        // BodyHeight tall, feet on the root, pelvis above it
        if (body != null)
        {
            Bounds b = body.bounds;
            if (b.size.y > 0.01f) t.localScale *= BodyHeight / b.size.y;
            b = body.bounds;
            Vector3 centre = pelvis != null ? pelvis.position : b.center;
            t.localPosition -= new Vector3(centre.x, b.min.y, centre.z);
        }

        // chest, arms and head shrink into the spine_02 joint, so the camera never sees them from inside
        var chest = Find(t, "spine_02");
        if (chest != null) chest.localScale = Vector3.one * 0.0001f;

        SetupRenderers(model, lit);
        var part = go.AddComponent<FeriPlayerBodyPart>();
        part.ps1Material = ps1; part.litMaterial = lit;
        Save(go, root + "/Resources/PlayerLegs.prefab");
    }

    // ------------------------------------------------------------------ helpers
    static void SetupRenderers(GameObject model, Material mat)
    {
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (r is SkinnedMeshRenderer s) s.updateWhenOffscreen = true;
        }
    }

    static Material MakeMat(string path, Shader shader, Texture tex)
    {
        if (shader == null) return null;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        else mat.shader = shader;
        foreach (var n in new[] { "_MainTex", "_BaseMap" }) if (mat.HasProperty(n)) mat.SetTexture(n, tex);
        foreach (var n in new[] { "_Color", "_BaseColor" }) if (mat.HasProperty(n)) mat.SetColor(n, Color.white);
        foreach (var n in new[] { "_Smoothness", "_Glossiness", "_Metallic" }) if (mat.HasProperty(n)) mat.SetFloat(n, 0f);
        if (mat.HasProperty("_UseVertexColor")) mat.SetFloat("_UseVertexColor", 0f);   // skinned, no baked vertex light
        if (mat.HasProperty("_Affine")) mat.SetFloat("_Affine", 0.4f);                  // right in front of the lens, full warp is too much
        if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
        if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Shader LitShader()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        Shader s = null;
        if (rp != null && rp.GetType().Name.Contains("Universal")) s = Shader.Find("Universal Render Pipeline/Lit");
        else if (rp != null && rp.GetType().Name.Contains("HD")) s = Shader.Find("HDRP/Lit");
        return s != null ? s : Shader.Find("Standard");
    }

    static void Save(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = Find(c, name); if (f != null) return f; }
        return null;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    static string FindRoot()
    {
        foreach (var guid in AssetDatabase.FindAssets("arms_rig"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith("/arms_rig.fbx") && p.Contains("/PlayerBody/")) return Path.GetDirectoryName(p).Replace('\\', '/');
        }
        return null;
    }
}
