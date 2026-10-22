Shader "CT Life/World/Terrain"
{
    // CT Island v2 ground (World lane). Mobile-cheap splat: one control map (grass, rock, sand, dirt), one painted macro
    // tint (rgb) with baked cavity AO (a), and ONE packed detail texture (r grass, g rock, b sand, a dirt, all tiling in
    // world metres) sampled at two scales. Colours come from the macro tint and per-layer colours; the detail only
    // modulates them, which keeps the look painterly. Height-based weight sharpening, a snow line on gentle high ground.
    // The far LOD (_FAR) skips the detail texture.
    // World v3 (2026-10-22): downtown is paved, not grass. _PaveMask (1 m over Timeline Downtown, dress.py v3_paving)
    // replaces the ground with the sidewalk stone (Poly Haven concrete_pavement_02, CC0) lit like the streets; outside the
    // mask it costs one extra sample. The far LOD paves with the stone's flat tone.
    Properties
    {
        _Control("Control (r grass, g rock, b sand, a dirt)", 2D) = "red" {}
        _Macro("Macro tint (rgb) + AO (a)", 2D) = "white" {}
        _Detail("Packed detail (r grass, g rock, b sand, a dirt)", 2D) = "gray" {}
        _Domain("Domain (min x, min z, size, 0)", Vector) = (-1024, -1024, 2048, 0)
        _DetailScale("Detail repeats per metre (x near, y far)", Vector) = (0.16, 0.035, 0, 0)
        _RockColor("Rock", Color) = (0.50, 0.48, 0.50, 1)
        _RockColorHigh("Rock (high)", Color) = (0.64, 0.62, 0.66, 1)
        _SandColor("Sand", Color) = (0.93, 0.84, 0.62, 1)
        _DirtColor("Dirt", Color) = (0.58, 0.44, 0.30, 1)
        _SnowColor("Snow", Color) = (0.97, 0.97, 1.0, 1)
        _Snow("Snow (start y, full y, min normal y, north bias)", Vector) = (138, 156, 0.45, 0.6)
        _WetSand("Wet sand (colour)", Color) = (0.60, 0.52, 0.40, 1)
        _Shore("Shore (sea level y, wet band height m, 0, 0)", Vector) = (-6, 1.1, 0, 0)
        _Contrast("Detail contrast", Range(0, 2)) = 0.7
        _Sharpness("Blend sharpness", Range(1, 8)) = 2.5
        _Toon("Toon step", Range(0, 1)) = 0.35
        _PaveMask("Downtown paving mask", 2D) = "black" {}
        _PaveDomain("Paving domain (min x, min z, size x, size z)", Vector) = (0, 0, 1, 1)
        _PaveAlbedo("Paving (rgb colour x AO, a smoothness)", 2D) = "grey" {}
        [Normal] _PaveNormal("Paving normal", 2D) = "bump" {}
        _PaveTint("Paving tint", Color) = (1, 1, 1, 1)
        _PaveTile("Paving tile (m), slabs a tile, slab variation", Vector) = (1.8, 4, 0.08, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-10" }
        HLSLINCLUDE
        #include "CTWorldCommon.hlsl"
        TEXTURE2D(_Control); SAMPLER(sampler_Control);
        TEXTURE2D(_Macro); SAMPLER(sampler_Macro);
        TEXTURE2D(_Detail); SAMPLER(sampler_Detail);
        TEXTURE2D(_PaveMask); SAMPLER(sampler_PaveMask);
        TEXTURE2D(_PaveAlbedo); SAMPLER(sampler_PaveAlbedo);
        TEXTURE2D(_PaveNormal); SAMPLER(sampler_PaveNormal);
        CBUFFER_START(UnityPerMaterial)
            float4 _Domain, _DetailScale, _Snow, _Shore, _PaveDomain, _PaveTile;
            half4 _RockColor, _RockColorHigh, _SandColor, _DirtColor, _SnowColor, _WetSand, _PaveTint;
            half _Contrast, _Sharpness, _Toon;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            half3 normalWS : TEXCOORD1;
            half fog : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            o.positionCS = p.positionCS;
            o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        half3 GroundAlbedo(float3 positionWS, half3 normalWS, out half ao)
        {
            float2 uv = (positionWS.xz - _Domain.xy) / _Domain.z;
            half4 w = SAMPLE_TEXTURE2D(_Control, sampler_Control, uv);
            half4 macro = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, uv);
            ao = macro.a;
            #if defined(_FAR)
                half4 det = half4(0.5h, 0.5h, 0.5h, 0.5h);
                half4 d1 = det, d2 = det;
            #else
                half4 d1 = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, positionWS.xz * _DetailScale.x);
                half4 d2 = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, positionWS.xz * _DetailScale.y + 0.37);
                half4 det = lerp(d1, d2, 0.4h);
                // height-blend: the detail "heights" sharpen the transitions into natural edges
                w = pow(max(w * (det + 0.25h), 1e-4h), _Sharpness);
            #endif
            w /= max(dot(w, half4(1, 1, 1, 1)), 1e-4h);
            half k = _Contrast;
            half3 grass = macro.rgb * (1.0h + (det.r - 0.5h) * k * 0.9h);
            // hue drift in the grass (yellow-green <-> blue-green), from the dirt/sand detail channels at the far scale
            #if !defined(_FAR)
                half drift = (d2.a - d2.b) * 1.6h;
                grass *= lerp(half3(1.0h, 1.0h, 1.0h), drift > 0 ? half3(1.10h, 1.04h, 0.72h) : half3(0.86h, 1.0h, 1.10h), saturate(abs(drift)));
            #endif
            half rockHigh = saturate((positionWS.y - 40.0) / 110.0);
            half rockDet = det.g;
            #if !defined(_FAR)
                // cliffs: tri-planar rock detail (no smearing down steep faces) and soft strata bands
                half3 bw = pow(abs(normalWS), 4.0h); bw /= max(dot(bw, half3(1, 1, 1)), 1e-3h);
                half rx = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, positionWS.zy * _DetailScale.x * 0.8).g;
                half rz = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, positionWS.xy * _DetailScale.x * 0.8).g;
                rockDet = rx * bw.x + d1.g * bw.y + rz * bw.z;
                half strata = sin(positionWS.y * 0.55h + (rx + rz) * 5.0h + d2.g * 6.0h) * 0.5h + 0.5h;
                rockDet = lerp(rockDet, rockDet * (0.9h + 0.2h * strata), saturate(1.0h - normalWS.y * 1.4h));
            #endif
            half3 rock = lerp(_RockColor.rgb, _RockColorHigh.rgb, rockHigh) * (1.0h + (rockDet - 0.5h) * k * 1.3h) * lerp(1.0h, macro.rgb * 1.6h, 0.06h);
            rock *= lerp(0.72h, 1.0h, ao);                     // crevices darker
            // rock strata: alternating cool-grey and warm-ochre beds (~7.5 m), strongest on steep faces; the far terrain
            // bakes the same bands (textures.far_albedo), so the range reads layered from downtown too
            {
                half bedNoise = (macro.r - macro.g) * 3.0h;
                #if !defined(_FAR)
                    bedNoise += (rx + rz) * 1.2h;
                #endif
                half bed = smoothstep(0.3h, 0.7h, sin(positionWS.y * 0.42h + bedNoise) * 0.5h + 0.5h);
                half3 bedTint = lerp(half3(0.80h, 0.79h, 0.84h), half3(1.14h, 1.02h, 0.86h), bed);
                rock *= lerp(half3(1, 1, 1), bedTint, clamp(1.0h - normalWS.y * 1.2h, 0.35h, 1.0h));
            }
            half3 sand = _SandColor.rgb * (1.0h + (det.b - 0.5h) * k * 0.6h);
            // wet sand: a darker band just above the waterline (follows the real shore height)
            half wet = smoothstep(_Shore.x + _Shore.y, _Shore.x + 0.15h, positionWS.y + (det.b - 0.5h) * 0.3h);
            sand = lerp(sand, _WetSand.rgb * (1.0h + (det.b - 0.5h) * k * 0.5h), wet);
            half3 dirt = _DirtColor.rgb * (1.0h + (det.a - 0.5h) * k) * lerp(1.0h, macro.rgb * 1.8h, 0.2h);
            half3 albedo = grass * w.r + rock * w.g + sand * w.b + dirt * w.a;
            #if defined(_FAR)
                albedo = macro.rgb;            // far terrain: islandgen bakes the composited ground colour (terrain_far_macro)
            #endif
            // snow only on the highest ground, mostly on the shaded north faces (normal +z = facing north), never on cliffs
            half north = lerp(1.0h, smoothstep(-0.05h, 0.35h, normalWS.z), _Snow.w);
            half cap = smoothstep(_Snow.y + 8.0h, _Snow.y + 16.0h, positionWS.y);
            half snow = smoothstep(_Snow.x, _Snow.y, positionWS.y + (det.g - 0.5h) * 10.0h) * smoothstep(_Snow.z, _Snow.z + 0.15h, normalWS.y) * max(north, cap);
            albedo = lerp(albedo, _SnowColor.rgb * (0.94h + det.g * 0.08h), snow);
            return albedo;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 n = normalize(input.normalWS);
            half ao;
            half3 albedo = GroundAlbedo(input.positionWS, n, ao);
            half3 c = CTWorldLight(albedo, input.positionWS, n, ao, _Toon) * lerp(0.82h, 1.0h, ao);
            float2 puv = (input.positionWS.xz - _PaveDomain.xy) / _PaveDomain.zw;
            half pave = all(puv == saturate(puv)) ? SAMPLE_TEXTURE2D(_PaveMask, sampler_PaveMask, puv).r : 0.0h;
            if (pave > 0.003h)
            {
                #if defined(_FAR)
                    half3 pc = CTWorldLight(_PaveTint.rgb * 0.19h, input.positionWS, n, 1.0h, 0.0h);
                #else
                    float2 tuv = input.positionWS.xz / _PaveTile.x;
                    half4 pa = SAMPLE_TEXTURE2D(_PaveAlbedo, sampler_PaveAlbedo, tuv);
                    half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_PaveNormal, sampler_PaveNormal, tuv));
                    float2 cell = floor(tuv * _PaveTile.y) + 17.0;
                    float3 p3 = frac(float3(cell.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33);
                    half slab = (half)frac((p3.x + p3.y) * p3.z) - 0.5h;
                    half3 pn = normalize(n * tn.z + half3(tn.x, 0.0h, tn.y));
                    half3 pc = CTWorldSurface(pa.rgb * _PaveTint.rgb * (1.0h + slab * _PaveTile.z * 2.0h), pa.a, input.positionWS, pn, 1.0h, 1.0h);
                #endif
                c = lerp(c, pc, pave);
            }
            c = MixFog(c, input.fog);
            c = CTWorldHaze(c, input.positionWS);
            return half4(c, 1);
        }

        // shadow caster / depth
        float3 _LightDirection;
        float4 ShadowVert(Attributes input) : SV_POSITION
        {
            UNITY_SETUP_INSTANCE_ID(input);
            float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 nWS = TransformObjectToWorldNormal(input.normalOS);
            float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
            return cs;
        }
        float4 DepthVert(Attributes input) : SV_POSITION
        {
            UNITY_SETUP_INSTANCE_ID(input);
            return TransformObjectToHClip(input.positionOS.xyz);
        }
        half4 NullFrag() : SV_Target { return 0; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma shader_feature_local _FAR
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment NullFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment NullFrag
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
