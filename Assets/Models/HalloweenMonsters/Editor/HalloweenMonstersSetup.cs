// Halloween Monsters - one-click setup.
// 1) Import settings are applied automatically to everything inside the "HalloweenMonsters" folder:
//    textures -> Point filter, no compression, no mip maps (crisp PS1 pixels)
//    models   -> Read/Write on, normals imported from the file
// 2) Menu: Tools > Halloween Monsters > Build Prefabs
//    Creates a material for each monster (texture + glowing emission, for Built-in, URP or HDRP)
//    and ready-to-use prefabs in HalloweenMonsters/Prefabs with the scripts already attached.
// This file must stay inside an "Editor" folder.
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public class HalloweenMonstersImport : AssetPostprocessor
{
    bool Ours => assetPath.Replace('\\', '/').Contains("/HalloweenMonsters/");

    void OnPreprocessTexture()
    {
        if (!Ours) return;
        var ti = (TextureImporter)assetImporter;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.wrapMode = TextureWrapMode.Clamp;
    }

    void OnPreprocessModel()
    {
        if (!Ours) return;
        var mi = (ModelImporter)assetImporter;
        mi.isReadable = true;                                  // NULLREF deforms its mesh at runtime
        mi.importNormals = ModelImporterNormals.Import;        // keep the smooth normals from the file
        mi.importAnimation = false;
    }
}

public static class HalloweenMonstersSetup
{
    const int IgnoreRaycastLayer = 2;

    [MenuItem("Tools/Halloween Monsters/Build Prefabs")]
    public static void Build()
    {
        string root = FindRoot();
        if (root == null)
        {
            EditorUtility.DisplayDialog("Halloween Monsters", "Couldn't find the HalloweenMonsters folder. Keep this script at HalloweenMonsters/Editor/.", "OK");
            return;
        }
        // make sure the import settings above are applied, even if the files were imported before this script existed
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { root }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { root }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);

        string prefabs = root + "/Prefabs";
        if (!AssetDatabase.IsValidFolder(prefabs)) AssetDatabase.CreateFolder(root, "Prefabs");
        int made = 0;

        // ---- NULLREF (pumpkin scarecrow)
        var nullRefMat = MakeMaterial(root + "/NullRef", "NullRef_Mat", "NullRef_tex.png", "NullRef_emission.png", new Color(1f, 0.45f, 0.08f));
        var go = Spawn(root + "/NullRef/NullRef.obj", nullRefMat, "NullRef");
        if (go)
        {
            var e = go.AddComponent<NullRefEntity>();
            var head = FindChild(go.transform, "head");
            var lightGo = new GameObject("PumpkinLight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.position = head ? head.GetComponent<Renderer>().bounds.center : go.transform.position + Vector3.up * 2.48f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.5f, 0.1f); light.range = 3f; light.intensity = 1.5f; light.shadows = LightShadows.None;
            e.headGlow = light;
            e.glowRenderer = head ? head.GetComponent<Renderer>() : null;
            SetLayer(go, IgnoreRaycastLayer);
            made += Save(go, prefabs + "/NullRef.prefab");
        }

        // ---- CLIPPING (ceiling eye mass)
        var clipMat = MakeMaterial(root + "/Clipping", "Clipping_Mat", "Clipping_tex.png", "Clipping_emission.png", Color.white);
        go = Spawn(root + "/Clipping/Clipping.obj", clipMat, "CeilingWatcher");
        if (go)
        {
            go.AddComponent<CeilingWatcher>();
            SetLayer(go, IgnoreRaycastLayer);
            made += Save(go, prefabs + "/CeilingWatcher.prefab");
        }

        // ---- FLOATING EYES (a staring pair, and a loose swarm)
        var eyeMat = MakeMaterial(root + "/FloatingEyes", "FloatingEyes_Mat", "FloatingEyes_tex.png", "FloatingEyes_emission.png", Color.white);
        string eyes = root + "/FloatingEyes/";
        var pair = new GameObject("FloatingEyes_Pair");
        AddEye(pair, eyes + "FloatingEye_DoublePupil.obj", eyeMat, new Vector3(-0.2f, 0f, 0f));
        AddEye(pair, eyes + "FloatingEye_DoublePupil.obj", eyeMat, new Vector3(0.2f, 0.04f, 0.03f));
        pair.AddComponent<FloatingEyes>();
        SetLayer(pair, IgnoreRaycastLayer);
        made += Save(pair, prefabs + "/FloatingEyes_Pair.prefab");

        var swarm = new GameObject("FloatingEyes_Swarm");
        AddEye(swarm, eyes + "FloatingEye_Human.obj", eyeMat, new Vector3(-0.6f, 0.1f, 0.2f));
        AddEye(swarm, eyes + "FloatingEye_Goat.obj", eyeMat, new Vector3(-0.15f, -0.15f, -0.3f));
        AddEye(swarm, eyes + "FloatingEye_Red.obj", eyeMat, new Vector3(0.3f, 0.12f, 0.1f));
        AddEye(swarm, eyes + "FloatingEye_DoublePupil.obj", eyeMat, new Vector3(0.7f, -0.05f, -0.2f));
        AddEye(swarm, eyes + "FloatingEye_Goat.obj", eyeMat, new Vector3(0.1f, 0.25f, -0.5f));
        swarm.AddComponent<FloatingEyes>();
        SetLayer(swarm, IgnoreRaycastLayer);
        made += Save(swarm, prefabs + "/FloatingEyes_Swarm.prefab");

        // ---- TRICK-OR-TREATER (main killer)
        var totMat = MakeMaterial(root + "/TrickOrTreater", "TrickOrTreater_Mat", "TrickOrTreater_tex.png", "TrickOrTreater_emission.png", Color.white);
        go = Spawn(root + "/TrickOrTreater/TrickOrTreater.obj", totMat, "TrickOrTreater");
        if (go)
        {
            go.AddComponent<TrickOrTreater>();                       // also adds a NavMeshAgent
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent)
            {
                agent.radius = 0.55f; agent.height = 3.4f; agent.speed = 1.3f;
                agent.angularSpeed = 260f; agent.acceleration = 14f; agent.stoppingDistance = 0.3f;
            }
            SetLayer(go, IgnoreRaycastLayer);
            made += Save(go, prefabs + "/TrickOrTreater.prefab");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        var folder = AssetDatabase.LoadAssetAtPath<Object>(prefabs);
        if (folder) { EditorGUIUtility.PingObject(folder); Selection.activeObject = folder; }
        EditorUtility.DisplayDialog("Halloween Monsters", made + " prefabs built in " + prefabs + ".\n\nDrag them into your scene. The Trick-or-Treater needs a baked NavMesh.", "Nice");
    }

