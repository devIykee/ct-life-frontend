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
    // World polish (2026-10-23): photo-scanned ground (art/pipeline/world_v3_ground.py, CC0 ambientCG / Poly Haven) in one
    // texture array: lawn grass at two scales and rotations (anti-tiling) under an aerial grass scan at 18 m and 61 m (macro
    // variation, also on the far LOD), soil for the dirt layer, and The Trenches' yards (_PaveMask green): worn concrete
    // slabs with joints, rectangular asphalt patch repairs, oil stains and puddles that mirror the sky. Fully paved pixels
    // skip the ground layers.
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
        _GroundArr("Ground scans (0 grass, 1 aerial grass, 2 soil, 3 concrete, 4 asphalt; a = smoothness)", 2DArray) = "" {}
        _GroundNrm("Ground scan normals (same slices)", 2DArray) = "" {}
        _GrassTint("Grass tint (rgb), macro hue mix (a)", Color) = (1.0, 0.97, 0.85, 0.35)
        _GrassScale("Grass tiles (m): x near, y second, z macro, w far macro", Vector) = (1.6, 2.3, 18, 61)
        _SoilTint("Soil tint", Color) = (1, 1, 1, 1)
        _YardTint("Trenches yard tint (rgb), stains (a)", Color) = (1, 1, 1, 0.6)
        _Yard("Yard: slab m, patch cell m, patch share, puddle share", Vector) = (3.0, 7.0, 0.33, 0.12)
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
        TEXTURE2D_ARRAY(_GroundArr); SAMPLER(sampler_GroundArr);
        TEXTURE2D_ARRAY(_GroundNrm);
        CBUFFER_START(UnityPerMaterial)
            float4 _Domain, _DetailScale, _Snow, _Shore, _PaveDomain, _PaveTile, _GrassScale, _Yard;
            half4 _RockColor, _RockColorHigh, _SandColor, _DirtColor, _SnowColor, _WetSand, _PaveTint, _GrassTint, _SoilTint, _YardTint;
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

        half3 GroundAlbedo(float3 positionWS, half3 normalWS, out half ao, out half3 nOut)
        {
            float2 uv = (positionWS.xz - _Domain.xy) / _Domain.z;
            half4 w = SAMPLE_TEXTURE2D(_Control, sampler_Control, uv);
            half4 macro = SAMPLE_TEXTURE2D(_Macro, sampler_Macro, uv);
            ao = macro.a;
            nOut = normalWS;
            float2 wp = positionWS.xz;
            const float2x2 rotA = float2x2(0.799, -0.602, 0.602, 0.799), rotB = float2x2(0.342, 0.940, -0.940, 0.342);
            // aerial grass scan: the large-scale patchiness of a real lawn (also on the far LOD, so the two agree)
            half4 aer = SAMPLE_TEXTURE2D_ARRAY(_GroundArr, sampler_GroundArr, mul(rotB, wp) / _GrassScale.w, 1);
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
            // grass: islandgen's painted macro keeps only its hue drift (normalised); the colour comes from the scans
            half macroLum = max(dot(macro.rgb, half3(0.3h, 0.55h, 0.15h)), 0.05h);
            half3 hue = lerp(half3(1, 1, 1), saturate(macro.rgb / macroLum * 0.55h), _GrassTint.a);
            #if defined(_FAR)
                half3 pf = aer.rgb * 16.6h;                         // the aerial scan over its mean luminance (linear)
                half3 grass = half3(0.099h, 0.125h, 0.028h) * lerp(half3(1, 1, 1), lerp(dot(pf, half3(0.2126h, 0.7152h, 0.0722h)).xxx, pf, 0.2h), 0.7h)
                            * hue * _GrassTint.rgb;
            #else
                half4 a1 = SAMPLE_TEXTURE2D_ARRAY(_GroundArr, sampler_GroundArr, wp / _GrassScale.z, 1);
                half4 g1 = SAMPLE_TEXTURE2D_ARRAY(_GroundArr, sampler_GroundArr, wp / _GrassScale.x, 0);
                half4 g2 = SAMPLE_TEXTURE2D_ARRAY(_GroundArr, sampler_GroundArr, mul(rotA, wp) / _GrassScale.y + 0.31, 0);
                half mixN = smoothstep(0.35h, 0.65h, d2.r + (a1.g - 0.25h));
                half3 lawn = lerp(g1.rgb, g2.rgb, mixN);
                // the aerial scan's dry and mossy patches, at two scales, over the lawn
                half3 pa = lerp(a1.rgb, aer.rgb, 0.4h) * 16.6h;     // over the scan's mean luminance (linear)
                half3 patchy = lerp(dot(pa, half3(0.2126h, 0.7152h, 0.0722h)).xxx, pa, 0.2h);
                half3 grass = lawn * lerp(half3(1, 1, 1), patchy, 0.7h) * hue * _GrassTint.rgb;
                if (w.r > 0.02h)
                {
                    half3 gn = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_GroundNrm, sampler_GroundArr, wp / _GrassScale.x, 0));
                    nOut = normalize(lerp(nOut, normalize(normalWS * gn.z + half3(gn.x, 0.0h, gn.y)), w.r * 0.8h));
                }
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
            #if defined(_FAR)
                half3 dirt = _DirtColor.rgb * lerp(1.0h, macro.rgb * 1.8h, 0.2h);
            #else
                half3 dirt = _DirtColor.rgb * (1.0h + (det.a - 0.5h) * k) * lerp(1.0h, macro.rgb * 1.8h, 0.2h);
                if (w.a > 0.02h)
                {
                    float2 suv = mul(rotA, wp) / 3.2;
                    half3 soil = SAMPLE_TEXTURE2D_ARRAY(_GroundArr, sampler_GroundArr, suv, 2).rgb * _SoilTint.rgb;
                    dirt = soil;
                    half3 sn = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_GroundNrm, sampler_GroundArr, suv, 2));
                    nOut = normalize(lerp(nOut, normalize(normalWS * sn.z + half3(sn.x, 0.0h, sn.y)), w.a));
                }
            #endif
            half3 albedo = grass * w.r + rock * w.g + sand * w.b + dirt * w.a;
            #if defined(_FAR)
                // far terrain: islandgen bakes the composited ground colour (terrain_far_macro); its grass is repainted with
                // the scans' tone so the far LOD meets the near terrain
                albedo = lerp(macro.rgb, grass, w.r * 0.85h);
            #endif
            // snow only on the highest ground, mostly on the shaded north faces (normal +z = facing north), never on cliffs
            half north = lerp(1.0h, smoothstep(-0.05h, 0.35h, normalWS.z), _Snow.w);
            half cap = smoothstep(_Snow.y + 8.0h, _Snow.y + 16.0h, positionWS.y);
            half snow = smoothstep(_Snow.x, _Snow.y, positionWS.y + (det.g - 0.5h) * 10.0h) * smoothstep(_Snow.z, _Snow.z + 0.15h, normalWS.y) * max(north, cap);
            albedo = lerp(albedo, _SnowColor.rgb * (0.94h + det.g * 0.08h), snow);
            return albedo;
        }

        // The Trenches' yards (_PaveMask green): worn concrete slabs with joints, rectangular asphalt patch repairs (one
        // array sample either way), oil stains and puddles from two low-frequency detail samples; puddles mirror the sky.
        float YardHash(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }
        float YardValue(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(YardHash(i), YardHash(i + float2(1, 0)), u.x), lerp(YardHash(i + float2(0, 1)), YardHash(i + float2(1, 1)), u.x), u.y);
        }
        half YardNoise(float2 p) { return (half)(YardValue(p) * 0.65 + YardValue(p * 2.3 + 5.1) * 0.35); }

        half3 YardColor(float3 positionWS, half3 n)
        {
            float2 wp = positionWS.xz;
            const float2x2 rotA = float2x2(0.799, -0.602, 0.602, 0.799);
            float2 pcell = wp / _Yard.y;
            float2 cid = floor(pcell), f = frac(pcell);
            float3 q3 = frac(float3(cid.xyx + 41.0) * 0.1031); q3 += dot(q3, q3.yzx + 33.33);
            float3 hh = frac((q3.xxy + q3.yzz) * q3.zyx);
            bool patch = hh.x < _Yard.z && all(f > hh.yz * 0.3) && all(f < 0.7 + hh.zy * 0.3);
            float2 tuv = (patch ? mul(rotA, wp) : wp) / _Yard.x;
            float slice = patch ? 4.0 : 3.0;
            half4 ya = SAMPLE_TEXTURE2D_ARRAY(_GroundArr, sampler_GroundArr, tuv, slice);
            half3 yn = UnpackNormal(SAMPLE_TEXTURE2D_ARRAY(_GroundNrm, sampler_GroundArr, tuv, slice));
            // slab joints (concrete only) and a tone per slab
            float2 j = abs(frac(wp / _Yard.x + 0.5) - 0.5) * _Yard.x;
            half joint = patch ? 0.0h : 1.0h - smoothstep(0.008h, 0.025h, (half)min(j.x, j.y));
            float2 sid = floor(wp / _Yard.x) + 7.0;
            float3 s3 = frac(float3(sid.xyx) * 0.1031); s3 += dot(s3, s3.yzx + 33.33);
            half slab = (half)frac((s3.x + s3.y) * s3.z) - 0.5h;
            // oil stains and puddles: smooth value noise (two octaves, ALU only), thresholded into soft-edged blobs
            half oil = smoothstep(0.66h, 0.72h, YardNoise(wp / 3.3 + 17.0)) * _YardTint.a;
            half puddle = smoothstep(0.70h - _Yard.w, 0.74h - _Yard.w, YardNoise(wp / 7.5 + 3.0));
            half3 alb = ya.rgb * _YardTint.rgb * (1.0h - joint * 0.55h) * (1.0h + slab * (patch ? 0.04h : 0.14h));
            alb *= lerp(1.0h, 0.45h, oil);
            half sm = lerp(ya.a * 0.8h, 0.5h, oil);
            alb *= lerp(1.0h, 0.55h, puddle);
            sm = lerp(sm, 0.82h, puddle);
            half3 pn = normalize(n * yn.z + half3(yn.x, 0.0h, yn.y) * (1.0h - puddle));
            return CTWorldSurface(alb, sm, positionWS, pn, 1.0h, lerp(1.0h, 0.6h, puddle));
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 n = normalize(input.normalWS);
            float2 puv = (input.positionWS.xz - _PaveDomain.xy) / _PaveDomain.zw;
            half2 pm = all(puv == saturate(puv)) ? SAMPLE_TEXTURE2D(_PaveMask, sampler_PaveMask, puv).rg : half2(0.0h, 0.0h);
            half pave = pm.r;
            half3 c = 0;
            if (pave < 0.997h)
            {
                half ao;
                half3 gn;
                half3 albedo = GroundAlbedo(input.positionWS, n, ao, gn);
                c = CTWorldLight(albedo, input.positionWS, gn, ao, _Toon) * lerp(0.82h, 1.0h, ao);
            }
            if (pave > 0.003h)
            {
                #if defined(_FAR)
                    half3 pc = CTWorldLight(lerp(_PaveTint.rgb * 0.19h, half3(0.085h, 0.085h, 0.08h), pm.g), input.positionWS, n, 1.0h, 0.0h);
                #else
                    half3 pc;
                    if (pm.g > 0.5h) pc = YardColor(input.positionWS, n);
                    else
                    {
                        float2 tuv = input.positionWS.xz / _PaveTile.x;
                        half4 pa = SAMPLE_TEXTURE2D(_PaveAlbedo, sampler_PaveAlbedo, tuv);
                        half3 tn = UnpackNormal(SAMPLE_TEXTURE2D(_PaveNormal, sampler_PaveNormal, tuv));
                        float2 cell = floor(tuv * _PaveTile.y) + 17.0;
                        float3 p3 = frac(float3(cell.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33);
                        half slab = (half)frac((p3.x + p3.y) * p3.z) - 0.5h;
                        half3 pn = normalize(n * tn.z + half3(tn.x, 0.0h, tn.y));
                        pc = CTWorldSurface(pa.rgb * _PaveTint.rgb * (1.0h + slab * _PaveTile.z * 2.0h), pa.a, input.positionWS, pn, 1.0h, 1.0h);
                    }
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
            #pragma target 3.5
            #pragma require 2darray
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
