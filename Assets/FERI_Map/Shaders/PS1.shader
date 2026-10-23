// FERI PS1 shader
// - vertex snapping (jitter), affine texture warping, point-sampled textures
// - Gouraud (per-vertex) lighting: baked vertex colours + dynamic PS1Light-s
// - per-pixel flashlight (PS1Flashlight), distance fog, optional 15-bit dithering
// Works in Built-in RP and URP (pass without LightMode tag).
Shader "FERI/PS1"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Emission ("Emission (unlit)", Range(0,1)) = 0
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
        [Toggle] _AlphaClip ("Alpha clip", Float) = 0
        [Toggle] _UseVertexColor ("Use baked vertex light", Float) = 1
        _Affine ("Affine warp", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 0
        [Toggle] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull [_Cull]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            float4 _Color; float _Emission; float _Cutoff; float _AlphaClip;
            float _UseVertexColor; float _Affine;

            // globals set by PS1Lighting
            float4 _PS1_Ambient;
            float4 _PS1_FogColor;
            float4 _PS1_FogRange;    // x start, y end
            float4 _PS1_Snap;        // xy virtual resolution, z enabled
            float  _PS1_Dither;
            float  _PS1_Exposure;
            float  _PS1_LightCount;
            float4 _PS1_LightPos[16];  // xyz, w = range
            float4 _PS1_LightCol[16];  // rgb * intensity, w = 1 if baked into vertex colours
            float4 _PS1_SpotPos;       // xyz, w = range
            float4 _PS1_SpotDir;       // xyz, w = cos(outer)
            float4 _PS1_SpotCol;       // rgb, w = cos(inner)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 uvw : TEXCOORD0;
                float3 light : TEXCOORD1;
                float  fog : TEXCOORD2;
                float3 wpos : TEXCOORD3;
                float3 wnormal : TEXCOORD4;
                float4 spos : TEXCOORD5;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float4 wp = mul(unity_ObjectToWorld, v.vertex);
                float3 wn = UnityObjectToWorldNormal(v.normal);
                float4 cp = mul(UNITY_MATRIX_VP, wp);
                if (_PS1_Snap.z > 0.5)
                {
                    float2 res = max(_PS1_Snap.xy * 0.5, 1);
                    cp.xy = floor(cp.xy / cp.w * res + 0.5) / res * cp.w;
                }
                o.pos = cp;

                float2 uv = TRANSFORM_TEX(v.uv, _MainTex);
                float aff = lerp(1.0, max(cp.w, 0.0001), _Affine);
                o.uvw = float3(uv * aff, aff);

                float3 L = (_UseVertexColor > 0.5) ? v.color.rgb : _PS1_Ambient.rgb;
                int count = (int)_PS1_LightCount;
                [unroll]
                for (int i = 0; i < 16; i++)
                {
                    if (i < count)
                    {
                        float skip = (_UseVertexColor > 0.5) ? _PS1_LightCol[i].w : 0;
                        float3 d = _PS1_LightPos[i].xyz - wp.xyz;
                        float dist = length(d);
                        float att = saturate(1.0 - dist / max(_PS1_LightPos[i].w, 0.001));
                        att *= att;
                        float ndl = saturate(dot(wn, d / max(dist, 0.0001))) * 0.8 + 0.2;
                        L += _PS1_LightCol[i].rgb * att * ndl * (1.0 - skip);
                    }
                }
                o.light = L;

                float camDist = length(_WorldSpaceCameraPos.xyz - wp.xyz);
                o.fog = (_PS1_FogRange.y > _PS1_FogRange.x) ? saturate((camDist - _PS1_FogRange.x) / (_PS1_FogRange.y - _PS1_FogRange.x)) : 0;
                o.wpos = wp.xyz;
                o.wnormal = wn;
                o.spos = ComputeScreenPos(cp);
                return o;
            }

            static const float bayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uvw.xy / i.uvw.z;
                float4 tex = tex2D(_MainTex, uv) * _Color;
                if (_AlphaClip > 0.5) clip(tex.a - _Cutoff);

                float3 light = i.light;
                // flashlight (per pixel so the cone reads well)
                float3 d = _PS1_SpotPos.xyz - i.wpos;
                float dist = length(d);
                float3 dn = d / max(dist, 0.0001);
                float cosA = dot(-dn, _PS1_SpotDir.xyz);
                float spot = saturate((cosA - _PS1_SpotDir.w) / max(_PS1_SpotCol.w - _PS1_SpotDir.w, 0.001));
                float att = saturate(1.0 - dist / max(_PS1_SpotPos.w, 0.001));
                float ndl = saturate(dot(normalize(i.wnormal), dn) * 0.85 + 0.15);
                light += _PS1_SpotCol.rgb * spot * att * att * ndl;

                float expo = (_PS1_Exposure > 0) ? _PS1_Exposure : 1.0;
                float3 col = lerp(tex.rgb * light * expo, tex.rgb, _Emission);
                col = lerp(col, _PS1_FogColor.rgb, i.fog);

                if (_PS1_Dither > 0.5)
                {
                    float2 px = floor(i.spos.xy / max(i.spos.w, 0.0001) * _ScreenParams.xy);
                    int idx = (int)(fmod(px.x, 4.0) + fmod(px.y, 4.0) * 4.0);
                    float b = bayer[idx] / 16.0 - 0.5;
                    col = floor(col * 31.0 + 0.5 + b) / 31.0;
                }
                return fixed4(col, tex.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
