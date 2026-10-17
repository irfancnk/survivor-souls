// Mobile sea around an island Chamber (KAN-245). Opaque: no depth texture, no grab pass, no realtime reflection, one draw call.
// Based on Sea.shader. The shore is the island rectangle (_IslandCentre, _IslandHalf): depth colour, smaller waves and the
// foam all come from the box distance to the island edge, so the sea rings the whole island.
// Vertex: three travelling sine waves rolling in from the north-east. Pixel: two scrolling ripple normal maps, thin bright
// crest lines along the long swell (reads from the gameplay camera), whitecaps on the crests, shore wash lines that roll in,
// fresnel to the sky colour and a soft sun glint.
Shader "SurvivorSouls/IslandSea"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.1, 0.19, 0.23, 1)
        _DeepColor ("Deep", Color) = (0.02, 0.05, 0.09, 1)
        _CrestColor ("Crest Light", Color) = (0.2, 0.36, 0.4, 1)
        _SkyColor ("Sky Reflection", Color) = (0.5, 0.42, 0.44, 1)
        _FoamColor ("Foam", Color) = (0.86, 0.9, 0.93, 1)
        _IslandCentre ("Island Centre XZ (world)", Vector) = (0, -400, 0, 0)
        _IslandHalf ("Island Half Size XZ (m)", Vector) = (21, 33, 0, 0)
        _DepthRange ("Shallow -> Deep (m)", Float) = 22
        _NormalMap ("Ripple Normal", 2D) = "bump" {}
        _RippleScale ("Ripple Tile (m)", Float) = 6
        _RippleStrength ("Ripple Strength", Range(0, 2)) = 0.8
        _FoamMap ("Foam Noise (R)", 2D) = "grey" {}
        _FoamWidth ("Shore Foam Width (m)", Float) = 5
        _WaveHeight ("Wave Height (m)", Range(0, 1)) = 0.35
        _WaveLength ("Wave Length (m)", Float) = 16
        _WaveSpeed ("Wave Speed", Float) = 0.9
        _FlowSpeed ("Ripple Flow Speed", Float) = 0.04
        _CrestWidth ("Crest Line Width", Range(0.01, 0.4)) = 0.12
        _Glint ("Sun Glint", Range(0, 2)) = 0.8
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
                half4 _CrestColor;
                half4 _SkyColor;
                half4 _FoamColor;
                float4 _IslandCentre;
                float4 _IslandHalf;
                float _DepthRange;
                float _RippleScale;
                half _RippleStrength;
                float _FoamWidth;
                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _FlowSpeed;
                half _CrestWidth;
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
                float2 phase : TEXCOORD3; // x = main swell phase, y = cross swell phase
            };

            // Distance (m) from the island rectangle edge; negative inside.
            float ShoreDistance(float2 p)
            {
                float2 d = abs(p - _IslandCentre.xy) - _IslandHalf.xy;
                return max(d.x, d.y);
            }

            // One travelling wave: height, XZ gradient and phase.
            float4 Wave(float2 p, float2 dir, float length, float amp, float speed)
            {
                float2 n = normalize(dir);
                float k = 6.2831853 / length;
                float f = dot(p, n) * k - _Time.y * speed * sqrt(9.8 * k) * 0.35;
                return float4(sin(f) * amp, n * (cos(f) * amp * k), f);
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float shore = ShoreDistance(ws.xz);
                float depth = saturate(shore / _DepthRange);
                float amp = _WaveHeight * lerp(0.3, 1.0, depth);
                // Swells roll in from the north-east (towards -X -Z), the cross swell from the north.
                float4 w1 = Wave(ws.xz, float2(-0.6, -1), _WaveLength, amp, _WaveSpeed);
                float4 w2 = Wave(ws.xz, float2(0.35, -1), _WaveLength * 0.53, amp * 0.45, _WaveSpeed * 1.2);
                float4 w3 = Wave(ws.xz, float2(-1, -0.25), _WaveLength * 0.31, amp * 0.2, _WaveSpeed * 1.5);
                ws.y += w1.x + w2.x + w3.x;
                float2 grad = w1.yz + w2.yz + w3.yz;
                o.waveNormal = normalize(half3(-grad.x, 1, -grad.y));
                o.phase = float2(w1.w, w2.w);
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
                half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv * 0.47 + float2(-t * 0.8, -t)), _RippleStrength);
                half3 rn = normalize(half3(n1.xy + n2.xy, n1.z * n2.z)); // tangent space ripple (X = world X, Y = world Z)
                half3 n = normalize(i.waveNormal + half3(rn.x, 0, rn.y));

                float shore = ShoreDistance(i.positionWS.xz);
                half depth = saturate(shore / _DepthRange);
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, sqrt(depth));

                // Thin crest lines along the swells: light shining through the wave top.
                half c1 = saturate((sin(i.phase.x) - (1.0 - _CrestWidth)) / _CrestWidth);
                half c2 = saturate((sin(i.phase.y) - (1.0 - _CrestWidth * 0.7)) / (_CrestWidth * 0.7)) * 0.6;
                half crest = max(c1, c2);
                water = lerp(water, _CrestColor.rgb, crest * 0.6);

                half3 lit = water * SSDiffuseLight(i.positionWS, n, 0.6);
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                half fresnel = pow(1.0 - saturate(dot(n, viewDir)), 4.0);
                half3 color = lerp(lit, _SkyColor.rgb, saturate(fresnel * 0.8 + 0.06));

                Light main = GetMainLight();
                half3 h = normalize(main.direction + viewDir);
                color += main.color * pow(saturate(dot(n, h)), 110.0) * _Glint;

                // Foam: whitecaps on the main crest, shore wash lines that roll in, a broken band against the island.
                half foamNoise = SAMPLE_TEXTURE2D(_FoamMap, sampler_FoamMap, i.positionWS.xz * float2(0.16, 0.3) + float2(t * 0.6, t)).r;
                half caps = saturate(c1 * foamNoise * 2.2 - 0.55);
                half near = saturate(1.0 - shore / _FoamWidth);
                half wash = frac(shore * 0.42 - _Time.y * 0.12 + foamNoise * 0.9);
                half washLine = saturate((wash - 0.8) / 0.18) * near;
                half band = saturate(1.0 - shore / (_FoamWidth * 0.35));
                half foam = saturate(max(max(caps, washLine * foamNoise * 1.6), band * foamNoise * 1.8 - 0.2));
                color = lerp(color, _FoamColor.rgb * SSDiffuseLight(i.positionWS, half3(0, 1, 0), 0.5), foam);

                return half4(MixFog(color, i.fog), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
