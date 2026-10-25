// X-ray overlay for the XRAY ability (FeriAbilities appends it to the monsters' renderers).
// Draws only where the object is hidden behind something (ZTest Greater), as a glowing additive silhouette.
Shader "FERI/XRay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.25, 0.15, 1)
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;

            struct v2f { float4 pos : SV_POSITION; float rim : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 view = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz);
                o.rim = 1.0 - saturate(dot(n, view));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float pulse = 0.85 + 0.15 * sin(_Time.y * 4.0);
                return _Color * (0.18 + i.rim * i.rim * 1.4) * pulse;
            }
            ENDCG
        }
    }
}
