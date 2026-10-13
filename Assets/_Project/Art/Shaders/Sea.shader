// Mobile sea for Realm 1 (URP). Opaque: no depth texture, no grab pass, no realtime reflection.
// Vertex: three travelling sine waves (smaller near the shore). Pixel: two scrolling normal maps, depth colour
// from distance to the shore line (_ShoreZ: shallow teal -> dark blue-grey), fresnel to the overcast sky colour,
// soft sun glint, broken foam near the shore. The mesh is a flat grid that follows the player in X only.
Shader "SurvivorSouls/Sea"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.24, 0.42, 0.44, 1)
        _DeepColor ("Deep", Color) = (0.1, 0.16, 0.2, 1)
        _SkyColor ("Sky Reflection", Color) = (0.7, 0.76, 0.82, 1)
        _FoamColor ("Foam", Color) = (0.9, 0.93, 0.95, 1)
        _ShoreZ ("Shore Line Z (world)", Float) = 14
        _SeaLevel ("Sea Level Y (world)", Float) = -0.35
        _DepthRange ("Shallow -> Deep (m)", Float) = 18
        _NormalMap ("Ripple Normal", 2D) = "bump" {}
        _RippleScale ("Ripple Tile (m)", Float) = 7
        _RippleStrength ("Ripple Strength", Range(0, 2)) = 0.7
        _FoamMap ("Foam Noise (R)", 2D) = "grey" {}
        _FoamWidth ("Foam Width (m)", Float) = 3
        _WaveHeight ("Wave Height (m)", Range(0, 0.5)) = 0.12
        _WaveLength ("Wave Length (m)", Float) = 9
        _WaveSpeed ("Wave Speed", Float) = 1.1
        _FlowSpeed ("Ripple Flow Speed", Float) = 0.05
        _Glint ("Sun Glint", Range(0, 2)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "SSLighting.hlsl"

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FoamMap); SAMPLER(sampler_FoamMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SkyColor;
                half4 _FoamColor;
                float _ShoreZ;
                float _SeaLevel;
                float _DepthRange;
                float _RippleScale;
                half _RippleStrength;
                float _FoamWidth;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _FlowSpeed;
                half _Glint;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 waveNormal : TEXCOORD1;
                half fog : TEXCOORD2;
            };

            // One travelling wave: height and its XZ gradient.
            float3 Wave(float2 p, float2 dir, float length, float amp, float speed)
            {
                float k = 6.2831853 / length;
                float f = dot(p, normalize(dir)) * k - _Time.y * speed * sqrt(9.8 * k) * 0.35;
                return float3(sin(f) * amp, normalize(dir) * (cos(f) * amp * k));
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float depth = saturate((ws.z - _ShoreZ) / _DepthRange);
                float amp = _WaveHeight * lerp(0.35, 1.0, depth);
                // Waves roll in towards the shore (-Z) at slight angles.
                float3 w = Wave(ws.xz, float2(0.15, -1), _WaveLength, amp, _WaveSpeed)
                         + Wave(ws.xz, float2(-0.45, -1), _WaveLength * 0.61, amp * 0.5, _WaveSpeed * 1.2)
                         + Wave(ws.xz, float2(0.8, -0.6), _WaveLength * 0.37, amp * 0.25, _WaveSpeed * 1.5);
                ws.y += w.x;
                o.waveNormal = normalize(half3(-w.y, 1, -w.z));
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz / _RippleScale;
                float t = _Time.y * _FlowSpeed;
                half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv + float2(t, -t * 1.7)), _RippleStrength);
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 0.53 + float2(-t * 0.8, -t)), _RippleStrength);
                half3 rn = normalize(half3(n1.xy + n2.xy, n1.z * n2.z)); // tangent space ripple (X = world X, Y = world Z)
                half3 n = normalize(i.waveNormal + half3(rn.x, 0, rn.y));

                float shore = i.positionWS.z - _ShoreZ;
                half depth = saturate(shore / _DepthRange);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, sqrt(depth));
                half3 lit = water * SSDiffuseLight(i.positionWS, n, 0.6);

                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                half3 color = lerp(lit, _SkyColor.rgb, saturate(fresnel * 0.85 + 0.08));

                Light main = GetMainLight();
                half3 h = normalize(main.direction + viewDir);
                color += main.color * pow(saturate(dot(n, h)), 90.0) * _Glint;

                // Broken foam: a band at the shore that swells in and out, plus thin streaks on wave crests.
                half foamNoise = SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, i.positionWS.xz * float2(0.18, 0.35) + float2(t * 0.6, t)).r;
                half swell = 0.5 + 0.5 * sin(_Time.y * 0.9 + i.positionWS.x * 0.13);
                half band = saturate(1.0 - shore / (_FoamWidth * (0.6 + 0.4 * swell)));
                half crest = saturate((i.positionWS.y - _SeaLevel - _WaveHeight * 0.6) / max(_WaveHeight * 0.4, 1e-3));
                half foam = saturate((band * 1.3 + crest * 0.35) * foamNoise * 2.0 - 0.45);
                color = lerp(color, _FoamColor.rgb * SSDiffuseLight(i.positionWS, half3(0, 1, 0), 0.5), foam);

                return half4(MixFog(color, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
