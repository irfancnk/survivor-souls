// Exit portal surface (KAN-39). One quad in the gate arch, one draw call, no texture, no grab pass.
// _Open (0..1, from PortalGate.cs through a MaterialPropertyBlock) drives three looks:
//   0          locked: dark film, slowly pulsing red cracks (isolines of value noise), red lock rune in the centre.
//   0 .. 0.6   opening: five rim runes light one by one, cracks spread and turn white-gold.
//   0.6 .. 0.8 the film bursts (white flash) into the portal.
//   1          open: swirling energy (noise in a frame that twists with the radius and spins with time), bright core,
//              fresnel-like rim along the arch edge.
// Shape: rounded rectangle (superellipse) filling the quad; the stone frame hides its corners. Premultiplied alpha.
// UV: (0,0) bottom-left of the arch opening. _Aspect = quad width / height keeps the swirl round.
// _Pulse (0..1, KAN-49): the Knight touched the sealed portal: lock rune, cracks and the five rim runes flare, then fade.
Shader "SurvivorSouls/Portal"
{
    Properties
    {
        [Header(Locked)]
        _SealColor ("Seal Film", Color) = (0.05, 0.03, 0.07, 0.9)
        _CrackColor ("Cracks", Color) = (1.0, 0.22, 0.08, 1)
        _RuneColor ("Rim Runes", Color) = (1.0, 0.78, 0.36, 1)

        [Header(Open)]
        _DeepColor ("Deep Swirl", Color) = (0.08, 0.24, 0.62, 1)
        _BrightColor ("Bright Swirl", Color) = (0.52, 0.86, 1.0, 1)
        _CoreColor ("Core", Color) = (0.95, 0.98, 1.0, 1)
        _RimColor ("Rim", Color) = (1.0, 0.88, 0.58, 1)
        _Twist ("Twist", Range(0, 12)) = 5.5
        _Spin ("Spin Speed", Range(0, 6)) = 1.6
        _Aspect ("Aspect (width / height)", Float) = 0.62

        [Header(Driven by PortalGate.cs)]
        _Open ("Open", Range(0, 1)) = 0
        _Pulse ("Seal Pulse", Range(0, 1)) = 0
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
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SealColor;
                half4 _CrackColor;
                half4 _RuneColor;
                half4 _DeepColor;
                half4 _BrightColor;
                half4 _CoreColor;
                half4 _RimColor;
                float _Twist;
                float _Spin;
                float _Aspect;
                float _Open;
                float _Pulse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 p : TEXCOORD0; // -1..1 across the arch
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = v.uv * 2.0 - 1.0;
                return o;
            }

            float Hash(float2 c)
            {
                return frac(sin(dot(c, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 x)
            {
                float2 i = floor(x), f = frac(x);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 x)
            {
                return Noise(x) * 0.55 + Noise(x * 2.03 + 7.1) * 0.3 + Noise(x * 4.1 + 13.7) * 0.15;
            }

            float2 Rotate(float2 v, float a)
            {
                float s = sin(a), c = cos(a);
                return float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float open = saturate(_Open);
                float2 p = i.p;
                float2 q = float2(p.x * _Aspect, p.y); // round space
                float r = length(q);

                // Arch shape: superellipse, soft edge.
                float2 a4 = p * p; a4 *= a4;
                float m = pow(a4.x + a4.y, 0.25);
                float aa = max(fwidth(m), 1e-3);
                float inside = 1.0 - smoothstep(0.96 - aa, 0.96 + aa, m);
                float rim = smoothstep(0.62, 0.95, m) * inside;

                // ---- Locked film + cracks
                float n = Fbm(q * 3.2 + float2(0.0, t * 0.03));
                float spread = smoothstep(0.05, 0.6, open);
                float crackWidth = lerp(0.025, 0.07, spread);
                float crack = 1.0 - smoothstep(0.0, crackWidth, abs(n - 0.5));
                crack *= 0.45 + 0.55 * smoothstep(0.9 - spread * 0.9, 0.0, r); // cracks spread out from the centre
                float pulse = 0.55 + 0.25 * sin(t * 2.1) + 0.2 * sin(t * 5.3 + n * 6.0);
                half3 crackRgb = lerp(_CrackColor.rgb, _RimColor.rgb * 1.3, spread);

                // Lock rune: ring + vertical bar + cross bar, fades out as the portal opens.
                float ringD = abs(r - 0.26);
                float rune = 1.0 - smoothstep(0.018, 0.03, ringD);
                rune = max(rune, (1.0 - smoothstep(0.02, 0.032, abs(q.x))) * step(abs(q.y), 0.34));
                rune = max(rune, (1.0 - smoothstep(0.02, 0.032, abs(q.y + 0.04))) * step(abs(q.x), 0.18));
                rune *= (1.0 - spread);

                float sealPulse = saturate(_Pulse) * (1.0 - spread); // KAN-49 seal touch flare (locked only)

                // Rim runes: five dots along the arch, lit one by one in the first half of the sequence.
                float runes = 0.0;
                [unroll] for (int k = 0; k < 5; k++)
                {
                    float ang = lerp(-2.4, 2.4, k / 4.0) + 1.5708; // left foot -> top -> right foot
                    float2 rp = float2(cos(ang) * 0.82, sin(ang) * 0.82);
                    float lit = max(smoothstep(k * 0.1, k * 0.1 + 0.08, open), sealPulse * 0.8);
                    runes += (1.0 - smoothstep(0.035, 0.06, length(p - rp))) * lit;
                }

                half3 film = _SealColor.rgb + crackRgb * crack * pulse + _CrackColor.rgb * rune * pulse;
                half filmA = saturate(_SealColor.a + crack * pulse * 0.4);
                film += (_RuneColor.rgb * rune * 1.8 + crackRgb * crack * 0.9) * sealPulse;
                filmA = saturate(filmA + crack * sealPulse * 0.3);

                // ---- Open swirl
                float twist = _Twist * (1.0 - r) - t * _Spin;
                float2 s = Rotate(q, twist);
                float sw = Fbm(s * 3.0 + float2(t * 0.15, 0.0));
                float bands = smoothstep(0.35, 0.75, sw);
                half3 swirl = lerp(_DeepColor.rgb, _BrightColor.rgb, bands);
                float core = exp(-r * r * 9.0);
                swirl += _CoreColor.rgb * core * (0.8 + 0.2 * sin(t * 3.0));
                swirl += _RimColor.rgb * rim * 0.9;
                half swirlA = saturate(0.82 + bands * 0.18);

                // ---- Mix by opening progress
                float reveal = smoothstep(0.6, 0.8, open);
                float flash = smoothstep(0.55, 0.65, open) * (1.0 - smoothstep(0.65, 0.85, open));
                half3 col = lerp(film, swirl, reveal) + _RuneColor.rgb * runes * (1.0 - reveal * 0.6) + flash * 1.4;
                half alpha = lerp(filmA, swirlA, reveal);
                alpha = saturate(alpha + runes * 0.5 + flash);

                alpha *= inside;
                return half4(col * alpha, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
