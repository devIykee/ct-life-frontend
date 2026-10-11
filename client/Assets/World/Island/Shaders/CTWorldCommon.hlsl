#ifndef CTWORLD_COMMON_INCLUDED
#define CTWORLD_COMMON_INCLUDED

// Shared lighting for the CT Island v2 world shaders (World lane). Stylised: wrapped diffuse with a soft toon step,
// spherical-harmonics ambient, the main light's realtime shadow, URP fog, plus a warm distance haze towards the sun
// (_CTWorldHaze* globals set by the island's atmosphere; zero when absent).

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

float4 _CTWorldHazeColor;      // rgb haze colour, a = strength (0 off)
float4 _CTWorldHazeParams;     // x = start distance, y = full distance, z = height falloff (1/m), w = sun-glow strength
float4 _CTWorldSunGlow;        // rgb glow colour towards the sun

half3 CTWorldLight(half3 albedo, float3 positionWS, half3 normalWS, half ao, half toon)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    half ndl = dot(normalWS, mainLight.direction);
    half wrapped = saturate((ndl + 0.35h) / 1.35h);
    half stepped = smoothstep(0.28h, 0.5h, wrapped);
    half diffuse = lerp(wrapped, stepped * 0.85h + wrapped * 0.15h, toon);
    half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
    half3 direct = mainLight.color * (diffuse * lerp(0.35h, 1.0h, shadow));
    half3 ambient = SampleSH(normalWS);
    return albedo * (direct + ambient * ao);
}

half3 CTWorldHaze(half3 color, float3 positionWS)
{
    if (_CTWorldHazeColor.a <= 0.0) return color;
    float3 toPixel = positionWS - _WorldSpaceCameraPos;
    float dist = length(toPixel);
    float h = saturate((dist - _CTWorldHazeParams.x) / max(1.0, _CTWorldHazeParams.y - _CTWorldHazeParams.x));
    float heightFade = exp(-max(0.0, positionWS.y + 6.0) * _CTWorldHazeParams.z);
    float amount = saturate(h * h * (3.0 - 2.0 * h) * lerp(0.55, 1.0, heightFade) * _CTWorldHazeColor.a);
    Light sun = GetMainLight();
    float toward = pow(saturate(dot(normalize(toPixel), sun.direction) * 0.5 + 0.5), 6.0) * _CTWorldHazeParams.w;
    half3 haze = lerp(_CTWorldHazeColor.rgb, _CTWorldSunGlow.rgb, saturate(toward));
    return lerp(color, haze, amount);
}

#endif
