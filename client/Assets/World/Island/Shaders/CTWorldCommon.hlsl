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

// World v3 (2026-10-22): the island's HDRI environment (IslandAtmosphere) and a compact PBR surface for the streets
// (roads, sidewalks, kerbs, plazas, downtown paving): the island's wrapped sun, shaped trilight fill and soft shadow,
// plus a GGX sun highlight and the HDRI reflection, so asphalt and stone catch the low sun like the PBR buildings do.
TEXTURECUBE(_CTWorldEnvCube); SAMPLER(sampler_CTWorldEnvCube);
float4 _CTWorldEnvParams;      // x = strength of the HDRI environment (0 = none), y = its mip count

half3 CTWorldEnv(half3 r, half perceptualRough)
{
    return SAMPLE_TEXTURECUBE_LOD(_CTWorldEnvCube, sampler_CTWorldEnvCube, r, perceptualRough * (_CTWorldEnvParams.y - 1.0)).rgb * _CTWorldEnvParams.x;
}

half3 CTWorldSurface(half3 albedo, half smooth, float3 positionWS, half3 n, half ao, half reflection)
{
    Light ml = GetMainLight();
    half ndl = dot(n, ml.direction);
    half wrapped = saturate((ndl + 0.35h) / 1.35h);
    half shadow = CTWorldShadow(positionWS) * ml.distanceAttenuation;
    bool tuned = _CTWorldAmbSky.a > 0.0;
    half keep = tuned ? (half)_CTWorldShadowParams.x : 0.35h;
    half3 direct = ml.color * (wrapped * lerp(keep, 1.0h, shadow));
    half fill = tuned ? lerp((half)_CTWorldShadowParams.z, 1.0h, wrapped * shadow) : 1.0h;
    half3 c = albedo * 0.96h * (direct + CTWorldAmbient(n) * fill * ao);
    half3 v = SafeNormalize(GetWorldSpaceViewDir(positionWS));
    half pr = 1.0h - smooth;
    half r = max(pr * pr, 0.0078125h);
    half r2 = r * r;
    half3 h = SafeNormalize(ml.direction + v);
    half nh = saturate(dot(n, h)), lh = saturate(dot(ml.direction, h));
    half d = nh * nh * (r2 - 1.0h) + 1.00001h;
    half spec = min(r2 / ((d * d) * max(0.1h, lh * lh) * (r * 4.0h + 2.0h)), 60.0h);
    c += 0.04h * spec * ml.color * (saturate(ndl) * shadow);
    if (_CTWorldEnvParams.x > 0.0)
    {
        half nv = saturate(dot(n, v));
        half f = 1.0h - nv; f *= f; f *= f;
        half envBRDF = lerp(0.04h, saturate(smooth + 0.04h), f) / (r2 + 1.0h);
        c += CTWorldEnv(reflect(-v, n), pr) * (envBRDF * ao * reflection * lerp(0.55h, 1.0h, shadow));
    }
    return c;
}

// LOD cross-fade for the island's instanced kit (IslandInstanceRenderer, 2026-10-22): during a switch both LODs draw with
// complementary screen-space dither masks instead of popping. fade > 0: the incoming LOD keeps the pixels whose noise is
// below the progress; fade < 0: the outgoing LOD keeps the rest. Only the _CTLODFADE variant (the renderer's fade
// material copies) has the discard, so ordinary opaque kit draws keep early depth on Mali.
void CTWorldLodFade(float2 positionCS, float fade)
{
    #if defined(_CTLODFADE)
        float n = frac(52.9829189 * frac(dot(floor(positionCS), float2(0.06711056, 0.00583715))));
        clip(fade > 0.0 ? fade - n : n + fade);
    #endif
}

#endif
