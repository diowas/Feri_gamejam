// House Furniture - one-click setup (same idea as the Halloween Monsters pack).
// 1) Import settings are applied automatically to everything inside the "HouseFurniture" folder:
//    textures -> Point filter, no compression, no mip maps (crisp PS1 pixels)
//    models   -> normals imported from the file
// 2) Menu: Tools > House Furniture > Build Prefabs
//    Creates the materials (Built-in, URP or HDRP) and ready-to-use prefabs in HouseFurniture/Prefabs:
//    Bed, Table, Chair, CookingStove, Fireplace, Candle and DiningSet (table + 4 chairs + candle).
//    Each prefab gets a BoxCollider; the fireplace, stove and candle get a flickering light.
// This file must stay inside an "Editor" folder.
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class HouseFurnitureImport : AssetPostprocessor
{
    bool Ours => assetPath.Replace('\\', '/').Contains("/HouseFurniture/");

    void OnPreprocessTexture()
    {
        if (!Ours) return;
        var ti = (TextureImporter)assetImporter;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.wrapMode = TextureWrapMode.Clamp;
        if (assetPath.EndsWith("Fire_tex.png")) ti.alphaIsTransparency = true;
    }

    void OnPreprocessModel()
    {
        if (!Ours) return;
        var mi = (ModelImporter)assetImporter;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importAnimation = false;
        mi.isReadable = false;
    }
}

