using System.Collections.Generic;
using UnityEngine;

namespace FeriMap
{

    [ExecuteAlways]
    public class PS1Lighting : MonoBehaviour
    {
        [Header("Osvetlitev")]
        public Color ambient = new Color(0.05f, 0.05f, 0.06f);
        [Tooltip("Mnozilnik svetlosti (vse PS1 materiale).")]
        public float exposure = 0.225f;

        [Header("Megla")]
        public bool fog = false;
        public Color fogColor = Color.black;
        public float fogStart = 6f;
        public float fogEnd = 34f;

        [Header("PS1 efekti")]
        public bool vertexSnap = false;
        public Vector2 snapResolution = new Vector2(320, 240);
        public bool dither = true;
        [Range(0f, 1f)] public float ditherStrength = 0.35f;
        public bool affineWarp = false;

        public const int MaxLights = 16;

        readonly Vector4[] lightPos = new Vector4[MaxLights];
        readonly Vector4[] lightCol = new Vector4[MaxLights];
        readonly List<PS1Light> sorted = new List<PS1Light>();

        static readonly int IdAmbient = Shader.PropertyToID("_PS1_Ambient");
        static readonly int IdFogColor = Shader.PropertyToID("_PS1_FogColor");
        static readonly int IdFogRange = Shader.PropertyToID("_PS1_FogRange");
        static readonly int IdSnap = Shader.PropertyToID("_PS1_Snap");
        static readonly int IdDither = Shader.PropertyToID("_PS1_Dither");
        static readonly int IdDisableAffine = Shader.PropertyToID("_PS1_DisableAffine");
        static readonly int IdExposure = Shader.PropertyToID("_PS1_Exposure");
        static readonly int IdCount = Shader.PropertyToID("_PS1_LightCount");
        static readonly int IdPos = Shader.PropertyToID("_PS1_LightPos");
        static readonly int IdCol = Shader.PropertyToID("_PS1_LightCol");
        static readonly int IdSpotPos = Shader.PropertyToID("_PS1_SpotPos");
        static readonly int IdSpotDir = Shader.PropertyToID("_PS1_SpotDir");
        static readonly int IdSpotCol = Shader.PropertyToID("_PS1_SpotCol");

        void OnEnable() { Upload(); }
        void OnValidate() { Upload(); }
        void LateUpdate() { Upload(); }

        Vector3 ViewerPosition()
        {
            Camera cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null
                && UnityEditor.SceneView.lastActiveSceneView.camera != null)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            return cam != null ? cam.transform.position : transform.position;
        }

        public void Upload()
        {
            Shader.SetGlobalColor(IdAmbient, ambient);
            Shader.SetGlobalColor(IdFogColor, fogColor);
            Shader.SetGlobalVector(IdFogRange, fog ? new Vector4(fogStart, fogEnd, 0, 0) : Vector4.zero);
            Shader.SetGlobalVector(IdSnap, new Vector4(snapResolution.x, snapResolution.y,
                (vertexSnap && Application.isPlaying) ? 1f : 0f, 0));
            Shader.SetGlobalFloat(IdDither, dither ? ditherStrength : 0f);
            Shader.SetGlobalFloat(IdDisableAffine, affineWarp ? 0f : 1f);
            Shader.SetGlobalFloat(IdExposure, exposure);

            Vector3 vp = ViewerPosition();
            sorted.Clear();
            for (int i = 0; i < PS1Light.All.Count; i++)
            {
                PS1Light l = PS1Light.All[i];
                if (l == null || !l.isActiveAndEnabled) continue;
                sorted.Add(l);
            }
            sorted.Sort((a, b) =>
                (Vector3.Distance(a.transform.position, vp) - a.range).CompareTo(
                 Vector3.Distance(b.transform.position, vp) - b.range));
            int n = Mathf.Min(MaxLights, sorted.Count);
            for (int i = 0; i < MaxLights; i++)
            {
                if (i < n)
                {
                    PS1Light l = sorted[i];
                    Vector3 p = l.transform.position;
                    float inten = Application.isPlaying ? l.CurrentIntensity : l.intensity;
                    lightPos[i] = new Vector4(p.x, p.y, p.z, l.range);
                    lightCol[i] = new Vector4(l.color.r * inten, l.color.g * inten, l.color.b * inten,
                        (l.baked && l.flicker <= 0f) ? 1f : 0f);
                }
                else
                {
                    lightPos[i] = new Vector4(0, -10000, 0, 0.001f);
                    lightCol[i] = Vector4.zero;
                }
            }
            Shader.SetGlobalFloat(IdCount, n);
            Shader.SetGlobalVectorArray(IdPos, lightPos);
            Shader.SetGlobalVectorArray(IdCol, lightCol);

            PS1Flashlight f = PS1Flashlight.Active;
            if (f != null && f.isActiveAndEnabled && Application.isPlaying && f.CurrentIntensity > 0f)
            {
                Transform t = f.transform;
                Vector3 p = t.position, d = t.forward;
                float ci = Mathf.Cos(f.innerAngle * Mathf.Deg2Rad);
                float co = Mathf.Cos(f.outerAngle * Mathf.Deg2Rad);
                Shader.SetGlobalVector(IdSpotPos, new Vector4(p.x, p.y, p.z, f.range));
                Shader.SetGlobalVector(IdSpotDir, new Vector4(d.x, d.y, d.z, co));
                Color c = f.color * f.CurrentIntensity;
                Shader.SetGlobalVector(IdSpotCol, new Vector4(c.r, c.g, c.b, ci));
            }
            else
            {
                Shader.SetGlobalVector(IdSpotCol, new Vector4(0, 0, 0, 1));
                Shader.SetGlobalVector(IdSpotPos, new Vector4(0, -10000, 0, 0.001f));
            }
        }
    }
}
