Shader "CT Life/World/Water"
{
    // CT Island v2 sea, river and lakes (World lane). Mobile-safe: no depth texture or grab pass. Water depth comes
    // from a baked map sampled by world x/z (r = depth / 12 m), which drives the shallow-to-deep tint, the transparency
    // over sand and the shore foam. Gentle waves in the vertex shader fade out at the shore. Two scrolling ripple
    // normals, Fresnel towards the sky, and a sun glint. Rivers pass their flow (metres per second along x/z) in uv1.
    Properties
    {
        _DepthMap("Depth map (r = depth / 12 m)", 2D) = "white" {}
        _Ripples("Ripple normal map", 2D) = "bump" {}
        _Domain("Domain (min x, min z, size, 0)", Vector) = (-1024, -1024, 2048, 0)
        _Shallow("Shallow", Color) = (0.36, 0.80, 0.82, 1)
        _Deep("Deep", Color) = (0.07, 0.36, 0.60, 1)
        _Sky("Sky reflection", Color) = (0.70, 0.84, 0.95, 1)
        _Foam("Foam", Color) = (0.98, 0.98, 0.95, 1)
        _Waves("Waves (amplitude m, wavelength m, speed, steepness)", Vector) = (0.14, 18, 1.2, 0.5)
        _RippleScale("Ripple repeats per metre (x, y layers)", Vector) = (0.06, 0.11, 0, 0)
        _Glint("Sun glint (power, strength)", Vector) = (180, 2.2, 0, 0)
        _FoamDepth("Foam depth (m)", Float) = 0.75
        _Alpha("Alpha (shallow, deep)", Vector) = (0.42, 1.0, 0, 0)
        _Reef("Shallow reef", Color) = (0.30, 0.86, 0.78, 1)
        _ReefBand("Reef band (from depth m, to depth m, strength, patch scale)", Vector) = (0.5, 3.6, 0.55, 0.02)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "CTWorldCommon.hlsl"
            TEXTURE2D(_DepthMap); SAMPLER(sampler_DepthMap);
            TEXTURE2D(_Ripples); SAMPLER(sampler_Ripples);
            CBUFFER_START(UnityPerMaterial)
                float4 _Domain, _Waves, _RippleScale, _Glint, _Alpha;
                half4 _Shallow, _Deep, _Sky, _Foam, _Reef;
                float4 _ReefBand;
                float _FoamDepth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv1 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1;
                float2 flow : TEXCOORD2; half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Depth(float2 xz) { return SAMPLE_TEXTURE2D_LOD(_DepthMap, sampler_DepthMap, (xz - _Domain.xy) / _Domain.z, 0).r * 12.0; }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(input.positionOS.xyz);
                float depth = Depth(ws.xz);
                float amp = _Waves.x * saturate(depth / 2.5) * (dot(input.uv1, input.uv1) > 0.0001 ? 0.2 : 1.0);
                float k = 6.2831853 / max(_Waves.y, 1.0);
                float t = _Time.y * _Waves.z;
                float2 d1 = normalize(float2(0.8, 0.6)), d2 = normalize(float2(-0.35, 0.94));
                float p1 = dot(d1, ws.xz) * k + t, p2 = dot(d2, ws.xz) * k * 1.7 - t * 1.3;
                ws.y += amp * (sin(p1) + 0.45 * sin(p2));
                float dx = amp * (k * d1.x * cos(p1) + 0.45 * 1.7 * k * d2.x * cos(p2));
                float dz = amp * (k * d1.y * cos(p1) + 0.45 * 1.7 * k * d2.y * cos(p2));
                o.normalWS = normalize(float3(-dx, 1.0, -dz));
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.flow = input.uv1;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 ws = input.positionWS;
                float depth = Depth(ws.xz);
                float2 flow = input.flow * _Time.y;
                float2 uvA = ws.xz * _RippleScale.x + float2(0.013, 0.021) * _Time.y - flow * _RippleScale.x;
                float2 uvB = ws.xz * _RippleScale.y + float2(-0.017, 0.009) * _Time.y - flow * _RippleScale.y * 1.3;
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_Ripples, sampler_Ripples, uvA), 0.6);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_Ripples, sampler_Ripples, uvB), 0.45);
                half3 rip = normalize(half3(nA.xy + nB.xy, nA.z * nB.z));
                half3 n = normalize(half3(input.normalWS.x + rip.x * 0.55, input.normalWS.y, input.normalWS.z + rip.y * 0.55));
                half3 view = normalize(_WorldSpaceCameraPos - ws);
                Light sun = GetMainLight(TransformWorldToShadowCoord(ws));
                half shadow = lerp(0.55h, 1.0h, sun.shadowAttenuation);
                half dep = smoothstep(0.0, 9.0, depth);
                half3 col = lerp(_Shallow.rgb, _Deep.rgb, dep);
                // a turquoise reef band in the shallows, broken into patches
                half patch = SAMPLE_TEXTURE2D(_Ripples, sampler_Ripples, ws.xz * _ReefBand.w + 0.31).r;
                half reef = smoothstep(_ReefBand.x, _ReefBand.x + 0.6h, depth) * smoothstep(_ReefBand.y, _ReefBand.y - 1.4h, depth);
                col = lerp(col, _Reef.rgb, reef * _ReefBand.z * (0.55h + 0.9h * patch));
                half3 ambient = SampleSH(half3(0, 1, 0));
                col = col * (ambient * 0.85h + sun.color * 0.45h * shadow);
                half fres = pow(1.0h - saturate(dot(n, view)), 4.0h);
                col = lerp(col, _Sky.rgb * (ambient + sun.color * 0.3h), saturate(fres * 0.7h));
                half3 h = normalize(sun.direction + view);
                half glint = pow(saturate(dot(n, h)), _Glint.x) * _Glint.y * shadow;
                col += sun.color * glint;
                // shore foam: a soft band plus animated lines running up the beach
                half noise = SAMPLE_TEXTURE2D(_Ripples, sampler_Ripples, ws.xz * 0.05 + _Time.y * 0.01).r;
                half band = smoothstep(_FoamDepth, 0.05h, depth + (noise - 0.5h) * 0.35h);
                half lines = smoothstep(0.55h, 0.95h, sin(depth * 9.0 - _Time.y * 1.6 + noise * 3.0) * 0.5h + 0.5h) * smoothstep(_FoamDepth * 1.8h, 0.2h, depth);
                half foam = saturate(band * 0.85h + lines * 0.45h);
                col = lerp(col, _Foam.rgb * (ambient + sun.color * 0.6h), foam);
                half alpha = lerp(_Alpha.x, _Alpha.y, smoothstep(0.0, 5.0, depth));     // opaque deep water: no seabed tiles show through
                alpha = saturate(max(alpha, foam * 0.9h) + fres * 0.2h);
                col = MixFog(col, input.fog);
                col = CTWorldHaze(col, ws);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
