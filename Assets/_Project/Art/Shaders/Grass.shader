// Mobile grass blades (URP). Opaque, two-sided, no textures: blade colour comes from vertex colour.
// Vertex colour: RGB = blade tint, A = bend weight (0 at the root, 1 at the tip).
// Wind: travelling sine waves + gust noise, tips only. Pusher: blades bend away from _SS_GrassPusher
// (xyz = world position, w = radius), set every frame by GrassPusher.cs on the player.
// Light: main light (wrapped Lambert on an up-biased normal) + SH ambient + per-vertex point lights + fog.
Shader "SurvivorSouls/Grass"
{
    Properties
    {
        _BaseTint ("Tint", Color) = (1, 1, 1, 1)
        _TipLight ("Tip Brightness", Range(0, 1)) = 0.15
        _WindStrength ("Wind Strength", Range(0, 0.5)) = 0.09
        _WindSpeed ("Wind Speed", Range(0, 5)) = 1.6
        _WindScale ("Wind Wave Scale", Range(0.01, 1)) = 0.22
        _WindDirection ("Wind Direction (XZ)", Vector) = (1, 0, 0.35, 0)
        _PushStrength ("Push Strength", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseTint;
                half _TipLight;
                half _WindStrength;
                half _WindSpeed;
                half _WindScale;
                float4 _WindDirection;
                half _PushStrength;
            CBUFFER_END

            float4 _SS_GrassPusher; // global: xyz = pusher position, w = radius (0 = off)

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR;
                half fog : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                half bend = v.color.a * v.color.a;

                // Wind: two travelling waves + slow gust envelope.
                float2 dir = normalize(_WindDirection.xz + 1e-4);
                float phase = dot(ws.xz, dir) * _WindScale;
                float t = _Time.y * _WindSpeed;
                float gust = 0.6 + 0.4 * sin(t * 0.37 + phase * 0.5);
                float sway = (sin(t - phase * 6.2831) * 0.7 + sin(t * 2.3 - phase * 13.0 + ws.x) * 0.3) * gust;
                ws.xz += dir * (sway * _WindStrength * bend);

                // Player push: tips lean away and down.
                float2 away = ws.xz - _SS_GrassPusher.xz;
                float dist = length(away);
                half push = saturate(1.0 - dist / max(_SS_GrassPusher.w, 1e-3)) * step(1e-3, _SS_GrassPusher.w);
                ws.xz += (away / max(dist, 1e-3)) * (push * _PushStrength * bend);
                ws.y -= push * _PushStrength * 0.5 * bend;

                // Lighting on an up-biased normal: blades read as one soft lawn, not as flat cards.
                half3 n = half3(0, 1, 0);
                Light main = GetMainLight();
                half ndl = saturate(dot(n, main.direction) * 0.6 + 0.4);
                half3 light = main.color * ndl + SampleSH(n);
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; ++i)
                {
                    Light add = GetAdditionalLight(i, ws);
                    light += add.color * add.distanceAttenuation * 0.8;
                }
                #endif

                half tip = 1.0 + _TipLight * v.color.a;
                o.color = v.color.rgb * _BaseTint.rgb * light * tip;
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return half4(MixFog(i.color, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