    // ------------------------------------------------------------------ helpers
    static string FindRoot()
    {
        foreach (var guid in AssetDatabase.FindAssets("HalloweenMonstersSetup t:MonoScript"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');   // .../HalloweenMonsters/Editor/HalloweenMonstersSetup.cs
            string editorDir = Path.GetDirectoryName(p).Replace('\\', '/');
            return Path.GetDirectoryName(editorDir).Replace('\\', '/');
        }
        return null;
    }

    static Shader PickShader()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        Shader s = null;
        if (rp == null) s = Shader.Find("Standard");
        else
        {
            string n = rp.GetType().Name;
            if (n.Contains("Universal")) s = Shader.Find("Universal Render Pipeline/Lit");
            else if (n.Contains("HD")) s = Shader.Find("HDRP/Lit");
            if (s == null) s = rp.defaultShader;
        }
        return s != null ? s : Shader.Find("Standard");
    }

    static Material MakeMaterial(string folder, string name, string texFile, string emissionFile, Color emission)
    {
        string path = folder + "/" + name + ".mat";
        var shader = PickShader();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        else mat.shader = shader;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + texFile);
        var emi = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + emissionFile);
        foreach (var p in new[] { "_BaseMap", "_MainTex", "_BaseColorMap" }) if (mat.HasProperty(p)) mat.SetTexture(p, tex);
        foreach (var p in new[] { "_BaseColor", "_Color" }) if (mat.HasProperty(p)) mat.SetColor(p, Color.white);
        foreach (var p in new[] { "_Smoothness", "_Glossiness", "_Metallic" }) if (mat.HasProperty(p)) mat.SetFloat(p, 0f);
        if (emi != null)
        {
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", emi);
            if (mat.HasProperty("_EmissiveColorMap")) mat.SetTexture("_EmissiveColorMap", emi);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission);
            if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", emission);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);              // URP: Render Face = Both
        if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);      // HDRP: double sided
        if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject Spawn(string objPath, Material mat, string name)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(objPath);
        if (model == null) { Debug.LogWarning("Halloween Monsters: missing " + objPath); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = name;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
        }
        return go;
    }

    static void AddEye(GameObject parent, string objPath, Material mat, Vector3 localPos)
    {
        var eye = Spawn(objPath, mat, Path.GetFileNameWithoutExtension(objPath));
        if (eye == null) return;
        eye.transform.SetParent(parent.transform, false);
        eye.transform.localPosition = localPos;
    }

    static Transform FindChild(Transform t, string name)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true))
            if (c.name.Trim().ToLowerInvariant() == name) return c;
        return null;
    }

    static void SetLayer(GameObject go, int layer)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    static int Save(GameObject go, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return 1;
    }
}
