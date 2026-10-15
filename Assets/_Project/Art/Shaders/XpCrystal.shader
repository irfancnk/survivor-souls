// XP soul crystal, the XP pickup body (KAN-177; replaces the KAN-34 XpOrb sphere). Opaque faceted gold gem drawn as mesh
// particles of ONE particle system (SoulWispField SetParticles): one draw call for any number of crystals, no texture,
// no realtime light loop, no shadows. Mesh: Tools/Blender/build_xp_crystal.py (SM_XpCrystal.fbx, flat facets, soft
// bevelled edges).
// Look: warm gold lit by the main light direction (wrapped, never black), deeper amber at the bottom, a soft inner core
// glow (strongest in the girdle and on facets that face the camera, so the gem looks lit from inside), sharp facet
// glints (flat facets catch the Blinn hotspot as the crystal spins), a bright fresnel rim, a shine band that sweeps over
// it, and a thin dark silhouette edge so it reads on white snow.
// Per-particle input: vertex colour RGB = tier tint (SoulWispField tiers; brighter while it flies in), vertex colour A =
// shine phase (0..1). Mesh UV0: u = height 0 (bottom tip) .. 1 (top tip), v = facet id 0..1. Particle mesh vertices
// arrive in world space (CPU-built mesh particles, GPU instancing off).
Shader "SurvivorSouls/XpCrystal"
{
    Properties
    {
        _BaseColor ("Base Multiplier", Color) = (1, 1, 1, 1)
        _DeepColor ("Deep Amber (bottom)", Color) = (0.78, 0.36, 0.06, 1)
        _Ambient ("Ambient (unlit side)", Range(0, 1)) = 0.55
        _CoreColor ("Core Glow", Color) = (1, 0.86, 0.5, 1)
        _CoreStrength ("Core Glow Strength", Range(0, 2)) = 0.55
        _FacetVariation ("Facet Brightness Variation", Range(0, 0.5)) = 0.16
        _HotspotColor ("Glint", Color) = (1, 0.98, 0.9, 1)
        _HotspotPower ("Glint Tightness", Range(4, 256)) = 64
        _HotspotStrength ("Glint Strength", Range(0, 4)) = 1.8
        _RimColor ("Rim", Color) = (1, 0.93, 0.62, 1)
        _RimStrength ("Rim Strength", Range(0, 2)) = 0.7
        _EdgeColor ("Dark Edge", Color) = (0.18, 0.08, 0.01, 1)
        _EdgeStart ("Edge Start (1 - N.V)", Range(0, 1)) = 0.7
        _EdgeEnd ("Edge Full (1 - N.V)", Range(0, 1)) = 0.9
        _SweepColor ("Shine Sweep", Color) = (1, 1, 0.9, 1)
        _SweepStrength ("Shine Sweep Strength", Range(0, 2)) = 0.7
        _SweepWidth ("Shine Sweep Width", Range(0.05, 1)) = 0.2
        _SweepPeriod ("Shine Sweep Period (s)", Range(0.3, 6)) = 2.2
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
                half4 _DeepColor;
                half _Ambient;
                half4 _CoreColor;
                half _CoreStrength;
                half _FacetVariation;
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
                float2 data : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
                float2 data : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.data = v.data;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 lightDir = normalize(_MainLightPosition.xyz + float3(0.0, 0.001, 0.0));
                half height = i.data.x;
                half facet = i.data.y;

                // Gold body: deeper amber towards the bottom tip, each flat facet a little brighter or darker (cut gem).
                half3 tint = i.color.rgb * _BaseColor.rgb;
                half3 gold = lerp(tint * _DeepColor.rgb, tint, smoothstep(0.0, 0.7, height));
                gold *= 1.0 + (facet - 0.5) * 2.0 * _FacetVariation;

                half wrap = saturate(dot(n, lightDir) * 0.5 + 0.5);
                half3 col = gold * (_Ambient + (1.0 - _Ambient) * wrap);

                // Inner core: strongest in the girdle (height ~0.4) and on facets that face the camera.
                half facing = saturate(dot(n, viewDir));
                half band = saturate(1.0 - abs(height - 0.42) * 2.2);
                col += _CoreColor.rgb * tint * (band * band * facing * _CoreStrength);

                // Facet glints: flat facets catch the hotspot in turn as the crystal spins.
                float3 h = normalize(lightDir + viewDir);
                half spec = pow(saturate(dot(n, h)), _HotspotPower) * _HotspotStrength;
                col += _HotspotColor.rgb * spec;

                half fresnel = 1.0 - facing;
                half rim = smoothstep(0.3, _EdgeStart, fresnel) * _RimStrength;
                col += _RimColor.rgb * rim * 0.6;

                // Shine band: slides over the crystal along a screen-diagonal axis, then rests off it for part of the period.
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
