// Attack warning circle (KAN-136): a rune circle burnt into the ground instead of a flat red ring + disc.
// Two materials of this shader per circle warning (same two draw calls as before), one shared mask texture each:
//  * Fill (_Part 0, T_TelegraphDisc): the disc that grows from the centre while the attack winds up. Dark scorched earth
//    with ground cracks that glow more as the wind-up fills, a bright leading edge on the growing rim. The crack pattern
//    stays fixed on the ground (_FillScale = fill radius / full radius), so the fill uncovers it, it does not zoom.
//    Mask: R = crack (1 on a crack), G = scorch blotches.
//  * Ring (_Part 1, T_TelegraphRing): the danger edge. A dark backing (reads on white snow), a bold outer rim and a thin
//    inner rim with sundial notches (R), and between the rims a band of occult glyphs (G) that turns while the warning
//    shows (_RuneTurns = turns since shown, from GroundTelegraph). Wave mode (_Wave 1, GroundTelegraph.ShowWave): only a
//    shattered-earth band (B: 0.5 = earth chunk, 1 = glowing crack) at the rim share 0.92; KAN-132 remaps it round
//    _WaveRim with _WaveStretch, so a wave can show a band as wide as its hit band.
// Colours follow _BaseColor (tint + alpha from GroundTelegraph through one MaterialPropertyBlock): the hue of the tint
// gives the scorch, the ember glow and the hot glow, so red (boss / physical), green (Bloater poison) and violet (Shaman
// hex) all keep their colour. _Flare (0..1, the hit) blazes the runes and cracks. Premultiplied alpha, two texture fetches
// on the ring, one on the fill.
Shader "SurvivorSouls/TelegraphCircle"
{
    Properties
    {
        [Header(Style)]
        _Part ("Part (0 fill, 1 ring)", Float) = 0
        _MainTex ("Mask", 2D) = "black" {}
        _Outline ("Outline", Color) = (0.05, 0.01, 0.01, 0.9)
        _EmberShift ("Ember Shift (added to the tint hue)", Color) = (0, 0.3, 0.03, 0)
        _Hot ("Hot Share (ember to white)", Range(0, 1)) = 0.6
        _Wash ("Inner Wash", Range(0, 0.5)) = 0.12
        _Opacity ("Fill Opacity Boost", Range(0.5, 2)) = 1.35
        _Glow ("Glow (added light on cracks and glyphs)", Range(0, 1)) = 0.45

        [Header(Driven by GroundTelegraph.cs)]
        _BaseColor ("Base Colour", Color) = (0.62, 0.02, 0.01, 1)
        _Progress ("Progress", Range(0, 1)) = 0
        _FillScale ("Fill Scale", Range(0, 1)) = 1
        _RuneTurns ("Rune Turns", Float) = 0
        _Flare ("Flare", Range(0, 1)) = 0
        _Wave ("Wave", Range(0, 1)) = 0
        _WaveRim ("Wave Rim (front, share of the quad half size)", Float) = 0.92
        _WaveStretch ("Wave Stretch (texture share per quad share)", Float) = 1
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

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Part;
                half4 _Outline;
                half4 _EmberShift;
                half _Hot;
                half _Wash;
                half _Opacity;
                half _Glow;
                half4 _BaseColor;
                float _Progress;
                float _FillScale;
                float _RuneTurns;
                float _Flare;
                float _Wave;
                float _WaveRim;
                float _WaveStretch;
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

            // Tint palette: hue (brightest channel = 1), ember (hue warmed), hot (ember towards white), scorch (dark hue).
            void Palette(out half3 hue, out half3 ember, out half3 hot, out half3 scorch)
            {
                half m = max(max(_BaseColor.r, _BaseColor.g), max(_BaseColor.b, 1e-3));
                hue = _BaseColor.rgb / m;
                ember = saturate(hue + _EmberShift.rgb);
                hot = lerp(ember, half3(1, 1, 1), _Hot);
                scorch = hue * 0.16 + 0.02;
            }

            half4 Fill(float2 uv)
            {
                half3 hue, ember, hot, scorch;
                Palette(hue, ember, hot, scorch);
                float2 p = uv * 2.0 - 1.0;
                float r = length(p);
                float prog = saturate(_Progress);
                // Fixed ground pattern: this quad covers _FillScale of the full circle.
                half4 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p * (_FillScale * 0.5) + 0.5);
                half crack = m.r, blotch = m.g;

                half3 col = lerp(scorch, hue * 0.42, blotch * 0.8);
                half glow = crack * lerp(0.35, 1.0, prog);
                col = lerp(col, ember, glow);
                // Bright leading edge on the growing rim (the time-left read), fades out once full.
                half edge = smoothstep(0.78, 0.93, r) * (1.0 - smoothstep(0.95, 0.99, r)) * lerp(1.0, 0.45, prog * prog);
                col = lerp(col, hot, edge * 0.8);
                // The hit: cracks blaze, the burn between them warms up.
                half flare = saturate(_Flare);
                col = lerp(col, hot, flare * (0.3 + 0.7 * crack));

                // Burnt ground reads dark on white snow: more opaque than the old flat disc; cracks and the edge also add
                // light (premultiplied: rgb past rgb x alpha glows on the dark Realms too).
                half shape = lerp(0.85, 1.0, r * r) * (1.0 - smoothstep(0.93, 0.99, r));
                half a = saturate(shape * _BaseColor.a * _Opacity * (0.85 + 0.2 * glow + 0.3 * edge));
                half3 light = (ember * glow + hot * (edge * 0.6 + flare * 0.5)) * (_Glow * shape * saturate(_BaseColor.a * 1.5));
                return half4(col * a + light, a);
            }

            half4 Ring(float2 uv)
            {
                half3 hue, ember, hot, scorch;
                Palette(hue, ember, hot, scorch);
                float2 p = uv * 2.0 - 1.0;
                float r = length(p);
                half4 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                half flare = saturate(_Flare);

                if (_Wave > 0.5)
                {
                    // Shattered earth band: dark chunks with glowing cracks. KAN-132: the band is remapped round the front
                    // (_WaveRim) so its width in metres follows the hit band (GroundTelegraph.ShowWave band), not the radius.
                    float rt = saturate(0.92 + (r - _WaveRim) * _WaveStretch);
                    float2 dir = r > 1e-4 ? p / r : float2(1.0, 0.0);
                    half wb = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, dir * (rt * 0.5) + 0.5).b;
                    half earth = saturate(wb * 2.0);
                    half crackGlow = saturate(wb * 2.0 - 1.0);
                    half3 c = lerp(scorch * 1.6, hot, crackGlow);
                    half wa = earth * _BaseColor.a;
                    return half4(c * wa, wa);
                }

                // Turning glyph band: sample the rune channel with the uv rotated round the centre.
                float ang = _RuneTurns * 6.2831853;
                float s = sin(ang), c = cos(ang);
                float2 q = float2(p.x * c - p.y * s, p.x * s + p.y * c);
                half rune = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, q * 0.5 + 0.5).g;

                float prog = saturate(_Progress);
                half backing = m.a * _Outline.a;
                half3 lineRgb = lerp(hue * 0.75, ember, prog * 0.6 + flare);
                half3 runeRgb = lerp(ember, hot, saturate(prog * prog + flare));
                half runeA = rune * lerp(0.6, 1.0, prog) * lerp(1.0, 1.3, flare);
                half wash = r < 0.8 ? _Wash : 0.0;

                // Back to front: wash, dark backing, rims + notches, glyphs.
                half4 o = half4(hue * 0.5 * wash, wash);
                o = o * (1.0 - backing) + half4(_Outline.rgb * backing, backing);
                o = o * (1.0 - m.r) + half4(lineRgb * m.r, m.r);
                runeA = saturate(runeA);
                o = o * (1.0 - runeA) + half4(runeRgb * runeA, runeA);
                o.rgb += (runeRgb * runeA + lineRgb * m.r * 0.5) * _Glow; // glyphs and rims glow a little
                return o * saturate(_BaseColor.a);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return _Part < 0.5 ? Fill(i.uv) : Ring(i.uv);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
