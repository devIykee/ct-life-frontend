Shader "CT Life/World/Road"
{
    // CT Island v2 roads (World lane). Lane markings are drawn procedurally from the vertex data, so every segment and
    // junction of a streamed cell merges into one mesh and one draw:
    //   uv0 = (metres right of the centreline, metres along), uv1 = (lane width, median), uv2 = (lanes forward, back),
    //   color = (one-way, centre-line kind 0 none / 0.5 dashed / 1 double yellow, parking width / 4, 1 lines | 0 none).
    // The surface is a tiling grayscale detail (asphalt or packed dirt) in world metres, tinted.
    Properties
    {
        _Detail("Surface detail (grayscale)", 2D) = "gray" {}
        _Color("Surface colour", Color) = (0.36, 0.37, 0.40, 1)
        _DetailScale("Detail repeats per metre", Float) = 0.2
        _Contrast("Detail contrast", Range(0, 2)) = 0.8
        _White("Paint white", Color) = (0.93, 0.93, 0.9, 1)
        _Yellow("Paint yellow", Color) = (0.96, 0.78, 0.22, 1)
        _LineWidth("Line width (m)", Float) = 0.14
        _Wear("Paint wear", Range(0, 1)) = 0.25
        _Tracks("Tyre track darkening", Range(0, 0.5)) = 0.12
        _Toon("Toon step", Range(0, 1)) = 0.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-5" }
        HLSLINCLUDE
        #include "CTWorldCommon.hlsl"
        TEXTURE2D(_Detail); SAMPLER(sampler_Detail);
        CBUFFER_START(UnityPerMaterial)
            half4 _Color, _White, _Yellow;
            float _DetailScale, _LineWidth;
            half _Contrast, _Wear, _Tracks, _Toon;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR;
            float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1;
            float2 uv0 : TEXCOORD2; float4 lanes : TEXCOORD3; half4 color : TEXCOORD4; half fog : TEXCOORD5;
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
            o.uv0 = input.uv0; o.lanes = float4(input.uv1, input.uv2); o.color = input.color;
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        // coverage of a line centred at x0 (half width hw) for lateral coordinate u
        float Line(float u, float x0, float hw, float aa) { return 1.0 - smoothstep(hw - aa, hw + aa, abs(u - x0)); }
        float Dash(float v, float period, float duty) { return step(frac(v / period), duty); }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float u = input.uv0.x, v = input.uv0.y;
            float lw = max(input.lanes.x, 0.1), med = input.lanes.y, F = input.lanes.z, B = input.lanes.w;
            bool oneWay = input.color.r > 0.5;
            half kind = input.color.g;
            float park = input.color.b * 4.0;
            bool lines = input.color.a > 0.5;
            half det = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, input.positionWS.xz * _DetailScale).r;
            half det2 = SAMPLE_TEXTURE2D(_Detail, sampler_Detail, input.positionWS.xz * (_DetailScale * 0.17) + 0.31).r;
            half3 albedo = _Color.rgb * (1.0h + (lerp(det, det2, 0.35h) - 0.5h) * _Contrast);
            float aa = max(fwidth(u) * 1.25, 0.004);
            float hw = _LineWidth * 0.5;
            float white = 0.0, yellow = 0.0, track = 0.0;
            if (lines)
            {
                if (oneWay)
                {
                    float h = F * lw * 0.5;
                    white += Line(u, -h, hw, aa) + Line(u, h, hw, aa);
                    [unroll] for (int k = 1; k < 4; k++) if (k < F) white += Line(u, -h + k * lw, hw, aa) * Dash(v, 9.0, 0.4);
                    [unroll] for (int t = 0; t < 4; t++) if (t < F) track += Line(u, -h + (t + 0.5) * lw - 0.85, 0.35, 0.3) + Line(u, -h + (t + 0.5) * lw + 0.85, 0.35, 0.3);
                }
                else
                {
                    float r0 = med * 0.5;
                    if (kind > 0.75)
                    {
                        float off = med >= 0.3 ? r0 - 0.12 : 0.11;
                        yellow += Line(u, -off, hw, aa) + Line(u, off, hw, aa);
                    }
                    else if (kind > 0.25) white += Line(u, 0.0, hw, aa) * Dash(v, 9.0, 0.38);
                    [unroll] for (int k = 1; k < 4; k++)
                    {
                        if (k < F) white += Line(u, r0 + k * lw, hw, aa) * Dash(v, 9.0, 0.4);
                        if (k < B) white += Line(u, -(r0 + k * lw), hw, aa) * Dash(v, 9.0, 0.4);
                    }
                    float er = r0 + F * lw, el = -(r0 + B * lw);
                    white += (Line(u, er, hw, aa) + Line(u, el, hw, aa)) * (park > 0.1 ? 0.0 : 1.0);
                    // parking bays: short ticks every 6 m in the parking strip
                    if (park > 0.1)
                    {
                        float inPark = step(er, u) * step(u, er + park) + step(el - park, u) * step(u, el);
                        float dv = abs(frac(v / 6.0 + 0.5) - 0.5) * 6.0;   // metres to the nearest bay boundary
                        float tick = 1.0 - smoothstep(0.07, 0.07 + aa * 3.0, dv);
                        white += inPark * tick * 0.9;
                        white += (Line(u, er, hw * 0.8, aa) + Line(u, el, hw * 0.8, aa)) * Dash(v, 6.0, 0.25);
                    }
                    [unroll] for (int t = 0; t < 4; t++)
                    {
                        if (t < F) track += Line(u, r0 + (t + 0.5) * lw - 0.85, 0.35, 0.3) + Line(u, r0 + (t + 0.5) * lw + 0.85, 0.35, 0.3);
                        if (t < B) track += Line(u, -(r0 + (t + 0.5) * lw) - 0.85, 0.35, 0.3) + Line(u, -(r0 + (t + 0.5) * lw) + 0.85, 0.35, 0.3);
                    }
                }
            }
            half wear = 1.0h - _Wear * saturate((det2 - 0.35h) * 2.0h);
            albedo *= 1.0h - saturate(track) * _Tracks;
            albedo = lerp(albedo, _White.rgb, saturate(white) * wear);
            albedo = lerp(albedo, _Yellow.rgb, saturate(yellow) * wear);
            half3 n = normalize(input.normalWS);
            half3 c = CTWorldLight(albedo, input.positionWS, n, 1.0h, _Toon);
            c = MixFog(c, input.fog);
            c = CTWorldHaze(c, input.positionWS);
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
