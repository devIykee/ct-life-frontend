Shader "CT Life/World/Kit"
{
    // CT Island v2 kit models (World lane): buildings, props, vehicles and plants drawn with GPU instancing by
    // IslandInstanceRenderer. Albedo = base map (palette atlas, a kit colour map or a gradient texture) x colour.
    // Per instance: _Tint. With _TINTPALETTE, texels in the palette's bottom half (the facade greys the kit pipeline
    // writes there) take the instance's facade colour and keep their own light/dark, so one mesh gives many buildings.
    // _Wind sways plants (more towards the top of the model). A cheap ground AO darkens the bottom metres.
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _Toon("Toon step", Range(0, 1)) = 0.4
        _Wind("Wind (strength m, frequency, height m, 0)", Vector) = (0, 1.3, 6, 0)
        _GroundAO("Ground AO (height m, strength)", Vector) = (1.6, 0.35, 0, 0)
        _Cutoff("Alpha cutoff (0 = opaque)", Range(0, 1)) = 0
        [HDR] _Emission("Emission (x tint alpha)", Color) = (0, 0, 0, 0)
        [Toggle(_TINTPALETTE)] _TintPalette("Tint palette facades", Float) = 0
        [Toggle(_TINTALL)] _TintAll("Tint everything (foliage colour drift)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "CTWorldCommon.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _Emission;
            float4 _Wind, _GroundAO;
            half _Toon, _Cutoff;
        CBUFFER_END
        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
        UNITY_INSTANCING_BUFFER_END(Props)

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2;
            half3 extra : TEXCOORD3;      // x = ground AO, y = fog
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        float3 WindOffset(float3 positionOS, float3 positionWS)
        {
            if (_Wind.x <= 0.0) return 0;
            float h = saturate(positionOS.y / max(_Wind.z, 0.1));
            float ph = _Time.y * _Wind.y + positionWS.x * 0.23 + positionWS.z * 0.17;
            float gust = 0.65 + 0.35 * sin(_Time.y * 0.37 + positionWS.x * 0.011);
            return float3(sin(ph), 0, cos(ph * 0.83)) * (_Wind.x * h * h * gust);
        }

        Varyings Vert(Attributes input)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 ws = TransformObjectToWorld(input.positionOS.xyz);
            ws += WindOffset(input.positionOS.xyz, ws);
            o.positionWS = ws;
            o.positionCS = TransformWorldToHClip(ws);
            o.normalWS = TransformObjectToWorldNormal(input.normalOS);
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            half ground = saturate(input.positionOS.y / max(_GroundAO.x, 0.01));
            o.extra = half3(lerp(1.0h - _GroundAO.y, 1.0h, ground), ComputeFogFactor(o.positionCS.z), 0);
            return o;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            if (_Cutoff > 0.0) clip(tex.a - _Cutoff);
            float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
            half3 albedo = tex.rgb * _BaseColor.rgb;
            #if defined(_TINTALL)
                albedo = lerp(albedo, albedo * tint.rgb * 1.25h, step(0.001, tint.a));
            #endif
            #if defined(_TINTPALETTE)
                // facade greys (palette bottom half) take this instance's colour; a 0.62 grey gives the colour itself
                half facade = step(input.uv.y, 0.5) * step(0.001, tint.a);
                albedo = lerp(albedo, tint.rgb * (tex.r / 0.62h), facade);
            #endif
            half3 n = normalize(input.normalWS);
            half ao = input.extra.x;
            half3 c = CTWorldLight(albedo, input.positionWS, n, ao, _Toon) * lerp(0.8h, 1.0h, ao);
            c += _Emission.rgb * tex.rgb * tint.a;
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
            posWS += WindOffset(input.positionOS.xyz, posWS);
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
            if (_Cutoff > 0.0) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
            return 0;
        }
        float4 DepthVert(Attributes input) : SV_POSITION
        {
            UNITY_SETUP_INSTANCE_ID(input);
            float3 ws = TransformObjectToWorld(input.positionOS.xyz);
            ws += WindOffset(input.positionOS.xyz, ws);
            return TransformWorldToHClip(ws);
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
            #pragma shader_feature_local _TINTPALETTE
            #pragma shader_feature_local _TINTALL
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
