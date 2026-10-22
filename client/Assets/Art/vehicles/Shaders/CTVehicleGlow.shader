Shader "CT Life/Vehicles/Glow"
{
    // Additive soft glow (2026-10-22, hovercar readability): a radial falloff on a quad, no texture. The hovercar's
    // thruster discs and the pool of light it throws on the street under it. _Color.rgb is HDR (bloom picks it up),
    // _Intensity is driven per frame by FlightPhysics (lift and throttle).
    Properties
    {
        [HDR] _Color("Glow", Color) = (0.35, 0.8, 1.6, 1)
        _Intensity("Intensity", Float) = 1
        _Falloff("Falloff power", Float) = 2.2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity, _Falloff;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V Vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv * 2.0 - 1.0; return o; }
            half4 Frag(V i) : SV_Target
            {
                half r = saturate(1.0h - length(i.uv));
                half g = pow(r, _Falloff);
                return half4(_Color.rgb * (g * _Intensity), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
