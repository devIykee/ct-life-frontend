Shader "CT Life/World/Toon"
{
    // CT Island v2 general surface (World lane): kerbs, sidewalks, bridges, tunnels, rails, props and, later, building
    // kits. Albedo = base map x colour x vertex colour (rgb tint; a = baked AO). _WORLDUV maps the base map in world
    // metres, picking the projection by the dominant normal axis (one sample), for pavers and concrete that never
    // stretch. _Fade dithers the surface away (venue roofs and camera-side walls when the player is inside).
    Properties
    {
        [MainTexture] _BaseMap("Base map", 2D) = "white" {}
        [MainColor] _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _WorldScale("World UV repeats per metre", Float) = 0.5
        _Toon("Toon step", Range(0, 1)) = 0.45
        _Spec("Specular (strength, power)", Vector) = (0, 32, 0, 0)
        [HDR] _Emission("Emission", Color) = (0, 0, 0, 0)
        _Fade("Dither fade", Range(0, 1)) = 0
        [Toggle(_WORLDUV)] _UseWorldUV("World UV", Float) = 0
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
            float4 _Spec;
            float _WorldScale;
            half _Toon, _Fade;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2;
            half4 color : TEXCOORD3; half fog : TEXCOORD4;
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
            o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            o.color = input.color;
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        void Dither(float4 positionCS)
        {
            if (_Fade <= 0.001) return;
            float2 px = floor(positionCS.xy);
            float d = frac(dot(px, float2(0.0671, 0.00583)) * 52.98);    // interleaved gradient noise
            clip(d - _Fade);
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            Dither(input.positionCS);
            half3 n = normalize(input.normalWS);
            float2 uv = input.uv;
            #if defined(_WORLDUV)
                half3 an = abs(n);
                uv = (an.y > an.x && an.y > an.z ? input.positionWS.xz : (an.x > an.z ? input.positionWS.zy : input.positionWS.xy)) * _WorldScale;
            #endif
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
            half3 albedo = tex.rgb * _BaseColor.rgb * input.color.rgb;
            half ao = input.color.a;
            half3 c = CTWorldLight(albedo, input.positionWS, n, ao, _Toon) * lerp(0.75h, 1.0h, ao);
            if (_Spec.x > 0.0)
            {
                Light sun = GetMainLight();
                half3 h = normalize(sun.direction + normalize(_WorldSpaceCameraPos - input.positionWS));
                c += sun.color * pow(saturate(dot(n, h)), _Spec.y) * _Spec.x;
            }
            c += _Emission.rgb * tex.rgb;
            c = MixFog(c, input.fog);
            c = CTWorldHaze(c, input.positionWS);
            return half4(c, 1);
        }

        float3 _LightDirection;
        struct ShadowVaryings { float4 positionCS : SV_POSITION; };
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
            return o;
        }
        half4 ShadowFrag(ShadowVaryings input) : SV_Target { Dither(input.positionCS); return 0; }
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
            #pragma shader_feature_local _WORLDUV
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
