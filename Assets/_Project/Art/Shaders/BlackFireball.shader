// Black fireball of the Butcher of Ashgrave (KAN-174). Mesh = SM_BlackFireball.fbx (Blender "Fantasy Black Fireball",
// Tools/Blender/export_black_fireball.py): a comet, head at the pivot pointing +Z, UV0.y = 0 at the head tip .. 1 at the
// tail tip, UV0.x = angle round the long axis. Drawn as mesh particles of ONE particle system (EnemyProjectiles
// SetParticles): one draw call for every fireball, no texture, no light loop, no shadows.
// Look (rebuilds the Blender Eevee shaders, which Unity cannot read): a near-black body, a violet flame band and a hot
// magenta core at the head, both broken up by flowing value noise; a crackling white-violet fresnel rim (the Blender
// lightning curve); the tail frays and fades out. Alpha blend (not additive): the dark body must read on pale snow.
// Per-particle input: vertex colour RGB = tint, A = fade. Particle mesh vertices arrive in world space (CPU-built mesh
// particles, GPU instancing off).
Shader "SurvivorSouls/BlackFireball"
{
    Properties
    {
        _BodyColor ("Body", Color) = (0.03, 0.0, 0.05, 1)
        _BandColor ("Flame Band", Color) = (0.42, 0.1, 1, 1)
        _CoreColor ("Hot Core", Color) = (1, 0.18, 0.85, 1)
        _RimColor ("Lightning Rim", Color) = (0.85, 0.72, 1, 1)
        _RimPower ("Rim Tightness", Range(0.5, 8)) = 2.4
        _RimStrength ("Rim Strength", Range(0, 4)) = 1.5
        _FlowSpeed ("Flame Flow (per s)", Range(0, 6)) = 1.8
        _TailFade ("Tail Fade Start (v)", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BodyColor;
                half4 _BandColor;
                half4 _CoreColor;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half _FlowSpeed;
                half _TailFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                half fog : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Value noise that wraps every `period` cells in x (u goes round the comet: no seam).
            float Noise(float2 p, float period)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float x0 = i.x - period * floor(i.x / period);
                float x1 = x0 + 1.0 - period * floor((x0 + 1.0) / period);
                float a = Hash(float2(x0, i.y));
                float b = Hash(float2(x1, i.y));
                float c = Hash(float2(x0, i.y + 1.0));
                float d = Hash(float2(x1, i.y + 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _FlowSpeed;
                float2 uv = i.uv;
                // Flames flow from the head to the tail (v grows), two octaves.
                float n = Noise(float2(uv.x * 6.0, uv.y * 4.0 - t), 6.0) * 0.65
                        + Noise(float2(uv.x * 12.0, uv.y * 8.0 - t * 1.7 + 3.1), 12.0) * 0.35;

                float3 normal = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half ndv = saturate(dot(normal, viewDir));
                half fresnel = 1.0 - ndv;

                float v = uv.y + (n - 0.5) * 0.35;
                half3 col = _BodyColor.rgb;
                half band = smoothstep(0.14, 0.3, v) * (1.0 - smoothstep(0.42, 0.68, v)) * smoothstep(0.35, 0.75, n);
                col = lerp(col, _BandColor.rgb * 1.6, band);
                half core = (1.0 - smoothstep(0.0, 0.2, uv.y)) * ndv * ndv * (0.55 + 0.7 * n);
                col += _CoreColor.rgb * core * 1.5;

                float crackle = Noise(float2(uv.x * 14.0 + t * 0.6, uv.y * 6.0 - t * 1.3), 14.0);
                half rim = pow(fresnel, _RimPower) * _RimStrength * (0.45 + smoothstep(0.5, 0.8, crackle));
                col += _RimColor.rgb * rim;
                col *= i.color.rgb;

                half tail = 1.0 - smoothstep(_TailFade, 1.0, v);
                half alpha = saturate(tail * (0.88 + rim)) * i.color.a;
                return half4(MixFog(col, i.fog), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
