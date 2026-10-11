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

float4 _CTWorldAmbSky;         // trilight ambient set by the island (a = 1 when set; else the engine SH is used)
float4 _CTWorldAmbEquator;
float4 _CTWorldAmbGround;
float4 _CTWorldShadowParams;   // x = sun left in shadow (0..1), y = PCF radius in texels, z = ambient in shadow, w unused

// Ambient from the island's own trilight (sky above, equator at the horizon, ground below). Per-object SH proved
// unreliable on the island (zero, or garbage near instantiated venue prefabs), and this gives the art direction a
// direct handle on fill-light contrast.
half3 CTWorldAmbient(half3 n)
{
    if (_CTWorldAmbSky.a <= 0.0) return SampleSH(n);
    half up = n.y;
    return up >= 0.0h ? lerp(_CTWorldAmbEquator.rgb, _CTWorldAmbSky.rgb, up) : lerp(_CTWorldAmbEquator.rgb, _CTWorldAmbGround.rgb, -up);
}

// Main-light shadow with a cheap 4-tap PCF (each tap a hardware 2x2 compare) and the distance fade, independent of
// the URP asset's soft-shadow switch, which other lanes share.
half CTWorldShadow(float3 positionWS)
{
    #if !defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        return 1.0h;
    #else
        float4 sc = TransformWorldToShadowCoord(positionWS);
        float r = max(_CTWorldShadowParams.y, 0.0) ;
        float2 t = _MainLightShadowmapSize.xy * r;
        half4 a4;
        a4.x = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, sc.xyz + float3(-t.x, -t.y * 0.4, 0));
        a4.y = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, sc.xyz + float3( t.x * 0.4, -t.y, 0));
        a4.z = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, sc.xyz + float3( t.x,  t.y * 0.4, 0));
        a4.w = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, sc.xyz + float3(-t.x * 0.4,  t.y, 0));
        half a = dot(a4, 0.25h);
        a = LerpWhiteTo(a, GetMainLightShadowParams().x);
        a = BEYOND_SHADOW_FAR(sc) ? 1.0h : a;
        return lerp(a, 1.0h, GetMainLightShadowFade(positionWS));
    #endif
}

half3 CTWorldLight(half3 albedo, float3 positionWS, half3 normalWS, half ao, half toon)
{
    Light mainLight = GetMainLight();
    half ndl = dot(normalWS, mainLight.direction);
    half wrapped = saturate((ndl + 0.35h) / 1.35h);
    half stepped = smoothstep(0.28h, 0.5h, wrapped);
    half diffuse = lerp(wrapped, stepped * 0.85h + wrapped * 0.15h, toon);
    half shadow = CTWorldShadow(positionWS) * mainLight.distanceAttenuation;
    bool tuned = _CTWorldAmbSky.a > 0.0;
    half keep = tuned ? (half)_CTWorldShadowParams.x : 0.35h;       // sun that still reaches shadowed surfaces
    half3 direct = mainLight.color * (diffuse * lerp(keep, 1.0h, shadow));
    // shadowed and sun-averted surfaces get a slightly reduced, cooler fill, so the sun direction reads
    half lit = diffuse * shadow;
    half fill = tuned ? lerp((half)_CTWorldShadowParams.z, 1.0h, lit) : 1.0h;
    half3 ambient = CTWorldAmbient(normalWS) * fill;
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
