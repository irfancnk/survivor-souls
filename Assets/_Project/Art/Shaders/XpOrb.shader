// XP orb (soul wisp body), the XP pickup (KAN-34). Opaque gold sphere drawn as mesh particles of ONE particle system (SoulWispField
// SetParticles): one draw call for any number of orbs, no texture, no realtime light loop, no shadows.
// Look: warm gold base lit by the scene's main light direction (wrapped, never black), an emissive core so it glows on
// shaded ground, a strong Blinn hotspot, a bright fresnel rim, a shine band that sweeps across the orb, and a thin dark
// silhouette edge so the orb reads on white snow (no second outline pass).
// Per-particle input: vertex colour RGB = tier tint (SoulWispField tiers), vertex colour A = shine phase (0..1), so the
// orbs do not flash in sync. Particle mesh vertices arrive in world space (CPU-built mesh particles, GPU instancing off).
Shader "SurvivorSouls/XpOrb"
{
    Properties
    {
        _BaseColor ("Base Multiplier", Color) = (1, 1, 1, 1)
        _Ambient ("Ambient (unlit side)", Range(0, 1)) = 0.5
        _Emission ("Core Emission", Range(0, 1)) = 0.35
        _HotspotColor ("Hotspot", Color) = (1, 0.97, 0.85, 1)
        _HotspotPower ("Hotspot Tightness", Range(4, 128)) = 36
        _HotspotStrength ("Hotspot Strength", Range(0, 3)) = 1.3
        _RimColor ("Rim", Color) = (1, 0.93, 0.62, 1)
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.8
        _EdgeColor ("Dark Edge", Color) = (0.16, 0.07, 0.01, 1)
        _EdgeStart ("Edge Start (1 - N.V)", Range(0, 1)) = 0.62
        _EdgeEnd ("Edge Full (1 - N.V)", Range(0, 1)) = 0.8
        _SweepColor ("Shine Sweep", Color) = (1, 1, 0.9, 1)
        _SweepStrength ("Shine Sweep Strength", Range(0, 2)) = 0.9
        _SweepWidth ("Shine Sweep Width", Range(0.05, 1)) = 0.22
        _SweepPeriod ("Shine Sweep Period (s)", Range(0.3, 6)) = 1.8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Ambient;
                half _Emission;
                half4 _HotspotColor;
                half _HotspotPower;
                half _HotspotStrength;
                half4 _RimColor;
                half _RimStrength;
                half4 _EdgeColor;
                half _EdgeStart;
                half _EdgeEnd;
                half4 _SweepColor;
                half _SweepStrength;
                half _SweepWidth;
                half _SweepPeriod;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
                half fog : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 lightDir = normalize(_MainLightPosition.xyz + float3(0.0, 0.001, 0.0));
                half3 gold = i.color.rgb * _BaseColor.rgb;

                half wrap = saturate(dot(n, lightDir) * 0.5 + 0.5);
                half3 col = gold * (_Ambient + (1.0 - _Ambient) * wrap) + gold * _Emission;

                float3 h = normalize(lightDir + viewDir);
                half spec = pow(saturate(dot(n, h)), _HotspotPower) * _HotspotStrength;
                col += _HotspotColor.rgb * spec;

                half fresnel = 1.0 - saturate(dot(n, viewDir));
                half rim = smoothstep(0.25, _EdgeStart, fresnel) * _RimStrength;
                col += _RimColor.rgb * rim * 0.6;

                // Shine band: slides over the orb along a screen-diagonal axis, then rests off the orb for part of the period.
                float3 axis = normalize(UNITY_MATRIX_V[0].xyz + UNITY_MATRIX_V[1].xyz);
                float s = frac(_Time.y / _SweepPeriod + i.color.a);
                float centre = lerp(-1.3, 2.6, s);
                float d = (dot(n, axis) - centre) / _SweepWidth;
                col += _SweepColor.rgb * (exp(-d * d) * _SweepStrength * (1.0 - fresnel * 0.5));

                half edge = smoothstep(_EdgeStart, _EdgeEnd, fresnel);
                col = lerp(col, _EdgeColor.rgb, edge * _EdgeColor.a);

                return half4(MixFog(col, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
