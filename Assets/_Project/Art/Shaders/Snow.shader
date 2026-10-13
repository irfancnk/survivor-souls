// Realm 1 snow ground (URP, mobile). World-space XZ mapping, so the big ground quad, beach slopes and snow
// mounds from different meshes join without seams. Anti-tiling: detail map at _DetailScale m, macro map at
// _MacroScale m blends fresh / wind-packed / scoured snow. Sparkle: sparse glints from a mipmapped dot map,
// offset by view direction so they twinkle when the camera moves; mips fade them out at distance (no shimmer).
// _DetailMap: RGB = albedo detail (around 0.5 grey = neutral, x2), A = sparkle dots.
// _MacroMap: R = packed amount, G = scoured (frozen sand/shingle shows) amount, B = soft brightness variation.
Shader "SurvivorSouls/Snow"
{
    Properties
    {
        _FreshColor ("Fresh Snow", Color) = (0.9, 0.92, 0.96, 1)
        _PackedColor ("Wind-Packed Snow", Color) = (0.78, 0.82, 0.88, 1)
        _ScouredColor ("Scoured (sand/shingle)", Color) = (0.62, 0.6, 0.58, 1)
        _DetailMap ("Detail (RGB) + Sparkle (A)", 2D) = "grey" {}
        _NormalMap ("Detail Normal", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 2)) = 0.6
        _DetailScale ("Detail Tile (m)", Float) = 6
        _MacroMap ("Macro (R packed, G scoured, B tone)", 2D) = "grey" {}
        _MacroScale ("Macro Tile (m)", Float) = 64
        _ScouredAmount ("Scoured Amount", Range(0, 1)) = 0.35
        _WetColor ("Wet Shingle (below floor)", Color) = (0.25, 0.26, 0.27, 1)
        _WetDepth ("Wet Fade Depth (m)", Float) = 0.35
        _Sparkle ("Sparkle", Range(0, 2)) = 0.6
        _Wrap ("Light Wrap", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "SSLighting.hlsl"

            TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_MacroMap); SAMPLER(sampler_MacroMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _FreshColor;
                half4 _PackedColor;
                half4 _ScouredColor;
                half _NormalStrength;
                float _DetailScale;
                float _MacroScale;
                half _ScouredAmount;
                half4 _WetColor;
                float _WetDepth;
                half _Sparkle;
                half _Wrap;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz / _DetailScale;
                half4 detail = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv);
                half4 detail2 = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv * 0.37 + 0.21); // second scale breaks repeats
                half3 macro = SAMPLE_TEXTURE2D(_MacroMap, sampler_MacroMap, i.positionWS.xz / _MacroScale).rgb;

                half3 snow = lerp(_FreshColor.rgb, _PackedColor.rgb, macro.r);
                snow = lerp(snow, _ScouredColor.rgb, saturate(macro.g * _ScouredAmount * 2.0));
                snow *= (0.92 + macro.b * 0.16) * (detail.rgb * 0.5 + 0.75) * (detail2.rgb * 0.3 + 0.85);
                // Below the play floor (beach slope down to the sea): snow gives way to wet dark shingle.
                half wet = saturate(-i.positionWS.y / _WetDepth);
                snow = lerp(snow, _WetColor.rgb * (detail.rgb * 0.6 + 0.7), wet);

                // Detail normal in world space (ground faces up: tangent X = world X, bitangent = world Z).
                half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalStrength);
                half3 n = normalize(i.normalWS);
                half3 t = normalize(half3(1, 0, 0) - n * n.x);
                half3 b = cross(n, t);
                n = normalize(tn.x * t + tn.y * b + tn.z * n);

                half3 color = snow * SSDiffuseLight(i.positionWS, n, _Wrap);

                // Sparkle: dots from two lookups that only line up from some view angles.
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                half glintA = detail.a;
                half glintB = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv * 1.31 + viewDir.xz * 0.35).a;
                half glint = saturate(glintA * glintB * 4.0 - 0.15) * _Sparkle * (1.0 - macro.g * 0.8) * (1.0 - wet);
                color += glint * GetMainLight().color;

                return half4(MixFog(color, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
