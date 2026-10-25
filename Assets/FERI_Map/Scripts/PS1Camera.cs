using UnityEngine;

namespace FeriMap
{

    [RequireComponent(typeof(Camera))]
    public class PS1Camera : MonoBehaviour
    {
        // A softer retro image without the original 240p pixelation.
        public bool lowRes = true;
        [Range(120, 720)] public int verticalResolution = 480;

        Camera cam;
        RenderTexture rt;
        Camera screenCam;
        int lastW, lastH, lastRes;

        void OnEnable()
        {
            cam = GetComponent<Camera>();
        }

        void OnDisable()
        {
            if (cam != null) cam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
            if (screenCam != null) Destroy(screenCam.gameObject);
        }

        void LateUpdate()
        {
            if (!lowRes)
            {
                if (cam.targetTexture == rt && rt != null) cam.targetTexture = null;
                if (screenCam != null) screenCam.enabled = false;
                return;
            }
            if (rt == null || lastW != Screen.width || lastH != Screen.height || lastRes != verticalResolution)
                Rebuild();
        }

        void Rebuild()
        {
            lastW = Screen.width; lastH = Screen.height; lastRes = verticalResolution;
            int h = verticalResolution;
            int w = Mathf.Max(1, Mathf.RoundToInt(h * (float)Screen.width / Mathf.Max(1, Screen.height)));
            if (rt != null) { cam.targetTexture = null; rt.Release(); Destroy(rt); }
            rt = new RenderTexture(w, h, 24);
            rt.filterMode = FilterMode.Bilinear;
            rt.name = "PS1_LowRes";
            rt.Create();
            cam.targetTexture = rt;
            if (screenCam == null)
            {

                var go = new GameObject("PS1 Screen Camera");
                go.hideFlags = HideFlags.DontSave;
                screenCam = go.AddComponent<Camera>();
                screenCam.cullingMask = 0;
                screenCam.clearFlags = CameraClearFlags.SolidColor;
                screenCam.backgroundColor = Color.black;
                screenCam.depth = cam.depth - 10;
            }
            screenCam.enabled = true;
        }

        void OnGUI()
        {
            if (!lowRes || rt == null || Event.current.type != EventType.Repaint) return;
            GUI.depth = 100;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rt, ScaleMode.StretchToFill, false);
        }
    }
}