public static class HouseFurnitureSetup
{
    [MenuItem("Tools/House Furniture/Build Prefabs")]
    public static void Build()
    {
        string root = FindRoot();
        if (root == null)
        {
            EditorUtility.DisplayDialog("House Furniture", "Couldn't find the HouseFurniture folder. Keep this script at HouseFurniture/Editor/.", "OK");
            return;
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { root }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { root }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);

        string prefabs = root + "/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabs)) AssetDatabase.CreateFolder(root, "Prefabs");

        var mat = MakeMaterial(root);
        var fireMat = MakeFireMaterial(root);
        int made = 0;

        // ---- simple pieces
        foreach (var n in new[] { "Bed", "Table", "Chair" })
        {
            var go = Spawn(root, n, mat, fireMat);
            if (!go) continue;
            AddBox(go);
            made += Save(go, prefabs + "/" + n + ".prefab");
        }

        // ---- cooking stove: small glow in front of the firebox door
        var stove = Spawn(root, "CookingStove", mat, fireMat);
        if (stove)
        {
            AddBox(stove, "pipe");
            var r = Find(stove.transform, "stove");
            var b = r ? r.GetComponent<Renderer>().bounds : new Bounds(new Vector3(0, 0.45f, 0), Vector3.one * 0.9f);
            // firebox door sits on the front, left half in the OBJ (Unity mirrors X on import -> right half)
            var light = AddLight(stove, new Vector3(0.23f, 0.42f, b.max.z + 0.25f), 0.6f, 1.8f);
            var f = stove.AddComponent<HouseFireFlicker>();
            f.fireLight = light; f.baseIntensity = light.intensity; f.flicker = 0.5f; f.jitterPosition = false;
            made += Save(stove, prefabs + "/CookingStove.prefab");
        }

        // ---- fireplace: big warm light + flame flipbook
        var fp = Spawn(root, "Fireplace", mat, fireMat);
        if (fp)
        {
            AddBox(fp, "fire", "hearth");
            var light = AddLight(fp, new Vector3(0f, 0.5f, 0.85f), 2.5f, 7f);
            light.shadows = LightShadows.Soft;
            var f = fp.AddComponent<HouseFireFlicker>();
            f.fireLight = light; f.baseIntensity = light.intensity;
            var fr = Find(fp.transform, "fire");
            f.fireRenderer = fr ? fr.GetComponent<Renderer>() : null;
            made += Save(fp, prefabs + "/Fireplace.prefab");
        }

        // ---- candle
        var candle = Spawn(root, "Candle", mat, fireMat);
        if (candle)
        {
            AddBox(candle, "fire");
            var light = AddLight(candle, new Vector3(0f, 0.22f, 0f), 0.7f, 3f);
            var f = candle.AddComponent<HouseFireFlicker>();
            f.fireLight = light; f.baseIntensity = light.intensity; f.flicker = 0.25f; f.speed = 12f;
            var fr = Find(candle.transform, "fire");
            f.fireRenderer = fr ? fr.GetComponent<Renderer>() : null;
            f.fps = 12f;
            made += Save(candle, prefabs + "/Candle.prefab");
        }

        // ---- dining set (nested prefabs, so editing Chair.prefab updates all four)
        var tableP = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs + "/Table.prefab");
        var chairP = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs + "/Chair.prefab");
        var candleP = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs + "/Candle.prefab");
        if (tableP && chairP)
        {
            var set = new GameObject("DiningSet");
            Child(tableP, set, Vector3.zero, 0f);
            Child(chairP, set, new Vector3(-0.35f, 0f, -0.55f), 0f);
            Child(chairP, set, new Vector3(0.35f, 0f, -0.5f), 4f);
            Child(chairP, set, new Vector3(0.3f, 0f, 0.62f), 165f);     // pulled out, someone just got up...
            Child(chairP, set, new Vector3(-0.95f, 0f, 0.05f), 90f);
            if (candleP) Child(candleP, set, new Vector3(0.15f, 0.76f, 0.05f), 0f);
            made += Save(set, prefabs + "/DiningSet.prefab");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        var folder = AssetDatabase.LoadAssetAtPath<Object>(prefabs);
        if (folder) { EditorGUIUtility.PingObject(folder); Selection.activeObject = folder; }
        EditorUtility.DisplayDialog("House Furniture", made + " prefabs built in " + prefabs + ".\n\nDrag them into the house. The Fireplace's origin is its back edge, so snap it flat against a wall.", "Nice");
    }

    // ------------------------------------------------------------------ helpers
    static string FindRoot()
    {
        foreach (var guid in AssetDatabase.FindAssets("HouseFurnitureSetup t:MonoScript"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            string editorDir = Path.GetDirectoryName(p).Replace('\\', '/');
            return Path.GetDirectoryName(editorDir).Replace('\\', '/');
        }
        return null;
    }

    static string Pipeline()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return "BuiltIn";
        string n = rp.GetType().Name;
        return n.Contains("Universal") ? "URP" : n.Contains("HD") ? "HDRP" : "Other";
    }

    static Material LoadOrCreate(string path, Shader shader)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        else mat.shader = shader;
        return mat;
    }

    static Material MakeMaterial(string root)
    {
        string p = Pipeline();
        Shader shader = p == "URP" ? Shader.Find("Universal Render Pipeline/Lit")
                      : p == "HDRP" ? Shader.Find("HDRP/Lit")
                      : p == "Other" ? GraphicsSettings.currentRenderPipeline.defaultShader
                      : Shader.Find("Standard");
        var mat = LoadOrCreate(root + "/HouseFurniture_Mat.mat", shader);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/HouseFurniture_tex.png");
        var emi = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/HouseFurniture_emission.png");
        foreach (var n in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" }) if (mat.HasProperty(n)) mat.SetTexture(n, tex);
        foreach (var n in new[] { "_BaseColor", "_Color" }) if (mat.HasProperty(n)) mat.SetColor(n, Color.white);
        foreach (var n in new[] { "_Smoothness", "_Glossiness", "_Metallic" }) if (mat.HasProperty(n)) mat.SetFloat(n, 0f);
        if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0f);
        if (mat.HasProperty("_EnvironmentReflections")) mat.SetFloat("_EnvironmentReflections", 0f);
        if (emi != null)
        {
            Color glow = Color.white * 1.6f;     // stove grate slits + embers
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emi);
            if (mat.HasProperty("_EmissiveColorMap")) mat.SetTexture("_EmissiveColorMap", emi);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", glow);
            if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", glow);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material MakeFireMaterial(string root)
    {
        string p = Pipeline();
        Shader shader = p == "URP" ? Shader.Find("Universal Render Pipeline/Unlit")
                      : p == "HDRP" ? Shader.Find("HDRP/Unlit")
                      : Shader.Find("Unlit/Transparent Cutout");
        if (shader == null) shader = Shader.Find("Unlit/Transparent Cutout");
        var mat = LoadOrCreate(root + "/Fire_Mat.mat", shader);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(root + "/Fire_tex.png");
        foreach (var n in new[] { "_BaseMap", "_MainTex", "_UnlitColorMap" }) if (mat.HasProperty(n)) mat.SetTexture(n, tex);   // the flame cards' UVs already cover frame 0 (u 0..0.25)
        foreach (var n in new[] { "_BaseColor", "_Color", "_UnlitColor" }) if (mat.HasProperty(n)) mat.SetColor(n, Color.white);
        // alpha cutout, both faces
        if (mat.HasProperty("_AlphaClip")) { mat.SetFloat("_AlphaClip", 1f); mat.EnableKeyword("_ALPHATEST_ON"); }
        if (mat.HasProperty("_AlphaCutoffEnable")) { mat.SetFloat("_AlphaCutoffEnable", 1f); mat.EnableKeyword("_ALPHATEST_ON"); }
        if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.5f);
        if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", 0.5f);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
        if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = (int)RenderQueue.AlphaTest;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject Spawn(string root, string name, Material mat, Material fireMat)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(root + "/" + name + ".obj");
        if (model == null) { Debug.LogWarning("House Furniture: missing " + name + ".obj"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            bool isFire = r.name.Trim().ToLowerInvariant() == "fire";
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = isFire ? fireMat : mat;
            r.sharedMaterials = mats;
            if (isFire) r.shadowCastingMode = ShadowCastingMode.Off;
        }
        return go;
    }

    // one BoxCollider on the root around every part except the excluded ones
    static void AddBox(GameObject go, params string[] exclude)
    {
        bool any = false; var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.name.Trim().ToLowerInvariant();
            if (System.Array.IndexOf(exclude, n) >= 0 || n == "fire") continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return;
        var bc = go.AddComponent<BoxCollider>();
        bc.center = go.transform.InverseTransformPoint(b.center);
        bc.size = b.size;
    }

    static Light AddLight(GameObject parent, Vector3 localPos, float intensity, float range)
    {
        var lg = new GameObject("FireLight");
        lg.transform.SetParent(parent.transform, false);
        lg.transform.localPosition = localPos;
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.55f, 0.2f);
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    static void Child(GameObject prefab, GameObject parent, Vector3 pos, float yaw)
    {
        var c = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        c.transform.SetParent(parent.transform, false);
        c.transform.localPosition = pos;
        c.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    static Transform Find(Transform t, string name)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true))
            if (c.name.Trim().ToLowerInvariant() == name) return c;
        return null;
    }

    static int Save(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return 1;
    }
}
