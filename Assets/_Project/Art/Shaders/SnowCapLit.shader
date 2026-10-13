// Props and masonry for Realm 1 (URP, mobile). One material for many props: palette/stone texture lit by the
// shared cheap model, plus a snow cap on up-facing surfaces (world normal Y) broken up by world-space noise.
// Vertex colour: RGB multiplies the albedo, A = snow coverage (1 = full cap, 0.5 = patchy, 0 = never: ice, fire bowls).
Shader "SurvivorSouls/SnowCapLit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _UvScale ("UV Scale", Float) = 1
        _SnowColor ("Snow Color", Color) = (0.9, 0.92, 0.96, 1)
        _SnowLevel ("Snow Level (normal Y)", Range(0, 1)) = 0.55
        _SnowSoftness ("Snow Edge Softness", Range(0.01, 0.5)) = 0.08
        _SnowNoise ("Snow Noise (B)", 2D) = "grey" {}
        _SnowNoiseScale ("Snow Noise Tile (m)", Float) = 5
        _SnowNoiseAmount ("Snow Noise Amount", Range(0, 1)) = 0.35
        _Wrap ("Light Wrap", Range(0, 1)) = 0.25
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

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_SnowNoise); SAMPLER(sampler_SnowNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _UvScale;
                half4 _SnowColor;
                half _SnowLevel;
                half _SnowSoftness;
                float _SnowNoiseScale;
                half _SnowNoiseAmount;
                half _Wrap;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : TEXCOORD3;
                half fog : TEXCOORD4;
            };

            // Smooth value noise in world space (no texture: patch edges stay soft at any distance).
            float Hash21(float2 p)
            {
                p = frac(p * float2(0.3183099, 0.3678794) + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * (p.x + p.y));
            }

            float ValueNoise(float2 x)
            {
                float2 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + 1.0), f.x), f.y);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = v.uv * _UvScale;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb * i.color.rgb;

                half noise = SAMPLE_TEXTURE2D(_SnowNoise, sampler_SnowNoise, i.positionWS.xz / _SnowNoiseScale).b;
                half level = _SnowLevel + (noise - 0.5) * _SnowNoiseAmount;
                half snow = smoothstep(level - _SnowSoftness, level + _SnowSoftness, n.y);
                // Coverage: low vertex alpha leaves only patches of snow (finer noise), 0 leaves none.
                float2 pp = float2(i.positionWS.x + i.positionWS.y * 0.7, i.positionWS.z - i.positionWS.y * 0.5);
                half patch = ValueNoise(pp / 1.4) * 0.65 + ValueNoise(pp / 0.55 + 7.3) * 0.35;
                snow *= saturate((patch - (1.0 - i.color.a)) * 4.0 + 0.5) * step(0.01, i.color.a);
                albedo = lerp(albedo, _SnowColor.rgb, snow);

                half3 color = albedo * SSDiffuseLight(i.positionWS, n, lerp(_Wrap, 0.45, snow));
                return half4(MixFog(color, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
