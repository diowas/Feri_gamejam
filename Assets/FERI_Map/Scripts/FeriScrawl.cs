// Pixel-font writing painted into a texture at runtime, and a quad that shows it: the messages on the Alfa whiteboards
// and the note in P02. Letters are 5x7 pixels, `px` texels per pixel, with ragged strokes and optional drips.
using UnityEngine;

namespace FeriMap
{
    public static class FeriScrawl
    {
        const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789',.!:-";
        static readonly string[] Glyphs =
        {
            ".###.#...##...#######...##...##...#",   // A
            "####.#...##...#####.#...##...#####.",   // B
            ".#####....#....#....#....#.....####",   // C
            "####.#...##...##...##...##...#####.",   // D
            "######....#....####.#....#....#####",   // E
            "######....#....####.#....#....#....",   // F
            ".#####....#....#..###...##...#.###.",   // G
            "#...##...##...#######...##...##...#",   // H
            "#####..#....#....#....#....#..#####",   // I
            "..###...#....#....#....#.#..#..##..",   // J
            "#...##..#.#.#..##...#.#..#..#.#...#",   // K
            "#....#....#....#....#....#....#####",   // L
            "#...###.###.#.##.#.##...##...##...#",   // M
            "#...###..##.#.##..###...##...##...#",   // N
            ".###.#...##...##...##...##...#.###.",   // O
            "####.#...##...#####.#....#....#....",   // P
            ".###.#...##...##...##.#.##..#..##.#",   // Q
            "####.#...##...#####.#.#..#..#.#...#",   // R
            ".#####....#.....###.....#....#####.",   // S
            "#####..#....#....#....#....#....#..",   // T
            "#...##...##...##...##...##...#.###.",   // U
            "#...##...##...##...##...#.#.#...#..",   // V
            "#...##...##...##.#.##.#.###.###...#",   // W
            "#...##...#.#.#...#...#.#.#...##...#",   // X
            "#...##...#.#.#...#....#....#....#..",   // Y
            "#####....#...#...#...#...#....#####",   // Z
            ".###.#...##..###.#.###..##...#.###.",   // 0
            "..#...##....#....#....#....#...###.",   // 1
            ".###.#...#....#...#...#...#...#####",   // 2
            "####.....#....#.###.....#....#####.",   // 3
            "#...##...##...######....#....#....#",   // 4
            "######....#....####.....#....#####.",   // 5
            ".###.#....#....####.#...##...#.###.",   // 6
            "#####....#...#...#...#....#....#...",   // 7
            ".###.#...##...#.###.#...##...#.###.",   // 8
            ".###.#...##...#.####....#....#.###.",   // 9
            "..#....#...#.......................",   // '
            "...........................#...#...",   // ,
            "................................#..",   // .
            "..#....#....#....#....#.........#..",   // !
            ".......#...................#.......",   // :
            "................###................",   // -
        };

        /// <summary>Paints the lines (upper case) into a texture. Background: paper colour, or transparent black.</summary>
        public static Texture2D Paint(string[] lines, int px, Color ink, bool drips, int seed, Color? paper = null)
        {
            const int Pad = 6, Gap = 3;   // in font pixels
            int cols = 0;
            foreach (var l in lines) cols = Mathf.Max(cols, l.Length);
            int w = (cols * 6 + Pad * 2) * px, h = (lines.Length * (7 + Gap) - Gap + Pad * 2) * px;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Writing" };
            var pix = new Color[w * h];
            Color bg = paper ?? new Color(0f, 0f, 0f, 0f);
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            for (int i = 0; i < pix.Length; i++)
                pix[i] = paper.HasValue ? bg * (0.94f + 0.06f * R()) : bg;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li].ToUpperInvariant();
                int x = (w - line.Length * 6 * px) / 2;                    // centred
                int top = h - (Pad + li * (7 + Gap)) * px - 1;
                foreach (char ch in line)
                {
                    int ci = Chars.IndexOf(ch);
                    int jy = (int)(R() * 3f) - 1;                          // letters bob up and down
                    if (ci >= 0)
                        for (int gy = 0; gy < 7; gy++)
                            for (int gx = 0; gx < 5; gx++)
                            {
                                if (Glyphs[ci][gy * 5 + gx] != '#') continue;
                                for (int sy = 0; sy < px; sy++)
                                    for (int sx = 0; sx < px; sx++)
                                    {
                                        if (px > 1 && R() < 0.12f) continue;   // ragged strokes
                                        Put(pix, w, h, x + gx * px + sx, top - gy * px - sy + jy, ink, R());
                                    }
                                if (drips && gy == 6 && R() < 0.3f)
                                {
                                    int dx = x + gx * px + (int)(R() * px), len = px + (int)(R() * px * 3f);
                                    for (int d = 0; d < len; d++) Put(pix, w, h, dx, top - 7 * px + jy - d, ink, R());
                                }
                            }
                    x += 6 * px;
                }
            }
            tex.SetPixels(pix);
            tex.Apply();
            return tex;
        }

        static void Put(Color[] pix, int w, int h, int x, int y, Color ink, float n)
        {
            if (x < 0 || x >= w || y < 0 || y >= h) return;
            Color c = ink * (0.8f + 0.35f * n); c.a = 1f;
            pix[y * w + x] = c;
        }

        /// <summary>
        /// A quad showing the lines, centred at pos and facing along rot's -Z (a quad's visible side), letters
        /// letterHeight metres tall. It copies the map material called matName (so it's lit like the map); with
        /// glow the writing faintly shines in the dark.
        /// </summary>
        public static GameObject Decal(string name, string[] lines, Vector3 pos, Quaternion rot, float letterHeight, int px,
            Color ink, Color glow, bool drips, int seed, string matName, Color? paper = null)
        {
            var tex = Paint(lines, px, ink, drips, seed, paper);
            float texel = letterHeight / (7f * px);
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(tex.width * texel, tex.height * texel, 1f);

            var src = GameObject.Find(matName);
            var r0 = src != null ? src.GetComponent<Renderer>() : null;
            Material m = r0 != null && r0.sharedMaterial != null ? new Material(r0.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.name = name + " (runtime)";
            m.mainTexture = tex;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_UseVertexColor")) m.SetFloat("_UseVertexColor", 0f);
            if (!paper.HasValue)
            {
                if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
                m.renderQueue = 2450;   // alpha test
            }
            if (glow.maxColorComponent > 0f)
            {
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", glow);
                m.EnableKeyword("_EMISSION");
            }
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
