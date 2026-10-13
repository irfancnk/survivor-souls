// Fake light for the exit portal (KAN-39): additive, no texture, no real light (keeps the 4-lights-per-object budget).
// _Shape 0 = round pool on the ground (quad lying flat), 1 = light beam (tall upright quad: soft sides, fades upward).
// Colour goes from _LockedColor to _OpenColor with _Open; _Alpha fades the whole thing. Both come from PortalGate.cs
// through a MaterialPropertyBlock. A slow shimmer keeps it alive.
Shader "SurvivorSouls/PortalGlow"
{
    Properties
    {
        _LockedColor ("Locked Colour", Color) = (0.9, 0.18, 0.06, 1)
        _OpenColor ("Open Colour", Color) = (0.45, 0.8, 1.0, 1)
        _Intensity ("Intensity", Range(0, 3)) = 1
        [Enum(Ground,0,Beam,1)] _Shape ("Shape", Float) = 0

        [Header(Driven by PortalGate.cs)]
        _Open ("Open", Range(0, 1)) = 0
        _Alpha ("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LockedColor;
                half4 _OpenColor;
                half _Intensity;
                float _Shape;
                float _Open;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.uv * 2.0 - 1.0;
                float ground = exp(-dot(p, p) * 3.2) * (1.0 - smoothstep(0.8, 1.0, length(p)));
                float side = exp(-p.x * p.x * 9.0);
                float beam = side * (1.0 - smoothstep(-0.2, 1.0, p.y)) * smoothstep(-1.0, -0.85, p.y)
                             * (0.85 + 0.15 * sin(p.y * 6.0 - t * 4.0));
                float shape = lerp(ground, beam, step(0.5, _Shape));
                half3 col = lerp(_LockedColor.rgb, _OpenColor.rgb, saturate(_Open));
                float shimmer = 0.9 + 0.1 * sin(t * 2.7);
                return half4(col * shape * _Intensity * _Alpha * shimmer, 0.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
