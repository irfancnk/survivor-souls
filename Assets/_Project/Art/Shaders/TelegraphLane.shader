// Attack warning lane (KAN-167, part of KAN-160): a burnt, cracked scorch on the ground instead of a flat red strip.
// Two materials of this shader per lane (same two draw calls as before):
//  * Fill (_Part 0): the quad that grows from the attacker to the far end while the attack winds up. Rust / dried-blood
//    scorch with torn noisy sides, cracks that widen with _Progress and glow with embers (hotter near the growing tip),
//    ember chevrons that flow from the attacker to the far end (direction even without the shape), a bright "snap"
//    (_Snap) just before the attack goes.
//  * Edge (_Part 1): the full-length quad: a dark torn outline on both sides (reads on white snow), a faint wash, and an
//    engraved sigil at the far end that pulses faster as the attack gets close.
// Per-renderer values come from LineTelegraph.cs through one MaterialPropertyBlock: _BaseColor (tint + alpha; the hit
// flash colour), _Span (this quad's length in m), _LaneWidth (m), _Progress (0..1), _Snap (0..1).
// UV: u across the lane, v 0 at the attacker .. 1 at this quad's far end. Premultiplied alpha, no texture fetch for
// the shape (value noise in the fragment, cheap: a few hashes per pixel on a small ground area).
Shader "SurvivorSouls/TelegraphLane"
{
    Properties
    {
        [Header(Style)]
        _Part ("Part (0 fill, 1 edge)", Float) = 0
        _ScorchDark ("Scorch Dark", Color) = (0.13, 0.02, 0.015, 1)
        _ScorchRust ("Scorch Rust", Color) = (0.42, 0.08, 0.03, 1)
        _Ember ("Ember", Color) = (1.0, 0.42, 0.08, 1)
        _EmberHot ("Ember Hot", Color) = (1.0, 0.8, 0.45, 1)
        _Outline ("Outline", Color) = (0.06, 0.01, 0.01, 0.9)
        _Tint ("Tint by Base Colour", Range(0, 1)) = 0.25
        _CrackScale ("Crack Cells per m", Range(0.3, 4)) = 1.4
        _ChevronSpacing ("Chevron Spacing (m)", Range(0.4, 4)) = 1.3
        _ChevronSweep ("Chevron Sweep (m)", Range(0, 1)) = 0.35
        _FlowSpeed ("Flow Speed (m/s)", Range(0, 8)) = 2.6
        _Torn ("Torn Edge", Range(0, 0.3)) = 0.12

        [Header(Driven by LineTelegraph.cs)]
        _BaseColor ("Base Colour", Color) = (0.5, 0.02, 0.01, 0.72)
        _Span ("Quad Length (m)", Float) = 8
        _LaneWidth ("Lane Width (m)", Float) = 2
        _Progress ("Progress", Range(0, 1)) = 0
        _Snap ("Snap", Range(0, 1)) = 0
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
                float _Part;
                half4 _ScorchDark;
                half4 _ScorchRust;
                half4 _Ember;
                half4 _EmberHot;
                half4 _Outline;
                half _Tint;
                float _CrackScale;
                float _ChevronSpacing;
                float _ChevronSweep;
                float _FlowSpeed;
                float _Torn;
                half4 _BaseColor;
                float _Span;
                float _LaneWidth;
                float _Progress;
                float _Snap;
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

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float2 Hash2(float2 p)
            {
                float h = Hash(p);
                return float2(h, Hash(p + h + 17.1));
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // Distance to the nearest Voronoi cell border (cracks), 3x3 cells.
            float CrackDistance(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 r = g + Hash2(i + g) * 0.8 + 0.1 - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                return sqrt(d2) - sqrt(d1);
            }

            half4 Fill(float2 uv)
            {
                float along = uv.y * _Span;                 // metres from the attacker
                float across = (uv.x - 0.5) * _LaneWidth;   // metres from the centre line
                float side = abs(uv.x * 2.0 - 1.0);         // 0 centre .. 1 side
                float p = saturate(_Progress);

                // Torn sides and a soft, ragged tip.
                float torn = (ValueNoise(float2(along * 1.9, uv.x > 0.5 ? 3.1 : 7.7)) - 0.5) * 2.0 * _Torn;
                float body = 1.0 - smoothstep(0.74 + torn, 0.9 + torn, side);
                body *= 1.0 - smoothstep(0.93, 1.0, uv.y);

                // Scorch: dark burn with rust patches.
                float blotch = ValueNoise(float2(along, across) * 1.3) * 0.65 + ValueNoise(float2(along, across) * 4.1) * 0.35;
                half3 col = lerp(_ScorchDark.rgb, _ScorchRust.rgb, smoothstep(0.35, 0.8, blotch));
                // Tint the burn a little by the base colour (the hit flash paints a hot one): stays dark, gets warmer.
                col = lerp(col, _BaseColor.rgb * 0.55, _Tint);

                // Cracks: thin at the start of the warning, wider and glowing as it fills; they snap wide at the end.
                float crackD = CrackDistance(float2(along, across) * _CrackScale);
                float crackW = lerp(0.03, 0.09, p) + _Snap * 0.07;
                float crack = 1.0 - smoothstep(crackW, crackW + 0.05, crackD);
                // Embers run from the attacker to the growing tip: hotter near the tip.
                float heat = lerp(0.35, 1.0, smoothstep(0.0, 1.0, uv.y)) * lerp(0.45, 1.0, p);
                half3 ember = lerp(_Ember.rgb, _EmberHot.rgb, saturate(heat * heat + _Snap));
                col = lerp(col, ember, crack * saturate(heat + _Snap));
                col += ember * crack * _Snap * 0.6; // the snap: cracks blaze, the burn between them stays dark

                // Chevrons flowing from the attacker to the far end (the tip leads, the sides trail).
                float c = frac((along - abs(across) * (_ChevronSweep / max(0.05, _LaneWidth * 0.5)) - _Time.y * _FlowSpeed) / _ChevronSpacing);
                float aa = max(fwidth(c), 1e-3);
                float chevron = smoothstep(0.62 - aa, 0.7, c) * (1.0 - smoothstep(0.82, 0.86 + aa, c)) * (1.0 - side * 0.6);
                col = lerp(col, ember, chevron * lerp(0.35, 0.75, p));

                col += _Ember.rgb * (_Snap * 0.12); // faint overall heat
                half a = body * saturate(lerp(0.78, 0.95, p) + crack * 0.1) * _BaseColor.a / 0.72;
                a = saturate(a);
                return half4(col * a, a);
            }

            half4 Edge(float2 uv)
            {
                float along = uv.y * _Span;
                float side = abs(uv.x * 2.0 - 1.0);
                float p = saturate(_Progress);
                float torn = (ValueNoise(float2(along * 1.9, uv.x > 0.5 ? 3.1 : 7.7)) - 0.5) * 2.0 * _Torn;

                // Dark torn outline along both sides (outside the fill's torn edge), faint wash inside.
                float rim = smoothstep(0.78 + torn, 0.86 + torn, side) * (1.0 - smoothstep(0.95 + torn * 0.5, 1.0, side));
                float wash = (1.0 - smoothstep(0.7, 0.84, side)) * 0.24;
                float start = smoothstep(0.0, min(0.4, _Span * 0.2), along);
                float endFade = 1.0 - smoothstep(_Span - 0.12, _Span, along);
                half4 o = half4(_Outline.rgb, 1.0) * (rim * _Outline.a * start * endFade);
                half3 washRgb = lerp(_ScorchRust.rgb, _BaseColor.rgb, 0.6);
                half washA = wash * start * endFade * (1.0 - o.a);
                o += half4(washRgb * washA, washA);

                // Sigil at the far end: a cracked ring, three inward spikes and a dot, pulsing faster near the attack.
                float r = _LaneWidth * 0.42;
                float2 q = float2((uv.x - 0.5) * _LaneWidth, along - (_Span - r - 0.08));
                float d = length(q);
                float aa = max(fwidth(d), 1e-3);
                float ring = 1.0 - smoothstep(0.05, 0.05 + aa, abs(d - r * 0.82));
                float ang = atan2(q.x, q.y);
                float spokeA = abs(frac(ang / 6.2831853 * 3.0 + 0.5) - 0.5) * 2.0; // 0 on the 3 spokes
                float spoke = (1.0 - smoothstep(0.06, 0.1, spokeA * d / max(r, 0.01) * 2.0))
                    * step(r * 0.25, d) * (1.0 - smoothstep(r * 0.7, r * 0.75, d));
                float dot_ = 1.0 - smoothstep(r * 0.12, r * 0.12 + aa, d);
                float gap = step(0.18, frac(ang / 6.2831853 * 5.0 + 0.1)); // broken (old, cracked) ring
                float sigil = saturate(ring * gap + spoke + dot_);
                float speed = lerp(3.0, 16.0, p * p);
                float pulse = 0.55 + 0.45 * sin(_Time.y * speed);
                half3 sigilRgb = lerp(_Ember.rgb, _EmberHot.rgb, saturate(pulse * p + _Snap));
                half sigilA = sigil * lerp(0.35, 1.0, p) * lerp(0.6, 1.0, pulse) * step(0.001, _Span - r * 2.0);
                o = o * (1.0 - sigilA) + half4(sigilRgb * sigilA, sigilA);

                return o * saturate(_BaseColor.a / 0.62);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                return _Part < 0.5 ? Fill(i.uv) : Edge(i.uv);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
