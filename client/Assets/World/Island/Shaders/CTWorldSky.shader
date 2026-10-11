Shader "CT Life/World/Sky"
{
    // CT Island v2 painted sky (World lane): a zenith-to-horizon gradient whose horizon equals the fog colour (so the
    // sea and far hills melt into it), a warm glow and disc around the sun, and soft painted clouds from one tiling
    // texture projected onto a dome. One texture sample per pixel; no procedural noise.
    Properties
    {
        _Zenith("Zenith", Color) = (0.30, 0.52, 0.80, 1)
        _Mid("Mid sky", Color) = (0.56, 0.72, 0.88, 1)
        _Horizon("Horizon (= fog)", Color) = (0.90, 0.82, 0.72, 1)
        _Below("Below the horizon", Color) = (0.80, 0.80, 0.78, 1)
        _SunGlowColor("Sun glow", Color) = (1.0, 0.72, 0.42, 1)
        _Sun("Sun (disc size, disc sharpness, glow power, glow strength)", Vector) = (0.9993, 900, 10, 0.75)
        _Clouds("Cloud noise (r)", 2D) = "black" {}
        _CloudColor("Cloud lit", Color) = (1.0, 0.95, 0.88, 1)
        _CloudShade("Cloud shade", Color) = (0.74, 0.70, 0.78, 1)
        _CloudParams("Clouds (scale, cover, softness, speed)", Vector) = (0.42, 0.55, 0.22, 0.004)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_Clouds); SAMPLER(sampler_Clouds);
            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Mid, _Horizon, _Below, _SunGlowColor, _CloudColor, _CloudShade;
                float4 _Sun, _CloudParams;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.dir = i.positionOS.xyz; return o; }
            half4 Frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                half3 sky = h > 0.0 ? lerp(lerp(_Horizon.rgb, _Mid.rgb, smoothstep(0.0, 0.22, h)), _Zenith.rgb, smoothstep(0.18, 0.85, h))
                                    : lerp(_Horizon.rgb, _Below.rgb, saturate(-h * 6.0));
                float3 sunDir = _MainLightPosition.xyz;
                float s = saturate(dot(d, sunDir));
                half glow = pow(s, _Sun.z) * _Sun.w;
                half disc = saturate((s - _Sun.x) * _Sun.y);
                sky = lerp(sky, _SunGlowColor.rgb, saturate(glow * (1.0 - saturate(h * 1.5))));
                // painted clouds on a flattened dome
                float2 uv = d.xz / (h + 0.18) * _CloudParams.x + _Time.y * _CloudParams.w;
                half n = SAMPLE_TEXTURE2D(_Clouds, sampler_Clouds, uv).r;
                half m = smoothstep(_CloudParams.y, _CloudParams.y + _CloudParams.z, n) * smoothstep(0.02, 0.18, h);
                half lit = saturate(0.55 + 0.45 * dot(normalize(float3(d.x, 0.3, d.z)), normalize(float3(sunDir.x, 0.3, sunDir.z))));
                half3 cloud = lerp(_CloudShade.rgb, _CloudColor.rgb, lit);
                cloud = lerp(cloud, _SunGlowColor.rgb, saturate(glow * 0.8));
                half3 c = lerp(sky, cloud, m * 0.92);
                c += _MainLightColor.rgb * disc * (1.0 - m);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
