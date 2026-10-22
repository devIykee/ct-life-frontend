Shader "CT Life/World/Kit PBR"
{
    // CT Island world v3 kit (World lane, 2026-10-22): the semi-real buildings, props and furniture (art/processed/world_v3)
    // drawn with GPU instancing by IslandInstanceRenderer. Same light and air as CT Life/World/Kit (the island's trilight
    // ambient, 4-tap soft sun shadow, fog and aerial haze), plus what the PBR atlases carry: a tangent-space normal map
    // (OpenGL), a mask map (R metallic, G occlusion, A smoothness), a GGX sun highlight and a procedural sky reflection
    // (no cubemap: zenith, horizon and ground colours plus the sun glow from IslandAtmosphere), so glass and wet-looking
    // stone pick up the golden-hour sky without a reflection probe. Per instance: _Tint (rgb multiplies the albedo when
    // a > 0, scaled by _TintStrength: small colour shifts so repeated facades read as different buildings; a parked decor
    // car's paint colour on its Paint submesh only). _GLASS: the kit's blended slot drawn opaque: clear texels clipped,
    // window panes darkened and smooth so they read through their reflection, cut-out foliage as it is. Cheap enough for Mali: three texture reads, one shadow. Alpha test is a keyword
    // (_ALPHATEST_ON, set where _Cutoff > 0) so opaque facades keep early depth; _CTLODFADE is the dithered LOD cross-fade.
    Properties
    {
        [MainTexture] _BaseMap("Base colour", 2D) = "white" {}
        [MainColor] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap("Normal map (OpenGL)", 2D) = "bump" {}
        _BumpScale("Normal strength", Range(0, 2)) = 1
        _MaskMap("Mask map (R metal, G AO, A smooth)", 2D) = "white" {}
        _Metallic("Metallic (scale with a mask, value without)", Range(0, 1)) = 0
        _Smoothness("Smoothness (scale with a mask, value without)", Range(0, 1)) = 0.25
        _OcclusionStrength("Occlusion strength", Range(0, 1)) = 1
        _Reflection("Sky reflection strength", Range(0, 2)) = 1
        _TintStrength("Instance tint strength (0 = ignore _Tint)", Range(0, 1)) = 1
        _GroundAO("Ground AO (height m, strength)", Vector) = (2.6, 0.4, 0, 0)
        _Cutoff("Alpha cutoff (0 = opaque)", Range(0, 1)) = 0
        [Toggle(_NORMALMAP)] _UseNormal("Use normal map", Float) = 0
        [Toggle(_MASKMAP)] _UseMask("Use mask map", Float) = 0
        [Toggle(_GLASS)] _Glass("Window glass", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "CTWorldCommon.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseMap_TexelSize;
            half4 _BaseColor;
            float4 _GroundAO;
            half _BumpScale, _Metallic, _Smoothness, _OcclusionStrength, _Reflection, _Cutoff, _TintStrength;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
            UNITY_DEFINE_INSTANCED_PROP(float, _LodFade)
        UNITY_INSTANCING_BUFFER_END(Props)

        float4 _CTWorldReflZenith;     // rgb sky straight up, a = 1 when IslandAtmosphere set them
        float4 _CTWorldReflHorizon;    // rgb sky at the horizon
        float4 _CTWorldReflGround;     // rgb what is below the horizon (street, neighbours)

        struct Attributes
        {
            float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2;
            half2 extra : TEXCOORD3;      // x = ground AO, y = fog
            #if defined(_NORMALMAP)
                half4 tangentWS : TEXCOORD4;
            #endif
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 ws = TransformObjectToWorld(input.positionOS.xyz);
            o.positionWS = ws;
            o.positionCS = TransformWorldToHClip(ws);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_NORMALMAP)
                o.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
            #endif
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            half ground = saturate(input.positionOS.y / max(_GroundAO.x, 0.01));
            o.extra = half2(lerp(1.0h - _GroundAO.y, 1.0h, ground), ComputeFogFactor(o.positionCS.z));
            return o;
        }

        // the golden-hour sky seen in a reflection: zenith -> horizon -> ground, a warm glow towards the sun; rough
        // surfaces see the blurred average (the ambient)
        half3 SkyReflect(half3 r, half rough, half3 n)
        {
            half3 amb = CTWorldAmbient(n);
            if (_CTWorldEnvParams.x > 0.0)
            {
                // the island's real sky (IslandAtmosphere, Poly Haven HDRI cubemap, its sun turned to the island sun):
                // roughness picks the blur; below the horizon the street and the neighbours stand in (the ground colour)
                half3 env = SAMPLE_TEXTURECUBE_LOD(_CTWorldEnvCube, sampler_CTWorldEnvCube, r, rough * (_CTWorldEnvParams.y - 1.0)).rgb * _CTWorldEnvParams.x;
                env = lerp(env, _CTWorldReflGround.rgb, saturate(-r.y * 3.0h) * step(0.0, _CTWorldReflGround.a));
                return env;
            }
            if (_CTWorldReflZenith.a <= 0.0) return amb;
            half up = r.y;
            half3 sky = up >= 0.0h ? lerp(_CTWorldReflHorizon.rgb, _CTWorldReflZenith.rgb, sqrt(up)) : lerp(_CTWorldReflHorizon.rgb, _CTWorldReflGround.rgb, saturate(-up * 4.0h));
            Light sun = GetMainLight();
            half toward = saturate(dot(r, sun.direction));
            sky += _CTWorldSunGlow.rgb * (pow(toward, 6.0h) * 0.6h * step(0.0h, up + 0.05h));
            return lerp(sky, amb, rough * rough);
        }

        half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            CTWorldLodFade(input.positionCS.xy, UNITY_ACCESS_INSTANCED_PROP(Props, _LodFade));
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            #if defined(_ALPHATEST_ON)
                half aTest = tex.a;
                #if defined(_GLASS)
                    // the test reads alpha no blurrier than mip 1.5: further down the chain the panes' alpha averages with
                    // the clear atlas texels around them and the windows were clipped into holes (see-through buildings)
                    float2 tdx = ddx(input.uv * _BaseMap_TexelSize.zw), tdy = ddy(input.uv * _BaseMap_TexelSize.zw);
                    float mip = 0.5 * log2(max(max(dot(tdx, tdx), dot(tdy, tdy)), 1e-6));
                    aTest = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv, min(mip, 1.5)).a;
                #endif
                clip(aTest - _Cutoff);
            #endif
            float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
            half3 albedo = tex.rgb * _BaseColor.rgb;
            albedo *= lerp(half3(1, 1, 1), (half3)tint.rgb, step(0.001, tint.a) * _TintStrength);
            half metal = _Metallic, occ = 1.0h, smooth = _Smoothness;
            #if defined(_MASKMAP)
                half4 m = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.uv);
                metal = m.r * _Metallic;
                occ = lerp(1.0h, m.g, _OcclusionStrength);
                smooth = m.a * _Smoothness;
            #endif
            half3 n = normalize(input.normalWS) * IS_FRONT_VFACE(facing, 1.0h, -1.0h);
            #if defined(_NORMALMAP)
                half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                half3 t = normalize(input.tangentWS.xyz);
                half3 b = cross(n, t) * input.tangentWS.w;
                n = normalize(tn.x * t + tn.y * b + tn.z * n);
            #endif
            #if defined(_GLASS)
                // the atlas's blended slot holds window panes (alpha ~0.2-0.7) and cut-out foliage (0 / 1): clear texels
                // are clipped (_Cutoff), panes become dark smooth glass, opaque texels (leaves) shade as they are
                half pane = 1.0h - smoothstep(0.75h, 0.95h, tex.a * _BaseColor.a);   // (a decor car's glass: colour alpha < 1)
                albedo *= lerp(1.0h, 0.42h, pane);
                smooth = lerp(min(smooth, 0.35h), max(smooth, 0.9h), pane);   // leaves stay matt (they read frosted otherwise)
                metal *= 1.0h - pane;
            #endif
            half3 v = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
            half oneMinusRefl = 0.96h * (1.0h - metal);
            half3 diffC = albedo * oneMinusRefl;
            half3 specC = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metal);
            half ao = input.extra.x * occ;

            // diffuse: the island's wrapped sun with soft shadow and its shaped trilight fill (CTWorldLight, toon 0)
            Light ml = GetMainLight();
            half ndl = dot(n, ml.direction);
            half wrapped = saturate((ndl + 0.35h) / 1.35h);
            half shadow = CTWorldShadow(input.positionWS) * ml.distanceAttenuation;
            bool tuned = _CTWorldAmbSky.a > 0.0;
            half keep = tuned ? (half)_CTWorldShadowParams.x : 0.35h;
            half3 direct = ml.color * (wrapped * lerp(keep, 1.0h, shadow));
            half lit = wrapped * shadow;
            half fill = tuned ? lerp((half)_CTWorldShadowParams.z, 1.0h, lit) : 1.0h;
            half3 c = diffC * (direct + CTWorldAmbient(n) * fill * ao) * lerp(0.85h, 1.0h, input.extra.x);

            // sun highlight (URP's mobile GGX approximation)
            half pr = 1.0h - smooth;
            half r = max(pr * pr, 0.0078125h);
            half r2 = r * r;
            half3 h = SafeNormalize(ml.direction + v);
            half nh = saturate(dot(n, h)), lh = saturate(dot(ml.direction, h));
            half d = nh * nh * (r2 - 1.0h) + 1.00001h;
            half specTerm = min(r2 / ((d * d) * max(0.1h, lh * lh) * (r * 4.0h + 2.0h)), 60.0h);
            c += specC * specTerm * ml.color * (saturate(ndl) * shadow);

            // sky reflection (URP's environment BRDF with the procedural sky)
            half nv = saturate(dot(n, v));
            half fres = (1.0h - nv); fres *= fres; fres *= fres;
            half grazing = saturate(smooth + (1.0h - oneMinusRefl));
            half3 envBRDF = lerp(specC, grazing.xxx, fres) / (r2 + 1.0h);
            half3 env = SkyReflect(reflect(-v, n), pr, n);
            c += env * envBRDF * (_Reflection * ao * lerp(0.55h, 1.0h, shadow));

            c = MixFog(c, input.extra.y);
            c = CTWorldHaze(c, input.positionWS);
            return half4(c, 1);
        }

        float3 _LightDirection;
        struct ShadowVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
        ShadowVaryings ShadowVert(Attributes input)
        {
            ShadowVaryings o;
            UNITY_SETUP_INSTANCE_ID(input);
            float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
            float3 nWS = TransformObjectToWorldNormal(input.normalOS);
            float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
            #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
            #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
            #endif
            o.positionCS = cs;
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            return o;
        }
        half4 ShadowFrag(ShadowVaryings input) : SV_Target
        {
            #if defined(_ALPHATEST_ON)
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
            #endif
            return 0;
        }
        float4 DepthVert(Attributes input) : SV_POSITION
        {
            UNITY_SETUP_INSTANCE_ID(input);
            return TransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
        }
        half4 NullFrag() : SV_Target { return 0; }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _MASKMAP
            #pragma shader_feature_local _GLASS
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_local _ _CTLODFADE
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]
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
