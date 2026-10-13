// Foam ribbon along a jagged shore line (URP, mobile, alpha blend). The mesh is a strip: UV.x = metres along the
// shore, UV.y = 0 at the land edge -> 1 out at sea; vertex alpha fades the strip ends. The wash band slides
// up and down the strip like waves running in and draining back.
Shader "SurvivorSouls/ShoreFoam"
{
    Properties
    {
        _FoamColor ("Foam", Color) = (0.92, 0.95, 0.97, 1)
        _FoamMap ("Foam Noise (R)", 2D) = "grey" {}
        _Opacity ("Opacity", Range(0, 1)) = 0.85
        _WashSpeed ("Wash Speed", Float) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "SSLighting.hlsl"

            TEXTURE2D(_FoamMap); SAMPLER(sampler_FoamMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _FoamColor;
                half _Opacity;
                float _WashSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half alpha : TEXCOORD1;
                half3 light : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = float2(ws.x, v.uv.y); // world X keeps neighbouring tiles' foam continuous
                o.alpha = v.color.a;
                o.light = SSDiffuseLight(ws, half3(0, 1, 0), 0.5);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _WashSpeed;
                half wash = 0.5 + 0.5 * sin(t + i.uv.x * 0.21);            // 0 = drained, 1 = run up the beach
                half front = 0.15 + (1.0 - wash) * 0.55;                    // where the wash edge sits across the strip
                half band = saturate(1.0 - abs(i.uv.y - front) * 3.2);
                half edge = saturate(1.0 - i.uv.y * 1.6) * 0.55;            // thin foam always clings to the land edge
                half noise = SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, float2(i.uv.x * 0.22, i.uv.y * 0.6 - t * 0.05)).r;
                half a = saturate((band + edge) * (noise * 1.8 - 0.25)) * _Opacity * i.alpha;
                return half4(MixFog(_FoamColor.rgb * i.light, i.fog), a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
