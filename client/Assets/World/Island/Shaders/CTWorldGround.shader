Shader "CT Life/World/Ground PBR"
{
    // CT Island street stone (World lane, 2026-10-22): sidewalks, kerbs, plazas and concrete skirts from CC0 Poly Haven
    // scans (art/pipeline/world_street_textures.py: colour x AO + smoothness, OpenGL normal), world-projected so every
    // strip of every cell lines up: tops in x/z, faces (kerb fronts, skirts) on the dominant axis. Each slab of a paved
    // texture gets its own small tone shift (_Slabs: slabs a tile, strength) so a pavement never reads as one repeated
    // photo. Lit by the island's PBR street lighting (CTWorldSurface: wrapped sun, trilight fill, soft shadow, sun
    // highlight, HDRI reflection). Two texture reads a pixel; no alpha test.
    Properties
    {
        _Albedo("Colour x AO (rgb), smoothness (a)", 2D) = "grey" {}
        [Normal] _Normal("Normal (OpenGL)", 2D) = "bump" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _TileMetres("Texture tile (m)", Float) = 1.8
        _NormalStrength("Normal strength", Range(0, 2)) = 1
        _Smoothness("Smoothness scale", Range(0, 2)) = 1
        _Slabs("Slab variation (slabs a tile, strength)", Vector) = (4, 0.08, 0, 0)
        _Reflection("Reflection", Range(0, 2)) = 1
        _EdgeAO("Ground contact darkening (height m, strength)", Vector) = (0, 0, 0, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-5" }
        HLSLINCLUDE
        #include "CTWorldCommon.hlsl"
        TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
        TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float4 _Slabs, _EdgeAO;
            float _TileMetres;
            half _NormalStrength, _Smoothness, _Reflection;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half fog : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
            o.positionCS = p.positionCS; o.positionWS = p.positionWS;
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        float Hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 ng = normalize(input.normalWS);
            float3 p = input.positionWS;
            // tops in x/z; side faces in (x or z, y): the axis the face looks along picks the plane
            bool top = abs(ng.y) > 0.6h;
            bool sideX = abs(ng.x) > abs(ng.z);
            float2 uv = (top ? p.xz : (sideX ? float2(p.z, p.y) : float2(p.x, p.y))) / _TileMetres;
            half4 a = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv);
            half3 tn = UnpackNormalScale(SAMPLE_TEXTURE2D(_Normal, sampler_Normal, uv), _NormalStrength);
            half slab = (half)Hash12(floor(uv * _Slabs.x) + 17.0) - 0.5h;
            half3 albedo = a.rgb * _Color.rgb * (1.0h + slab * _Slabs.y * 2.0h);
            half3 n = ng;
            if (top) n = normalize(ng * tn.z + half3(tn.x, 0.0h, tn.y));
            else n = normalize(ng * tn.z + (sideX ? half3(0.0h, tn.y, tn.x * sign(ng.x)) : half3(tn.x * -sign(ng.z), tn.y, 0.0h)) * 0.6h);
            half ao = 1.0h;
            if (_EdgeAO.x > 0.0) ao = lerp(1.0h - (half)_EdgeAO.y, 1.0h, saturate(frac(p.y) / _EdgeAO.x));
            half3 c = CTWorldSurface(albedo, saturate(a.a * _Smoothness), p, n, ao, _Reflection);
            c = MixFog(c, input.fog);
            c = CTWorldHaze(c, p);
            return half4(c, 1);
        }

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
        float4 DepthVert(Attributes input) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(input); return TransformObjectToHClip(input.positionOS.xyz); }
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
            #pragma fragment NullFrag
            #pragma multi_compile_instancing
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
