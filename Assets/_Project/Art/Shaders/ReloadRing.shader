// Reload ring under the Knight (KAN-42). Flat quad on the floor, one draw call, no texture. The fragment shader draws
// a dim track, a bright arc that grows clockwise from 12 o'clock with _Progress (0..1, atan2 in the fragment), a bright
// head at the arc tip, a thin dark outline so the ring reads on white snow, and a soft outer glow. Premultiplied alpha.
// Per-frame values (_Progress, _Flash, _Pulse, _Calm, _Alpha) come from ReloadRing.cs through one MaterialPropertyBlock;
// the style values are material properties (M_ReloadRing.mat).
// UV space: centre (0.5, 0.5), +V = world +Z (screen up) because the script keeps the quad world-aligned.
Shader "SurvivorSouls/ReloadRing"
{
    Properties
    {
        [Header(Style)]
        _TrackColor ("Track", Color) = (0.58, 0.2, 0.02, 0.6)
        _ArcColor ("Arc", Color) = (1.0, 0.52, 0.06, 1)
        _HeadColor ("Head and Flash", Color) = (1.0, 0.9, 0.62, 1)
        _OutlineColor ("Outline", Color) = (0.1, 0.04, 0.01, 0.85)
        _Radius ("Radius (UV, 1 = quad edge)", Range(0.2, 0.95)) = 0.7
        _Thickness ("Thickness", Range(0.01, 0.3)) = 0.14
        _Outline ("Outline Width", Range(0, 0.1)) = 0.035
        _HeadSize ("Head Size (x half thickness)", Range(0.5, 3)) = 1.3
        _Glow ("Glow", Range(0, 1)) = 0.35
        _GlowWidth ("Glow Width", Range(0.01, 0.3)) = 0.11
        _CalmDim ("Calm Arc Brightness", Range(0, 1)) = 0.7
        _PulseGrow ("Pulse Radius Grow", Range(0, 0.2)) = 0.07
        _Opacity ("Opacity", Range(0, 1)) = 0.85

        [Header(Driven by ReloadRing.cs)]
        _Progress ("Progress", Range(0, 1)) = 1
        _Flash ("Ready Flash", Range(0, 1)) = 0
        _Pulse ("Fire Pulse", Range(0, 1)) = 0
        _Calm ("Calm (no target)", Range(0, 1)) = 1
        _Alpha ("Alpha Multiplier", Range(0, 1)) = 1
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
                half4 _TrackColor;
                half4 _ArcColor;
                half4 _HeadColor;
                half4 _OutlineColor;
                float _Radius;
                float _Thickness;
                float _Outline;
                float _HeadSize;
                half _Glow;
                float _GlowWidth;
                half _CalmDim;
                float _PulseGrow;
                half _Opacity;
                float _Progress;
                half _Flash;
                half _Pulse;
                half _Calm;
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
                float2 p : TEXCOORD0; // -1..1 around the centre
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = v.uv * 2.0 - 1.0;
                return o;
            }

            // 1 inside |d| < halfWidth, 0 outside, anti-aliased over one pixel.
            float Band(float d, float halfWidth, float aa)
            {
                return 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, d);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                const float TAU = 6.2831853;
                float2 p = i.p;
                float r = length(p);
                float aa = max(fwidth(r), 1e-4);

                float radius = _Radius + _Pulse * _PulseGrow;
                float halfT = _Thickness * 0.5;
                float d = abs(r - radius);
                float ring = Band(d, halfT, aa);
                float rim = saturate(Band(d, halfT + _Outline, aa) - ring);

                // Clockwise turn from 12 o'clock, 0..1. Anti-alias the arc end in arc length, not in t (t jumps at the seam).
                float t = frac(atan2(p.x, p.y) / TAU + 1.0);
                float progress = saturate(_Progress);
                float arcAa = aa / max(r * TAU, 1e-3);
                float arc = progress >= 0.9999 ? 1.0 : (1.0 - smoothstep(progress - arcAa, progress, t)) * step(1e-4, progress);

                // Head: a dot at the arc tip while reloading.
                float headA = progress * TAU;
                float2 headP = radius * float2(sin(headA), cos(headA));
                float reloading = step(1e-4, progress) * step(progress, 0.9999);
                float head = Band(length(p - headP), halfT * _HeadSize, aa) * reloading;

                half3 arcRgb = _ArcColor.rgb * lerp(1.0, _CalmDim, _Calm);
                half3 col = lerp(_TrackColor.rgb, arcRgb, arc);
                half a = lerp(_TrackColor.a, _ArcColor.a, arc);
                float boost = saturate(_Flash + _Pulse * 0.6);
                col = lerp(col, _HeadColor.rgb, boost * 0.75);
                a = saturate(a + boost * 0.3);

                // Premultiplied: ring body, then the head over it, the dark rim around it and the glow outside it.
                half4 o = half4(col * a, a) * ring;
                half headMix = head * _HeadColor.a;
                o = lerp(o, half4(_HeadColor.rgb, 1.0) * headMix, headMix);
                half rimA = rim * _OutlineColor.a * (1.0 - head);
                o += half4(_OutlineColor.rgb * rimA, rimA) * (1.0 - o.a);
                float glowFall = exp(-(d * d) / (_GlowWidth * _GlowWidth));
                half glowA = _Glow * glowFall * max(arc * lerp(1.0, _CalmDim, _Calm), head) * (1.0 - ring) * (1.0 + boost);
                o += half4(arcRgb * glowA, glowA * 0.6) * (1.0 - o.a);

                return o * (_Opacity * _Alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
