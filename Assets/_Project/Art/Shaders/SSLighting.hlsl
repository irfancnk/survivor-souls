// Shared cheap lighting for Survivor Souls world shaders (URP, unshadowed).
// Wrapped Lambert main light + SH ambient + additional point lights (per pixel or per vertex, as URP sets).
#ifndef SS_LIGHTING_INCLUDED
#define SS_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// wrap: 0 = plain Lambert, 0.5 = soft (snow scatters light into the shadow side).
half3 SSDiffuseLight(float3 positionWS, half3 normalWS, half wrap)
{
    Light main = GetMainLight();
    half ndl = saturate((dot(normalWS, main.direction) + wrap) / (1.0 + wrap));
    half3 light = main.color * ndl + SampleSH(normalWS);
    #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
    uint count = GetAdditionalLightsCount();
    for (uint i = 0u; i < count; ++i)
    {
        Light add = GetAdditionalLight(i, positionWS);
        half andl = saturate((dot(normalWS, add.direction) + 0.3) / 1.3);
        light += add.color * (add.distanceAttenuation * andl);
    }
    #endif
    return light;
}

#endif
