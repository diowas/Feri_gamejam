using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FeriMap.EditorTools
{

    public static class FeriMapBuilder
    {
        public const string RootName = "FERI_Map";
        const float CellSize = 1.25f;
        const int GlassLayer = 2;

        [Serializable] public class LAtlas { public string name; public float[] rect; }
        [Serializable] public class LMat { public string name; public string tex; public float[] tile; public float[] color; public string mode; public float emissive; public string cull; public bool baked; }
        [Serializable] public class LBox { public string g; public float[] c; public float[] s; public float[] r; public string m; public int col; public string uv; public float[] rect; public int li; public string tag; }
        [Serializable] public class LCyl { public string g; public float[] c; public float r; public float h; public int seg; public string m; public int col; }
        [Serializable] public class LLight { public float[] p; public float[] col; public float i; public float range; public float flicker; public bool bake; }
        [Serializable] public class LFloor { public string label; public float y; public bool enabled; }
        [Serializable] public class LElev { public string name; public float[] shaft; public string doorSide; public float doorCenter; public LFloor[] floors; public string start; }
        [Serializable] public class LProp { public string model; public string tex; public float[] p; public float yaw; public float h; public float li; public float[] lcol; public float lrng; public float lfl; public float ly; public int col; public float rx; }
        [Serializable] public class LSpawn { public float[] p; public float yaw; }
        [Serializable] public class LSettings { public float[] ambient; public float[] fogColor; public float fogStart; public float fogEnd; }
        [Serializable]
        public class Layout
        {
            public int version; public LAtlas[] atlas; public LMat[] materials; public LBox[] boxes; public LCyl[] cyls;
            public LLight[] lights; public LElev[] elevators; public LProp[] props; public LSpawn spawn; public LSettings settings;
        }

        class Acc
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
        }

        class Ctx
        {
            public string baseDir;
            public Layout L;
            public Dictionary<string, Material> mats = new Dictionary<string, Material>();
            public Dictionary<string, LMat> matInfo = new Dictionary<string, LMat>();
            public Dictionary<string, float[]> atlas = new Dictionary<string, float[]>();
            public List<Mesh> meshes = new List<Mesh>();
            public List<MeshFilter> bakeTargets = new List<MeshFilter>();
            public List<MeshFilter> ambientOnly = new List<MeshFilter>();
            public Color ambient;
            public Transform root;
            public int missingTextures;
        }

        [MenuItem("FERI/Zgradi mapo v novo sceno", false, 1)]
        public static void BuildNewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build();
            string dir = FindBaseDir();
            if (dir != null)
            {
                string path = dir + "/FERI_Map.unity";
                EditorSceneManager.SaveScene(scene, path);
                Debug.Log("[FERI] Scena shranjena: " + path);
            }
        }

        [MenuItem("FERI/Zgradi mapo v trenutno sceno", false, 2)]
        public static void BuildCurrentScene()
        {
            Build();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        [MenuItem("FERI/Posodobi materiale", false, 21)]
        public static void RepairMaterials()
        {
            string baseDir = FindBaseDir();
            Shader shader = Shader.Find("FERI/PS1");
            if (baseDir == null || shader == null) { EditorUtility.DisplayDialog("FERI", "Ne najdem Data/feri_layout.json ali shaderja FERI/PS1.", "OK"); return; }
            var C = new Ctx();
            C.baseDir = baseDir;
            C.L = JsonUtility.FromJson<Layout>(File.ReadAllText(baseDir + "/Data/feri_layout.json"));
            EnsureFolder(baseDir, "Materials");
            EnsureFolder(baseDir, "Generated");
            FixTextureImports(baseDir + "/Textures");
            AssetDatabase.Refresh();
            int fixedCount = 0;
            foreach (LMat m in C.L.materials)
            {
                string path = baseDir + "/Materials/" + m.name + ".mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { GetMaterial(C, m, shader); fixedCount++; continue; }
                if (mat.GetTexture("_MainTex") == null || mat.shader != shader)
                {
                    mat.shader = shader;
                    ApplyMode(mat, m.mode, m.cull);
                    mat.SetTexture("_MainTex", LoadTexture(C, m.tex));
                    mat.SetColor("_Color", ToColor(m.color, Color.white));
                    mat.SetFloat("_Emission", m.emissive);
                    mat.SetFloat("_UseVertexColor", m.baked ? 1f : 0f);
                    EditorUtility.SetDirty(mat);
                    fixedCount++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[FERI] Posodobljenih materialov: " + fixedCount + (C.missingTextures > 0 ? ", manjkajocih tekstur: " + C.missingTextures : ""));
        }

        [MenuItem("FERI/Ponovno zapeci osvetlitev", false, 20)]
        public static void RebakeMenu()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null) { EditorUtility.DisplayDialog("FERI", "V sceni ni objekta " + RootName + ".", "OK"); return; }
            PS1Lighting lighting = root.GetComponentInChildren<PS1Lighting>(true);
            Color amb = lighting != null ? lighting.ambient : new Color(0.05f, 0.05f, 0.06f);
            var targets = new List<MeshFilter>();
            foreach (string sub in new[] { "Geometry", "Elevator/Landings" })
            {
                Transform t = root.transform.Find(sub);
                if (t != null) targets.AddRange(t.GetComponentsInChildren<MeshFilter>(true));
            }
            Bake(targets, amb);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }

        [MenuItem("FERI/Ponovno zgradi stopnice (StairsDown)", false, 22)]
        public static void RebuildStairsMenu() { RebuildGroup("StairsDown"); }

        // Rebuilds one group of the layout (its Geometry and Colliders children) in the open scene, leaving the rest of
        // the map and FERI_Meshes.asset alone: the new meshes go to Generated/FERI_<group>.asset. Save the scene after.
        public static void RebuildGroup(string group)
        {
            GameObject rootGo = GameObject.Find(RootName);
            if (rootGo == null) { EditorUtility.DisplayDialog("FERI", "V sceni ni objekta " + RootName + ".", "OK"); return; }
            string baseDir = FindBaseDir();
            Shader shader = Shader.Find("FERI/PS1");
            if (baseDir == null || shader == null) { EditorUtility.DisplayDialog("FERI", "Ne najdem layouta ali shaderja FERI/PS1.", "OK"); return; }

            var C = new Ctx();
            C.baseDir = baseDir;
            C.L = JsonUtility.FromJson<Layout>(File.ReadAllText(baseDir + "/Data/feri_layout.json"));
            PS1Lighting lighting = rootGo.GetComponentInChildren<PS1Lighting>(true);
            C.ambient = lighting != null ? lighting.ambient : ToColor(C.L.settings.ambient, new Color(0.05f, 0.05f, 0.06f));
            if (C.L.atlas != null) foreach (var a in C.L.atlas) C.atlas[a.name] = a.rect;
            foreach (LMat m in C.L.materials) { C.matInfo[m.name] = m; C.mats[m.name] = GetMaterial(C, m, shader); }
            C.root = rootGo.transform;

            Transform geo = Child(rootGo.transform, "Geometry"), cols = Child(rootGo.transform, "Colliders");
            foreach (Transform parent in new[] { geo.Find(group), cols.Find(group) })
                if (parent != null) UnityEngine.Object.DestroyImmediate(parent.gameObject);

            BuildGeometry(C, geo, cols, group);

            string meshPath = baseDir + "/Generated/FERI_" + group + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            for (int i = 0; i < C.meshes.Count; i++)
            {
                if (i == 0) AssetDatabase.CreateAsset(C.meshes[0], meshPath);
                else AssetDatabase.AddObjectToAsset(C.meshes[i], meshPath);
            }
            Bake(C.bakeTargets, C.ambient);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[FERI] " + group + " zgrajen znova: " + C.meshes.Count + " mrez. Shrani sceno (Ctrl+S).");
        }

        [MenuItem("FERI/Pretvori izbrane materiale v PS1", false, 40)]
        public static void ConvertSelected()
        {
            Shader sh = Shader.Find("FERI/PS1");
            if (sh == null) { Debug.LogError("[FERI] Shader FERI/PS1 ni najden."); return; }
            var set = new HashSet<Material>();
            foreach (UnityEngine.Object o in Selection.objects)
            {
                Material m = o as Material;
                if (m != null) set.Add(m);
                GameObject go = o as GameObject;
                if (go != null)
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (Material rm in r.sharedMaterials) if (rm != null) set.Add(rm);
            }
            int n = 0;
            foreach (Material m in set)
            {
                if (m.shader == sh) continue;
                Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
                Color col = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
                Undo.RecordObject(m, "Convert to PS1");
                m.shader = sh;
                m.SetTexture("_MainTex", tex);
                m.SetColor("_Color", col);
                ApplyMode(m, "opaque", "back");
                m.SetFloat("_UseVertexColor", 0f);
                m.SetFloat("_Emission", 0f);
                EditorUtility.SetDirty(m);
                if (tex != null)
                {
                    var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) as TextureImporter;
                    if (ti != null && ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; ti.SaveAndReimport(); }
                }
                n++;
            }
            Debug.Log("[FERI] Pretvorjenih materialov: " + n);
        }

        static string FindBaseDir()
        {
            foreach (string guid in AssetDatabase.FindAssets("feri_layout t:TextAsset"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith("/Data/feri_layout.json")) return p.Substring(0, p.Length - "/Data/feri_layout.json".Length);
            }
            return null;
        }

        public static GameObject Build()
        {
            string baseDir = FindBaseDir();
            if (baseDir == null) { EditorUtility.DisplayDialog("FERI", "Ne najdem Data/feri_layout.json.", "OK"); return null; }
            Shader shader = Shader.Find("FERI/PS1");
            if (shader == null) { EditorUtility.DisplayDialog("FERI", "Shader FERI/PS1 ni najden (Shaders/PS1.shader).", "OK"); return null; }

            var C = new Ctx();
            C.baseDir = baseDir;
            C.L = JsonUtility.FromJson<Layout>(File.ReadAllText(baseDir + "/Data/feri_layout.json"));
            C.ambient = ToColor(C.L.settings.ambient, new Color(0.05f, 0.05f, 0.06f));
            if (C.L.atlas != null) foreach (var a in C.L.atlas) C.atlas[a.name] = a.rect;

            try
            {
                EditorUtility.DisplayProgressBar("FERI", "Teksture in materiali", 0.05f);
                EnsureFolder(baseDir, "Materials");
                EnsureFolder(baseDir, "Generated");
                FixTextureImports(baseDir + "/Textures");
                AssetDatabase.Refresh();
                foreach (LMat m in C.L.materials) { C.matInfo[m.name] = m; C.mats[m.name] = GetMaterial(C, m, shader); }
                AssetDatabase.SaveAssets();

                GameObject old = GameObject.Find(RootName);
                if (old != null) UnityEngine.Object.DestroyImmediate(old);
                string meshPath = baseDir + "/Generated/FERI_Meshes.asset";
                AssetDatabase.DeleteAsset(meshPath);

                GameObject root = new GameObject(RootName);
                C.root = root.transform;
                Transform geo = Child(root.transform, "Geometry");
                Transform cols = Child(root.transform, "Colliders");
                Transform lightsRoot = Child(root.transform, "Lights");

                EditorUtility.DisplayProgressBar("FERI", "Geometrija", 0.15f);
                BuildGeometry(C, geo, cols);

                EditorUtility.DisplayProgressBar("FERI", "Luci", 0.4f);
                var lights = BuildLights(C, lightsRoot);
                BuildDynamicLamps(C, geo, lights);

                EditorUtility.DisplayProgressBar("FERI", "Dvigalo", 0.45f);
                if (C.L.elevators != null) foreach (LElev e in C.L.elevators) BuildElevator(C, e);
                BuildProps(C, shader);

                BuildPlayerAndSettings(C);

                EditorUtility.DisplayProgressBar("FERI", "Shranjujem mreze", 0.5f);
                for (int i = 0; i < C.meshes.Count; i++)
                {
                    if (i == 0) AssetDatabase.CreateAsset(C.meshes[0], meshPath);
                    else AssetDatabase.AddObjectToAsset(C.meshes[i], meshPath);
                }

                foreach (MeshFilter mf in C.ambientOnly) FillColor(mf.sharedMesh, C.ambient);
                Bake(C.bakeTargets, C.ambient);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = root;
                Debug.Log(string.Format("[FERI] Mapa zgrajena: {0} mrez, {1} luci.", C.meshes.Count, lights.Count));
                if (C.missingTextures > 0)
                    EditorUtility.DisplayDialog("FERI", C.missingTextures + " tekstur ni bilo mogoce naloziti (glej Console). Preveri mapo FERI_Map/Textures in zazeni FERI > Posodobi materiale.", "OK");
                return root;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static Transform Child(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        static void FixTextureImports(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*.png"))
            {
                string p = file.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                if (FeriTextureImporter.Configure(ti)) ti.SaveAndReimport();
            }
        }

        static Color ToColor(float[] a, Color def)
        {
            if (a == null || a.Length < 3) return def;
            return new Color(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1f);
        }

        static Vector3 V3(float[] a) { return new Vector3(a[0], a[1], a[2]); }

        static Texture2D LoadTexture(Ctx C, string texName)
        {
            string path = C.baseDir + "/Textures/" + texName + ".png";
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null && File.Exists(path))
            {

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null && FeriTextureImporter.Configure(ti)) ti.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            if (tex == null && File.Exists(path))
            {

                string assetPath = C.baseDir + "/Generated/Tex_" + texName + ".asset";
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (tex == null)
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(File.ReadAllBytes(path)))
                    {
                        tex.name = texName;
                        tex.filterMode = FilterMode.Point;
                        tex.wrapMode = TextureWrapMode.Repeat;
                        AssetDatabase.CreateAsset(tex, assetPath);
                        Debug.LogWarning("[FERI] " + path + " ni uvozen kot tekstura - uporabljam kopijo " + assetPath);
                    }
                    else tex = null;
                }
            }
            if (tex == null) { C.missingTextures++; Debug.LogError("[FERI] Tekstura ni najdena: " + path); }
            return tex;
        }

        static Material GetMaterial(Ctx C, LMat m, Shader shader)
        {
            string path = C.baseDir + "/Materials/" + m.name + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {

                bool dirty = false;
                if (mat.shader != shader) { mat.shader = shader; ApplyMode(mat, m.mode, m.cull); dirty = true; }
                if (mat.GetTexture("_MainTex") == null)
                {
                    mat.SetTexture("_MainTex", LoadTexture(C, m.tex));
                    mat.SetColor("_Color", ToColor(m.color, Color.white));
                    dirty = true;
                }
                if (dirty) EditorUtility.SetDirty(mat);
                return mat;
            }
            mat = new Material(shader);
            mat.name = m.name;
            mat.SetTexture("_MainTex", LoadTexture(C, m.tex));
            mat.SetColor("_Color", ToColor(m.color, Color.white));
            mat.SetFloat("_Emission", m.emissive);
            mat.SetFloat("_UseVertexColor", m.baked ? 1f : 0f);
            ApplyMode(mat, m.mode, m.cull);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        public static void ApplyMode(Material mat, string mode, string cull)
        {
            mat.SetFloat("_Cull", cull == "off" ? (float)CullMode.Off : (float)CullMode.Back);
            if (mode == "transparent")
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (mode == "cutout")
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }
        }

        static readonly Vector3[] FN = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
        static readonly Vector3[] FU = { Vector3.left, Vector3.right, Vector3.forward, Vector3.back, Vector3.right, Vector3.right };
        static readonly Vector3[] FV = { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.forward, Vector3.back };

        static float AbsDot(Vector3 a, Vector3 h) { return Mathf.Abs(a.x) * h.x + Mathf.Abs(a.y) * h.y + Mathf.Abs(a.z) * h.z; }

        static void AddBox(Acc a, Vector3 c, Vector3 size, Quaternion rot, bool rotated, string uvMode, float[] rect,
            Vector2 tile, float cell, Vector3 origin)
        {
            Vector3 h = size * 0.5f;
            bool fit = uvMode == "f";
            for (int f = 0; f < 6; f++)
            {
                Vector3 n = FN[f], ua = FU[f], va = FV[f];
                float hu = AbsDot(ua, h), hv = AbsDot(va, h);
                if (hu < 1e-5f || hv < 1e-5f) continue;
                int nu = cell > 0 ? Mathf.Max(1, Mathf.CeilToInt(2f * hu / cell)) : 1;
                int nv = cell > 0 ? Mathf.Max(1, Mathf.CeilToInt(2f * hv / cell)) : 1;
                Vector3 center = Vector3.Scale(n, h);
                int b0 = a.v.Count;
                Vector3 wn = rot * n;
                for (int j = 0; j <= nv; j++)
                {
                    for (int i = 0; i <= nu; i++)
                    {
                        float su = -1f + 2f * i / nu, sv = -1f + 2f * j / nv;
                        Vector3 p = center + ua * (su * hu) + va * (sv * hv);
                        Vector2 uv;
                        if (fit)
                        {
                            float u = (su + 1f) * 0.5f, v = (sv + 1f) * 0.5f;
                            if (rect != null && rect.Length == 4) { u = rect[0] + (rect[2] - rect[0]) * u; v = rect[1] + (rect[3] - rect[1]) * v; }
                            uv = new Vector2(u, v);
                        }
                        else
                        {
                            Vector3 wp = rotated ? p : p + c;
                            uv = new Vector2(Vector3.Dot(wp, ua) / tile.x, Vector3.Dot(wp, va) / tile.y);
                        }
                        a.v.Add(rot * p + c - origin);
                        a.n.Add(wn);
                        a.uv.Add(uv);
                    }
                }
                for (int j = 0; j < nv; j++)
                {
                    for (int i = 0; i < nu; i++)
                    {
                        int bl = b0 + j * (nu + 1) + i, br = bl + 1, tl = bl + nu + 1, tr = tl + 1;
                        a.t.Add(bl); a.t.Add(tl); a.t.Add(tr);
                        a.t.Add(bl); a.t.Add(tr); a.t.Add(br);
                    }
                }
            }
        }

        static void AddCyl(Acc a, Vector3 c, float r, float h, int seg, Vector2 tile)
        {
            float hh = h * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float a0 = 2f * Mathf.PI * i / seg, a1 = 2f * Mathf.PI * (i + 1) / seg, am = (a0 + a1) * 0.5f;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0) * r, -hh, Mathf.Sin(a0) * r);
                Vector3 p1 = c + new Vector3(Mathf.Cos(a1) * r, -hh, Mathf.Sin(a1) * r);
                Vector3 p2 = p1 + Vector3.up * h, p3 = p0 + Vector3.up * h;
                Vector3 n = new Vector3(Mathf.Cos(am), 0, Mathf.Sin(am));
                float u0 = a0 * r / tile.x, u1 = a1 * r / tile.x, v0 = p0.y / tile.y, v1 = p3.y / tile.y;
                int b = a.v.Count;
                a.v.Add(p0); a.v.Add(p1); a.v.Add(p2); a.v.Add(p3);
                for (int k = 0; k < 4; k++) a.n.Add(n);
                a.uv.Add(new Vector2(u0, v0)); a.uv.Add(new Vector2(u1, v0)); a.uv.Add(new Vector2(u1, v1)); a.uv.Add(new Vector2(u0, v1));
                a.t.Add(b); a.t.Add(b + 3); a.t.Add(b + 2);
                a.t.Add(b); a.t.Add(b + 2); a.t.Add(b + 1);

                Vector3 top = c + Vector3.up * hh, bot = c - Vector3.up * hh;
                b = a.v.Count;
                a.v.Add(top); a.v.Add(p2); a.v.Add(p3);
                for (int k = 0; k < 3; k++) { a.n.Add(Vector3.up); }
                a.uv.Add(new Vector2(top.x / tile.x, top.z / tile.y)); a.uv.Add(new Vector2(p2.x / tile.x, p2.z / tile.y)); a.uv.Add(new Vector2(p3.x / tile.x, p3.z / tile.y));
                a.t.Add(b); a.t.Add(b + 1); a.t.Add(b + 2);
                b = a.v.Count;
                a.v.Add(bot); a.v.Add(p0); a.v.Add(p1);
                for (int k = 0; k < 3; k++) { a.n.Add(Vector3.down); }
                a.uv.Add(new Vector2(bot.x / tile.x, bot.z / tile.y)); a.uv.Add(new Vector2(p0.x / tile.x, p0.z / tile.y)); a.uv.Add(new Vector2(p1.x / tile.x, p1.z / tile.y));
                a.t.Add(b); a.t.Add(b + 1); a.t.Add(b + 2);
            }
        }

        static Mesh ToMesh(Ctx C, Acc a, string name)
        {
            var m = new Mesh();
            m.name = name;
            if (a.v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(a.v);
            m.SetNormals(a.n);
            m.SetUVs(0, a.uv);
            var cols = new List<Color>(a.v.Count);
            for (int i = 0; i < a.v.Count; i++) cols.Add(Color.white);
            m.SetColors(cols);
            m.SetTriangles(a.t, 0);
            m.RecalculateBounds();
            C.meshes.Add(m);
            return m;
        }

        static Vector2 Tile(LMat m)
        {
            if (m == null || m.tile == null || m.tile.Length < 2) return Vector2.one;
            return new Vector2(Mathf.Max(0.001f, m.tile[0]), Mathf.Max(0.001f, m.tile[1]));
        }

        static bool IsGlass(LMat m) { return m != null && m.mode == "transparent"; }

        static void BuildGeometry(Ctx C, Transform geo, Transform cols, string onlyGroup = null)
        {
            var render = new Dictionary<string, Acc>();
            var renderKey = new Dictionary<string, KeyValuePair<string, string>>();
            var colSolid = new Dictionary<string, Acc>();
            var colGlass = new Dictionary<string, Acc>();

            foreach (LBox b in C.L.boxes)
            {
                if (b.li >= 0 || (onlyGroup != null && b.g != onlyGroup)) continue;
                LMat mi; C.matInfo.TryGetValue(b.m, out mi);
                Vector3 c = V3(b.c), s = V3(b.s);
                bool rotated = b.r != null && b.r.Length == 3 && (b.r[0] != 0 || b.r[1] != 0 || b.r[2] != 0);
                Quaternion rot = rotated ? Quaternion.Euler(b.r[0], b.r[1], b.r[2]) : Quaternion.identity;

                if (b.col == 2 || (b.col == 1 && rotated))
                {
                    var go = new GameObject("Col_" + (b.tag ?? b.m));
                    go.transform.SetParent(Child(cols, b.g), false);
                    go.transform.position = c;
                    go.transform.rotation = rot;
                    var bc = go.AddComponent<BoxCollider>();
                    bc.size = s;
                    go.layer = (b.col == 2 || IsGlass(mi)) ? GlassLayer : 0;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    if (b.col == 2) continue;
                }
                else if (b.col == 1)
                {
                    var dict = IsGlass(mi) ? colGlass : colSolid;
                    Acc ca;
                    if (!dict.TryGetValue(b.g, out ca)) { ca = new Acc(); dict[b.g] = ca; }
                    AddBox(ca, c, s, Quaternion.identity, false, "w", null, Vector2.one, 0f, Vector3.zero);
                }

                string key = b.g + "|" + b.m;
                Acc ra;
                if (!render.TryGetValue(key, out ra)) { ra = new Acc(); render[key] = ra; renderKey[key] = new KeyValuePair<string, string>(b.g, b.m); }
                bool subdiv = mi != null && mi.emissive <= 0f && mi.baked;
                AddBox(ra, c, s, rot, rotated, b.uv, b.rect, Tile(mi), subdiv ? CellSize : 0f, Vector3.zero);
            }

            foreach (LCyl cy in C.L.cyls)
            {
                if (onlyGroup != null && cy.g != onlyGroup) continue;
                LMat mi; C.matInfo.TryGetValue(cy.m, out mi);
                string key = cy.g + "|" + cy.m;
                Acc ra;
                if (!render.TryGetValue(key, out ra)) { ra = new Acc(); render[key] = ra; renderKey[key] = new KeyValuePair<string, string>(cy.g, cy.m); }
                AddCyl(ra, V3(cy.c), cy.r, cy.h, Mathf.Max(3, cy.seg), Tile(mi));
                if (cy.col == 1)
                {
                    var go = new GameObject("Col_cyl");
                    go.transform.SetParent(Child(cols, cy.g), false);
                    go.transform.position = V3(cy.c);
                    var cc = go.AddComponent<CapsuleCollider>();
                    cc.radius = cy.r; cc.height = Mathf.Max(cy.h, cy.r * 2f); cc.direction = 1;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                }
            }

            foreach (var kv in render)
            {
                string g = renderKey[kv.Key].Key, m = renderKey[kv.Key].Value;
                var go = new GameObject(m);
                go.transform.SetParent(Child(geo, g), false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = ToMesh(C, kv.Value, g + "_" + m);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = C.mats[m];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                LMat mi = C.matInfo[m];
                if (mi.emissive <= 0f) C.bakeTargets.Add(mf);
            }

            foreach (var pair in new[] { new KeyValuePair<Dictionary<string, Acc>, bool>(colSolid, false), new KeyValuePair<Dictionary<string, Acc>, bool>(colGlass, true) })
            {
                foreach (var kv in pair.Key)
                {
                    var go = new GameObject(pair.Value ? "Glass" : "Solid");
                    go.transform.SetParent(Child(cols, kv.Key), false);
                    go.layer = pair.Value ? GlassLayer : 0;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.sharedMesh = ToMesh(C, kv.Value, "col_" + kv.Key + (pair.Value ? "_glass" : ""));
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                }
            }
        }

        static List<PS1Light> BuildLights(Ctx C, Transform parent)
        {
            var list = new List<PS1Light>();
            for (int i = 0; i < C.L.lights.Length; i++)
            {
                LLight l = C.L.lights[i];
                var go = new GameObject("Light_" + i.ToString("000"));
                go.transform.SetParent(parent, false);
                go.transform.position = V3(l.p);
                var pl = go.AddComponent<PS1Light>();
                pl.color = ToColor(l.col, Color.white);
                pl.intensity = l.i;
                pl.range = l.range;
                pl.flicker = l.flicker;
                pl.baked = l.bake && l.flicker <= 0f;
                list.Add(pl);
            }
            return list;
        }

        static void BuildDynamicLamps(Ctx C, Transform geo, List<PS1Light> lights)
        {
            Transform parent = Child(geo, "Dynamic");
            foreach (LBox b in C.L.boxes)
            {
                if (b.li < 0) continue;
                LMat mi; C.matInfo.TryGetValue(b.m, out mi);
                Vector3 c = V3(b.c);
                var acc = new Acc();
                AddBox(acc, c, V3(b.s), Quaternion.identity, false, b.uv, b.rect, Tile(mi), 0f, c);
                var go = new GameObject("Lamp_" + b.li.ToString("000"));
                go.transform.SetParent(parent, false);
                go.transform.position = c;
                go.AddComponent<MeshFilter>().sharedMesh = ToMesh(C, acc, "lamp_" + b.li);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = C.mats[b.m];
                mr.shadowCastingMode = ShadowCastingMode.Off;
                if (b.li < lights.Count) lights[b.li].lampRenderer = mr;
            }
        }

        static GameObject MakeBox(Ctx C, string name, Transform parent, Vector3 min, Vector3 max, string mat,
            string uv = "w", string atlasName = null, bool collider = true, bool trigger = false, bool worldSpaceInput = true)
        {
            Vector3 c = (min + max) * 0.5f, s = max - min;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            if (worldSpaceInput) go.transform.position = c; else go.transform.localPosition = c;
            LMat mi; C.matInfo.TryGetValue(mat, out mi);
            float[] rect = null;
            if (atlasName != null) C.atlas.TryGetValue(atlasName, out rect);
            var acc = new Acc();
            AddBox(acc, Vector3.zero, s, Quaternion.identity, false, uv, rect, Tile(mi), 0f, Vector3.zero);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(C, acc, name);
            var mr = go.AddComponent<MeshRenderer>();
            Material m; if (C.mats.TryGetValue(mat, out m)) mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            if (collider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = s;
                bc.isTrigger = trigger;
                if (trigger) go.layer = GlassLayer;
            }
            return go;
        }

        static Vector4 RectV(Ctx C, string name)
        {
            float[] r;
            if (C.atlas.TryGetValue(name, out r) && r != null && r.Length == 4) return new Vector4(r[0], r[1], r[2], r[3]);
            return new Vector4(0, 0, 1, 1);
        }

        static void BuildProps(Ctx C, Shader shader)
        {
            if (C.L.props == null || C.L.props.Length == 0) return;
            Transform root = Child(C.root, "Props");
            var cache = new Dictionary<string, Material>();
            int missing = 0;
            foreach (LProp p in C.L.props)
            {
                string path = C.baseDir + "/Props/" + p.model + ".fbx";
                GameObject src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (src == null)
                {
                    if (missing++ == 0) Debug.LogWarning("[FERI] Model ni najden: " + path);
                    continue;
                }
                GameObject holder = new GameObject(p.model);
                holder.transform.SetParent(root, false);
                GameObject tilt = new GameObject("Model");
                tilt.transform.SetParent(holder.transform, false);
                GameObject go = UnityEngine.Object.Instantiate(src);
                go.transform.SetParent(tilt.transform, false);
                var rends = holder.GetComponentsInChildren<MeshRenderer>();
                if (rends.Length == 0) { UnityEngine.Object.DestroyImmediate(holder); continue; }

                Bounds lb;
                if (!LocalBounds(holder.transform, out lb)) { UnityEngine.Object.DestroyImmediate(holder); continue; }
                if (p.rx != 0f)
                {
                    tilt.transform.localRotation = Quaternion.Euler(p.rx, 0f, 0f);
                    if (!LocalBounds(holder.transform, out lb)) { UnityEngine.Object.DestroyImmediate(holder); continue; }
                }

                float sc = (p.h > 0.0001f && lb.size.y > 0.00001f) ? p.h / lb.size.y : 1f;
                holder.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f);
                holder.transform.localScale = new Vector3(sc, sc, sc);
                Vector3 want = new Vector3(p.p[0], p.p[1], p.p[2]);
                Vector3 anchor = holder.transform.TransformPoint(new Vector3(lb.center.x, lb.min.y, lb.center.z));
                holder.transform.position += want - anchor;
                GameObject go2 = holder;

                foreach (MeshRenderer r in rends)
                {
                    Material[] ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++) ms[i] = PropMaterial(C, cache, ms[i], p.tex, shader);
                    r.sharedMaterials = ms;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }

                if (p.col != 0)
                {
                    var bc = go2.AddComponent<BoxCollider>();
                    bc.center = lb.center;
                    bc.size = lb.size;
                }

                if (p.li > 0.0001f)
                {
                    var lgo = new GameObject("Light");
                    lgo.transform.SetParent(go2.transform, false);
                    lgo.transform.position = new Vector3(want.x, want.y + p.ly, want.z);
                    var pl = lgo.AddComponent<PS1Light>();
                    pl.color = ToColor(p.lcol, new Color(1f, 0.5f, 0.15f));
                    pl.intensity = p.li;
                    pl.range = p.lrng > 0 ? p.lrng : 4.5f;
                    pl.flicker = p.lfl;
                    pl.baked = false;
                }
            }
            if (missing > 0) Debug.LogWarning("[FERI] Manjkajocih modelov: " + missing + " (mapa FERI_Map/Props)");
        }

        static bool LocalBounds(Transform root, out Bounds lb)
        {
            lb = new Bounds();
            bool any = false;
            Matrix4x4 toLocal = root.worldToLocalMatrix;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                Vector3 mn = mb.min, mx = mb.max;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? mn.x : mx.x,
                                            (i & 2) == 0 ? mn.y : mx.y,
                                            (i & 4) == 0 ? mn.z : mx.z);
                    Vector3 w = m.MultiplyPoint3x4(c);
                    if (!any) { lb = new Bounds(w, Vector3.zero); any = true; }
                    else lb.Encapsulate(w);
                }
            }
            return any;
        }

        static Material PropMaterial(Ctx C, Dictionary<string, Material> cache, Material srcMat, string texName, Shader shader)
        {
            Texture tex = srcMat != null ? srcMat.mainTexture : null;
            if (tex == null && !string.IsNullOrEmpty(texName))
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(C.baseDir + "/Props/Textures/" + texName + ".png");
            string key = tex != null ? tex.name : (srcMat != null ? srcMat.name : "none");
            Material m;
            if (cache.TryGetValue(key, out m)) return m;
            string path = C.baseDir + "/Materials/Prop_" + key + ".mat";
            m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            ApplyMode(m, "cutout", "off");
            m.SetTexture("_MainTex", tex);
            m.SetColor("_Color", Color.white);
            m.SetFloat("_Emission", 0f);
            m.SetFloat("_UseVertexColor", 0f);
            EditorUtility.SetDirty(m);
            cache[key] = m;
            return m;
        }

        static void BuildElevator(Ctx C, LElev e)
        {
            float x0 = e.shaft[0], x1 = e.shaft[1], z0 = e.shaft[2], z1 = e.shaft[3];
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            string side = string.IsNullOrEmpty(e.doorSide) ? "-x" : e.doorSide;
            float yaw = side == "-z" ? -90f : side == "+z" ? 90f : side == "+x" ? 180f : 0f;
            bool zDoor = side == "-z" || side == "+z";
            float dz = zDoor ? (side == "-z" ? cx - e.doorCenter : e.doorCenter - cx)
                             : (side == "+x" ? cz - e.doorCenter : e.doorCenter - cz);
            float hw = (zDoor ? (z1 - z0) : (x1 - x0)) * 0.5f;
            var rootGo = new GameObject(string.IsNullOrEmpty(e.name) ? "Elevator" : "Elevator");
            rootGo.transform.SetParent(C.root, false);
            rootGo.transform.localPosition = new Vector3(cx, 0f, cz);
            rootGo.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var elev = rootGo.AddComponent<FeriElevator>();
            elev.doorSlideAxis = Vector3.forward;
            elev.doorTravel = 0.48f;

            int start = 0;
            for (int i = 0; i < e.floors.Length; i++) if (e.floors[i].label == e.start) start = i;
            float y0 = e.floors[start].y;

            var cabinGo = new GameObject("Cabin");
            cabinGo.transform.SetParent(rootGo.transform, false);
            cabinGo.transform.localPosition = new Vector3(0f, y0, 0f);
            var rb = cabinGo.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            Transform cab = cabinGo.transform;
            float hx = (zDoor ? (z1 - z0) : (x1 - x0)) * 0.5f - 0.05f;
            float hz = (zDoor ? (x1 - x0) : (z1 - z0)) * 0.5f - 0.05f;
            float ldz = dz;
            const float H = 2.4f, DH = 2.1f, DW = 0.5f;
            var parts = new List<GameObject>();
            parts.Add(MakeBox(C, "Floor", cab, new Vector3(-hx, -0.12f, -hz), new Vector3(hx, 0, hz), "terrazzo", worldSpaceInput: false));
            parts.Add(MakeBox(C, "Ceiling", cab, new Vector3(-hx, H, -hz), new Vector3(hx, H + 0.1f, hz), "steel", worldSpaceInput: false));
            parts.Add(MakeBox(C, "CeilingLamp", cab, new Vector3(-0.5f, H - 0.03f, -0.3f), new Vector3(0.5f, H, 0.3f), "lamp", "f", collider: false, worldSpaceInput: false));
            parts.Add(MakeBox(C, "BackWall", cab, new Vector3(hx - 0.1f, 0, -hz), new Vector3(hx, H, hz), "plaster", worldSpaceInput: false));
            parts.Add(MakeBox(C, "Mirror", cab, new Vector3(hx - 0.13f, 1.0f, -0.6f), new Vector3(hx - 0.1f, 2.2f, 0.6f), "steel", collider: false, worldSpaceInput: false));
            parts.Add(MakeBox(C, "WallS", cab, new Vector3(-hx, 0, -hz), new Vector3(hx - 0.1f, H, -hz + 0.1f), "plaster", worldSpaceInput: false));
            parts.Add(MakeBox(C, "WallN", cab, new Vector3(-hx, 0, hz - 0.1f), new Vector3(hx - 0.1f, H, hz), "plaster", worldSpaceInput: false));
            parts.Add(MakeBox(C, "FrontA", cab, new Vector3(-hx, 0, -hz + 0.1f), new Vector3(-hx + 0.1f, H, ldz - DW), "steel", worldSpaceInput: false));
            parts.Add(MakeBox(C, "FrontB", cab, new Vector3(-hx, 0, ldz + DW), new Vector3(-hx + 0.1f, H, hz - 0.1f), "steel", worldSpaceInput: false));
            parts.Add(MakeBox(C, "FrontTop", cab, new Vector3(-hx, DH, ldz - DW), new Vector3(-hx + 0.1f, H, ldz + DW), "steel", worldSpaceInput: false));
            parts.Add(MakeBox(C, "Handrail", cab, new Vector3(-0.6f, 0.9f, hz - 0.16f), new Vector3(0.9f, 0.94f, hz - 0.12f), "steel", collider: false, worldSpaceInput: false));
            var doorA = MakeBox(C, "DoorA", cab, new Vector3(-hx + 0.03f, 0, ldz - DW), new Vector3(-hx + 0.07f, DH, ldz), "door_steel", "f", worldSpaceInput: false);
            var doorB = MakeBox(C, "DoorB", cab, new Vector3(-hx + 0.03f, 0, ldz), new Vector3(-hx + 0.07f, DH, ldz + DW), "door_steel", "f", worldSpaceInput: false);
            parts.Add(doorA); parts.Add(doorB);

            float pz = -hz + 0.1f;
            parts.Add(MakeBox(C, "Panel", cab, new Vector3(-1.07f, 0.75f, pz), new Vector3(-0.83f, 1.75f, pz + 0.015f), "signs", "f", "panel", collider: false, worldSpaceInput: false));
            var disp = MakeBox(C, "Display", cab, new Vector3(-1.04f, 1.52f, pz + 0.015f), new Vector3(-0.86f, 1.64f, pz + 0.02f), "signs_glow", "f", null, collider: false, worldSpaceInput: false);
            parts.Add(disp);

            string[,] grid = { { "3", "4" }, { "1", "2" }, { "-1", "0" }, { "-3", "-2" } };
            var floorsList = new List<FeriElevator.Floor>();
            foreach (LFloor lf in e.floors)
                floorsList.Add(new FeriElevator.Floor { label = lf.label, y = lf.y, enabled = lf.enabled, ledRect = RectV(C, "led" + lf.label) });
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    string lb = grid[row, col];
                    int fi = Array.FindIndex(e.floors, f => f.label == lb);
                    float bx = col == 0 ? -0.91f : -0.99f;
                    float by = 1.38f - row * 0.1f;
                    var bgo = MakeBox(C, "Btn_" + lb, cab, new Vector3(bx - 0.035f, by - 0.035f, pz + 0.015f),
                        new Vector3(bx + 0.035f, by + 0.035f, pz + 0.03f), "signs", "f", "btn" + lb, collider: true, trigger: true, worldSpaceInput: false);
                    var btn = bgo.AddComponent<FeriElevatorButton>();
                    btn.elevator = elev; btn.floorIndex = fi; btn.buttonRenderer = bgo.GetComponent<MeshRenderer>();
                    if (fi >= 0) floorsList[fi].cabinButton = btn;
                    parts.Add(bgo);
                }
            }
            parts.Add(MakeBox(C, "BtnAlarm", cab, new Vector3(-0.985f, 0.915f, pz + 0.015f), new Vector3(-0.915f, 0.985f, pz + 0.03f), "signs", "f", "btnAlarm", collider: false, worldSpaceInput: false));

            var lgo = new GameObject("CabinLight");
            lgo.transform.SetParent(cab, false);
            lgo.transform.localPosition = new Vector3(0, H - 0.25f, 0);
            var cl = lgo.AddComponent<PS1Light>();
            cl.color = new Color(1f, 0.95f, 0.85f); cl.intensity = 0.95f; cl.range = 4.5f; cl.baked = false; cl.flicker = 0.12f;
            cl.lampRenderer = cab.Find("CeilingLamp").GetComponent<MeshRenderer>();
            var vol = new GameObject("PassengerVolume");
            vol.transform.SetParent(cab, false);
            vol.layer = GlassLayer;
            var vbc = vol.AddComponent<BoxCollider>();
            vbc.isTrigger = true; vbc.center = new Vector3(0.05f, H * 0.5f, 0); vbc.size = new Vector3(hx * 2f - 0.2f, H, hz * 2f - 0.2f);
            var src = cabinGo.AddComponent<AudioSource>();
            src.spatialBlend = 1f; src.minDistance = 1f; src.maxDistance = 18f; src.playOnAwake = false;
            foreach (var p in parts) { var mf = p.GetComponent<MeshFilter>(); if (mf != null) C.ambientOnly.Add(mf); }

            Transform landings = Child(rootGo.transform, "Landings");
            for (int i = 0; i < e.floors.Length; i++)
            {
                LFloor lf = e.floors[i];
                if (!lf.enabled) continue;
                var lroot = new GameObject("Landing_" + lf.label);
                lroot.transform.SetParent(landings, false);
                float y = lf.y;
                float lx = -hw;
                float wx = lx - 0.2f;
                var la = MakeBox(C, "DoorA", lroot.transform, new Vector3(lx - 0.14f, y, dz - DW), new Vector3(lx - 0.10f, y + DH, dz), "door_steel", "f", worldSpaceInput: false);
                var lb2 = MakeBox(C, "DoorB", lroot.transform, new Vector3(lx - 0.14f, y, dz), new Vector3(lx - 0.10f, y + DH, dz + DW), "door_steel", "f", worldSpaceInput: false);
                var ld = MakeBox(C, "Display", lroot.transform, new Vector3(wx - 0.10f, y + 2.24f, dz - 0.18f), new Vector3(wx - 0.08f, y + 2.46f, dz + 0.18f), "signs_glow", "f", null, collider: false, worldSpaceInput: false);
                MakeBox(C, "DisplayFrame", lroot.transform, new Vector3(wx - 0.08f, y + 2.2f, dz - 0.24f), new Vector3(wx - 0.05f, y + 2.5f, dz + 0.24f), "signs", "f", "display", collider: false, worldSpaceInput: false);
                var call = MakeBox(C, "CallButton", lroot.transform, new Vector3(wx - 0.02f, y + 1.05f, dz + 0.8f), new Vector3(wx, y + 1.17f, dz + 0.92f), "signs", "f", "btnCall", collider: true, trigger: true, worldSpaceInput: false);
                var cb = call.AddComponent<FeriElevatorButton>();
                cb.elevator = elev; cb.floorIndex = i; cb.isCallButton = true; cb.buttonRenderer = call.GetComponent<MeshRenderer>();
                floorsList[i].landingDoorA = la.transform;
                floorsList[i].landingDoorB = lb2.transform;
                floorsList[i].landingDisplay = ld.GetComponent<MeshRenderer>();
                foreach (var mf in lroot.GetComponentsInChildren<MeshFilter>()) C.bakeTargets.Add(mf);
            }

            elev.floors = floorsList;
            elev.cabin = cab;
            elev.cabinDoorA = doorA.transform;
            elev.cabinDoorB = doorB.transform;
            elev.cabinDisplay = disp.GetComponent<MeshRenderer>();
            elev.cabinVolume = vbc;
            elev.startFloor = start;
            elev.currentFloor = start;
            elev.audioSource = src;
        }

        static void BuildPlayerAndSettings(Ctx C)
        {
            LSettings s = C.L.settings;
            var lgo = new GameObject("PS1 Lighting");
            lgo.transform.SetParent(C.root, false);
            var lighting = lgo.AddComponent<PS1Lighting>();
            lighting.ambient = C.ambient;
            lighting.fogColor = ToColor(s.fogColor, Color.black);
            lighting.fogStart = s.fogStart;
            lighting.fogEnd = s.fogEnd;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;

            foreach (Camera cam in Camera.allCameras)
            {
                if (cam.transform.IsChildOf(C.root)) continue;
                cam.gameObject.SetActive(false);
                Debug.Log("[FERI] Izklopil obstojeco kamero: " + cam.name);
            }

            var player = new GameObject("Player");
            player.transform.SetParent(C.root, false);
            player.transform.position = V3(C.L.spawn.p);
            player.transform.rotation = Quaternion.Euler(0, C.L.spawn.yaw, 0);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.28f; cc.center = new Vector3(0, 0.875f, 0);
            cc.stepOffset = 0.35f; cc.slopeLimit = 50f; cc.skinWidth = 0.03f;
            var fp = player.AddComponent<FeriPlayer>();

            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.63f, 0);
            var pc = camGo.AddComponent<Camera>();
            pc.nearClipPlane = 0.05f; pc.farClipPlane = 60f; pc.fieldOfView = 70f;
            pc.clearFlags = CameraClearFlags.SolidColor; pc.backgroundColor = Color.black;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<PS1Camera>();
            var fl = camGo.AddComponent<PS1Flashlight>();
            fp.cameraPivot = camGo.transform;
            fp.flashlight = fl;
        }

        static void FillColor(Mesh m, Color c)
        {
            if (m == null) return;
            var cols = new Color[m.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            m.colors = cols;
            EditorUtility.SetDirty(m);
        }

        public static void Bake(List<MeshFilter> targets, Color ambient)
        {
            Physics.SyncTransforms();
            var lights = new List<PS1Light>();
            foreach (PS1Light l in PS1Light.All)
                if (l != null && l.baked && l.flicker <= 0f && l.isActiveAndEnabled) lights.Add(l);
            int mask = ~(1 << GlassLayer);
            var near = new List<PS1Light>();
            try
            {
                for (int ti = 0; ti < targets.Count; ti++)
                {
                    MeshFilter mf = targets[ti];
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (ti % 8 == 0)
                        EditorUtility.DisplayProgressBar("FERI", "Pecem osvetlitev (" + ti + "/" + targets.Count + ")", 0.55f + 0.45f * ti / Mathf.Max(1, targets.Count));
                    Mesh mesh = mf.sharedMesh;
                    Matrix4x4 m = mf.transform.localToWorldMatrix;
                    Bounds wb = mf.GetComponent<Renderer>() != null ? mf.GetComponent<Renderer>().bounds : new Bounds(mf.transform.position, Vector3.one * 1000f);
                    near.Clear();
                    foreach (PS1Light l in lights)
                    {
                        float d = Mathf.Sqrt(wb.SqrDistance(l.transform.position));
                        if (d < l.range) near.Add(l);
                    }
                    Vector3[] vs = mesh.vertices;
                    Vector3[] ns = mesh.normals;
                    var cols = new Color[vs.Length];
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3 wp = m.MultiplyPoint3x4(vs[i]);
                        Vector3 wn = m.MultiplyVector(ns[i]).normalized;
                        Color c = ambient;
                        for (int k = 0; k < near.Count; k++)
                        {
                            PS1Light l = near[k];
                            Vector3 lp = l.transform.position;
                            Vector3 d = lp - wp;
                            float dist = d.magnitude;
                            if (dist >= l.range || dist < 1e-4f) continue;
                            float att = 1f - dist / l.range; att *= att;
                            float ndl = Mathf.Clamp01(Vector3.Dot(wn, d / dist)) * 0.8f + 0.2f;
                            if (Physics.Linecast(wp + wn * 0.04f, lp, mask, QueryTriggerInteraction.Ignore)) continue;
                            c += l.color * (l.intensity * att * ndl);
                        }
                        c.a = 1f;
                        cols[i] = c;
                    }
                    mesh.colors = cols;
                    EditorUtility.SetDirty(mesh);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
