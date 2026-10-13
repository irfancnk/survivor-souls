// Cold overcast skybox: zenith -> horizon gradient, a pale band at the horizon, a soft low sun glow and a
// darker sea-side below the horizon. No textures. Used for ambient look and any view that reaches the sky.
Shader "SurvivorSouls/OvercastSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.52, 0.58, 0.66, 1)
        _HorizonColor ("Horizon", Color) = (0.8, 0.83, 0.86, 1)
        _BelowColor ("Below Horizon", Color) = (0.32, 0.38, 0.43, 1)
        _SunGlowColor ("Sun Glow", Color) = (1, 0.82, 0.62, 1)
        _SunDirection ("Sun Glow Direction", Vector) = (-0.4, 0.12, 1, 0)
        _SunGlowSize ("Sun Glow Size", Range(1, 64)) = 10
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _BelowColor;
                half4 _SunGlowColor;
                float4 _SunDirection;
                half _SunGlowSize;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                half up = saturate(d.y);
                half3 c = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(up, 0.6));
                c = lerp(c, _BelowColor.rgb, saturate(-d.y * 6.0));
                half glow = pow(saturate(dot(d, normalize(_SunDirection.xyz))), _SunGlowSize);
                c += _SunGlowColor.rgb * glow * 0.35 * saturate(1.0 + d.y * 4.0);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
