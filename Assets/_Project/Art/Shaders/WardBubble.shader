// Hallowed Shield ward bubble around the Knight (KAN-93). One sphere, one draw call, no texture. Front faces only: a
// fresnel rim (bright gold-white at the edge, nearly clear in the middle, so the Knight stays readable), a faint hex
// lattice in object space that drifts slowly upward, and a thin rune band round the equator. A darker outer fringe keeps
// the edge readable on white snow. Premultiplied alpha.
// Per-frame values (_Alpha, _Flash) come from ShieldWardEffect.cs through one MaterialPropertyBlock; the style values are
// material properties (M_FX_WardBubble.mat).
Shader "SurvivorSouls/WardBubble"
{
    Properties
    {
        [Header(Style)]
        _RimColor ("Rim", Color) = (1.0, 0.86, 0.52, 1)
        _InnerColor ("Inner Tint", Color) = (0.62, 0.78, 1.0, 1)
        _FringeColor ("Dark Fringe", Color) = (0.12, 0.08, 0.02, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _RimStrength ("Rim Strength", Range(0, 2)) = 1.3
        _InnerAlpha ("Inner Alpha", Range(0, 0.5)) = 0.08
        _Fringe ("Fringe Strength", Range(0, 1)) = 0.35
        _HexScale ("Hex Lattice Scale", Range(1, 20)) = 7
        _HexStrength ("Hex Lattice Strength", Range(0, 1)) = 0.28
        _BandStrength ("Rune Band Strength", Range(0, 1)) = 0.3
        _Drift ("Lattice Drift (per s)", Range(0, 1)) = 0.12
        _Opacity ("Opacity", Range(0, 1)) = 0.9

        [Header(Driven by ShieldWardEffect.cs)]
        _Alpha ("Alpha Multiplier", Range(0, 1)) = 1
        _Flash ("Hit Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half4 _InnerColor;
                half4 _FringeColor;
                float _RimPower;
                half _RimStrength;
                half _InnerAlpha;
                half _Fringe;
                float _HexScale;
                half _HexStrength;
                half _BandStrength;
                float _Drift;
                half _Opacity;
                half _Alpha;
                half _Flash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            // Floored modulo (GLSL mod): fmod truncates and mirrors the grid at 0.
            float2 Mod(float2 x, float2 y)
            {
                return x - y * floor(x / y);
            }

            // Distance to the nearest hex cell edge, 0 at the edge, ~0.5 in the middle.
            float HexEdge(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float2 a = Mod(p, s) - s * 0.5;
                float2 b = Mod(p - s * 0.5, s) - s * 0.5;
                float2 g = dot(a, a) < dot(b, b) ? a : b;
                g = abs(g);
                return 0.5 - max(dot(g, s * 0.5), g.x);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float facing = saturate(dot(n, v));
                float rim = pow(1.0 - facing, _RimPower);

                // Lattice on a cylinder wrap of the sphere (longitude, height), drifting up.
                float3 q = normalize(i.positionOS);
                float lon = atan2(q.z, q.x) / 6.2831853;
                float2 hp = float2(lon * _HexScale * 2.0, q.y * _HexScale * 0.9 + _Time.y * _Drift * _HexScale);
                float edge = HexEdge(hp);
                float aa = max(fwidth(edge), 1e-4);
                float hex = 1.0 - smoothstep(0.0, aa * 1.5 + 0.02, edge);

                // Rune band: a thin bright ring round the middle with dashes (glyph rhythm).
                float band = 1.0 - smoothstep(0.012, 0.03, abs(q.y - 0.05));
                float dash = step(0.45, frac(lon * 48.0 + _Time.y * 0.05));
                band *= dash;

                half flash = _Flash;
                half3 col = _InnerColor.rgb * _InnerAlpha;
                half a = _InnerAlpha;
                half rimA = saturate(rim * _RimStrength * (1.0 + flash));
                col += _RimColor.rgb * rimA;
                a += rimA * 0.75;
                half latticeA = hex * _HexStrength * (0.35 + rim) * (1.0 + flash * 1.5);
                col += _RimColor.rgb * latticeA;
                a += latticeA * 0.5;
                half bandA = band * _BandStrength * (1.0 + flash);
                col += _RimColor.rgb * bandA;
                a += bandA * 0.5;
                // Dark fringe right at the silhouette: the gold rim still reads on bright snow.
                half fringe = smoothstep(0.82, 0.97, 1.0 - facing) * _Fringe;
                col = lerp(col, _FringeColor.rgb * a, fringe * 0.5);
                a = saturate(a + fringe * 0.25);

                half k = saturate(_Opacity * _Alpha);
                return half4(col * k, saturate(a) * k);
            }
            ENDHLSL
        }
    }
}
