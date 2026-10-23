Shader "CT Life/World/Interior Baked"
{
    // World v3 walk-in rooms (art/pipeline/world_v3_interior.py): the whole room's lighting is baked in Cycles into one
    // texture (albedo x direct + indirect light + emission, linear radiance stored as sRGB), so the room needs no runtime
    // lights: one texture sample, an exposure, alpha clip for the plants' leaves. No fog (the camera is a few metres away)
    // and no shadows received (the room is lit by its own bake).
    Properties
    {
        _BaseMap("Baked radiance (RGB), cut-out (A)", 2D) = "white" {}
        _Exposure("Exposure", Float) = 1.0
        _Tint("Tint", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha cut-off", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Exposure, _Cutoff;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }
            half4 Frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                clip(c.a - _Cutoff);
                return half4(c.rgb * _Tint.rgb * _Exposure, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                half _Exposure, _Cutoff;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V Vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = TRANSFORM_TEX(i.uv, _BaseMap); return o; }
            half4 Frag(V i) : SV_Target { clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a - _Cutoff); return 0; }
            ENDHLSL
        }
    }
}
