// Dressing for the blackout mission (FeriBlackout calls Setup once): "THEY'RE WATCHING" is scrawled across the middle
// whiteboard in Alfa (the whiteboard texture itself has been wiped clean), in red that faintly glows in the dark.
using UnityEngine;

namespace FeriMap
{
    public static class FeriAlfaWhiteboard
    {
        // just in front of the middle whiteboard at the front of Alfa (it faces the seats, towards -z)
        static readonly Vector3 BoardFront = new Vector3(7.25f, 1.2f, 38.455f);
        static readonly Vector2 WritingSize = new Vector2(3.3f, 0.45f);

        public static void Setup()
        {
            Write("THEY'RE WATCHING");
        }

        static void Write(string text)
        {
            var tex = Scrawl(text);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "WhiteboardWriting";
            go.transform.position = BoardFront;
            go.transform.rotation = Quaternion.Euler(0f, 0f, -2.5f);   // a quad faces -z already: towards the seats; slightly crooked
            go.transform.localScale = new Vector3(WritingSize.x, WritingSize.y, 1f);

            Material m = null;
            var src = GameObject.Find("whiteboard");
            var r0 = src != null ? src.GetComponent<Renderer>() : null;
            m = r0 != null && r0.sharedMaterial != null ? new Material(r0.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.name = "Whiteboard writing (runtime)";
            m.mainTexture = tex;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 0f);
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
            if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(0.3f, 0.03f, 0.03f));
            m.EnableKeyword("_EMISSION");
            m.renderQueue = 2450;   // alpha test
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 5x7 pixel letters
        static readonly string Chars = "THEYRWACINGO'";
        static readonly string[] Font =
        {
            "#####..#....#....#....#....#....#..",   // T
            "#...##...##...#######...##...##...#",   // H
            "######....#....####.#....#....#####",   // E
            "#...##...#.#.#...#....#....#....#..",   // Y
            "####.#...##...#####.#.#..#..#.#...#",   // R
            "#...##...##...##.#.##.#.##.#.#.#.#.",   // W
            ".###.#...##...#######...##...##...#",   // A
            ".#####....#....#....#....#.....####",   // C
            "#####..#....#....#....#....#..#####",   // I
            "#...###..##.#.##..###...##...##...#",   // N
            ".#####....#....#..###...##...#.###.",   // G
            ".###.#...##...##...##...##...#.###.",   // O
            "..#....#...#.......................",   // '
        };

        // the text in rough red strokes with drips, on a transparent black background (the emission map uses it too)
        static Texture2D Scrawl(string text)
        {
            const int Px = 3, W = 320, H = 44;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Whiteboard writing" };
            var px = new Color[W * H];
            var rnd = new System.Random(66);
            float R() => (float)rnd.NextDouble();

            int x0 = (W - text.Length * 6 * Px) / 2;
            int x = x0;
            foreach (char ch in text)
            {
                int ci = Chars.IndexOf(ch);
                int jy = (int)(R() * 3f) - 1;   // letters bob up and down
                if (ci >= 0)
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                        {
                            if (Font[ci][gy * 5 + gx] != '#') continue;
                            for (int sy = 0; sy < Px; sy++)
                                for (int sx = 0; sx < Px; sx++)
                                {
                                    if (R() < 0.12f) continue;   // ragged strokes
                                    int ix = x + gx * Px + sx, iy = H - 8 - (gy * Px + sy) + jy - 4;
                                    Put(px, W, H, ix, iy, R());
                                }
                            // drips run down from the bottom row
                            if (gy == 6 && R() < 0.35f)
                            {
                                int dx = x + gx * Px + (int)(R() * Px), len = 2 + (int)(R() * 9f);
                                for (int d = 0; d < len; d++) Put(px, W, H, dx, H - 8 - 7 * Px + jy - 4 - d, R());
                            }
                        }
                x += 6 * Px;
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static void Put(Color[] px, int w, int h, int x, int y, float n)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            px[y * w + x] = new Color(0.55f + 0.25f * n, 0.03f, 0.03f, 1f);   // red marker, running like blood
        }
    }
}
