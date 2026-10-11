Shader "CT Life/World/Facade"
{
    // CT Island v2 venue building shells (World lane): windows, floors, trims and cladding drawn procedurally from the
    // wall's own metres, so one material fits any wall size. uv0 = (metres along the wall, metres up), uv1.x = wall
    // length (bays centre on it), vertex colour = the venue's wall colour. _Style: 0 plaster with punched windows and
    // cornice bands, 1 glass curtain wall, 2 corrugated industrial with a high window strip, 3 timber boards with
    // small windows, 4 arena ribs with banners, 5 striped arcade with neon. _Fade dithers the wall away (VenueShell:
    // roofs and camera-side walls while the player is inside).
    Properties
    {
        _Style("Style", Float) = 0
        _Trim("Trim colour", Color) = (0.95, 0.93, 0.88, 1)
        _Glass("Glass colour", Color) = (0.42, 0.62, 0.74, 1)
        _Frame("Window frame colour", Color) = (0.25, 0.26, 0.30, 1)
        _Accent("Accent colour (banners, neon, shop band)", Color) = (0.95, 0.35, 0.55, 1)
        _Floors("Floors (ground floor height, floor height, bay width, 0)", Vector) = (4.6, 3.6, 3.4, 0)
        _Toon("Toon step", Range(0, 1)) = 0.4
        _Fade("Dither fade", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "CTWorldCommon.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Trim, _Glass, _Frame, _Accent;
            float4 _Floors;
            half _Style, _Toon, _Fade;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float4 uv : TEXCOORD2;
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
            o.uv = float4(input.uv, input.uv1);
            o.color = input.color;
            o.fog = ComputeFogFactor(p.positionCS.z);
            return o;
        }

        void Dither(float4 positionCS)
        {
            if (_Fade <= 0.001) return;
            float2 px = floor(positionCS.xy);
            float d = frac(dot(px, float2(0.0671, 0.00583)) * 52.98);
            clip(d - _Fade);
        }

        float Box(float2 p, float2 lo, float2 hi) { return step(lo.x, p.x) * step(p.x, hi.x) * step(lo.y, p.y) * step(p.y, hi.y); }

        // albedo, and how glassy the texel is (glass gets a sky gradient and a little spec)
        half3 Facade(float2 uv, float len, half3 wall, out half glass)
        {
            glass = 0;
            float gf = _Floors.x, fh = _Floors.y, bay = _Floors.z;
            float nb = max(1.0, round(len / bay));
            float bw = len / nb;
            float bx = frac(uv.x / bw) * bw;                 // metres across the bay
            float above = uv.y - gf;
            float fl = floor(max(above, 0.0) / fh);
            float fy = above - fl * fh;                      // metres up within the floor
            half3 c = wall;
            int style = (int)round(_Style);
            if (style == 1)
            {
                // glass curtain wall: mullions every bay / 2, spandrel bands at each floor slab
                float mx = frac(uv.x / (bw * 0.5)) * bw * 0.5;
                float mull = step(mx, 0.12) + step(bw * 0.5 - 0.12, mx);
                float slab = step(fmod(max(uv.y, 0.0), fh), 0.55);
                glass = (1 - saturate(mull)) * (1 - slab);
                c = lerp(wall, _Frame.rgb, saturate(mull) * 0.85);
                c = lerp(c, wall * 0.9, slab);
            }
            else if (style == 2)
            {
                // corrugated cladding with a strip of high windows, a dark plinth
                float rib = 0.5 + 0.5 * cos(uv.x * 6.2831 / 0.6);
                c = wall * (0.86 + 0.14 * rib);
                float strip = Box(float2(bx, uv.y), float2(0.4, gf + 2.2), float2(bw - 0.4, gf + 3.4));
                glass = strip;
                c = lerp(c, _Frame.rgb * 0.8, step(uv.y, 0.9));
            }
            else if (style == 3)
            {
                // timber boards and small mullioned windows
                float board = frac(uv.y / 0.28);
                c = wall * (0.88 + 0.12 * step(0.1, board)) * (0.95 + 0.05 * sin(uv.x * 1.7 + floor(uv.y / 0.28) * 2.1));
                float win = Box(float2(bx, uv.y), float2(bw * 0.3, 1.4), float2(bw * 0.7, 2.7));
                float bar = step(abs(bx - bw * 0.5), 0.05) + step(abs(uv.y - 2.05), 0.05);
                glass = win * (1 - saturate(bar));
                float fr = Box(float2(bx, uv.y), float2(bw * 0.3 - 0.12, 1.28), float2(bw * 0.7 + 0.12, 2.82)) - win;
                c = lerp(c, _Trim.rgb, saturate(fr + win * saturate(bar)));
            }
            else if (style == 4)
            {
                // arena: tall ribs, a banner in every other bay
                float rib = Box(float2(bx, uv.y), float2(0.0, 0.0), float2(0.7, 1e4));
                float banner = Box(float2(bx, uv.y), float2(1.2, gf), float2(bw - 0.5, gf + 5.0)) * step(0.5, frac(floor(uv.x / bw) * 0.5));
                c = lerp(wall, _Trim.rgb, rib);
                c = lerp(c, _Accent.rgb, banner);
            }
            else if (style == 5)
            {
                // arcade: candy stripes, a neon band over the ground floor
                float st = step(0.5, frac(uv.x / 1.2));
                c = lerp(wall, _Trim.rgb, st * 0.55);
                float neon = step(abs(uv.y - (gf - 0.4)), 0.18);
                c = lerp(c, _Accent.rgb * 1.6, neon);
                float win = Box(float2(bx, uv.y), float2(0.6, gf + 0.8), float2(bw - 0.6, gf + 2.4));
                glass = win;
            }
            else
            {
                // plaster: punched windows with frames and sills, a band at every floor, a cornice at the top
                float win = Box(float2(bx, fy), float2(bw * 0.24, 0.9), float2(bw * 0.76, 2.6)) * step(0.0, above);
                float fr = Box(float2(bx, fy), float2(bw * 0.24 - 0.1, 0.8), float2(bw * 0.76 + 0.1, 2.7)) * step(0.0, above) - win;
                float sill = Box(float2(bx, fy), float2(bw * 0.22, 0.68), float2(bw * 0.78, 0.82)) * step(0.0, above);
                float band = step(abs(above - fl * fh), 0.14) * step(0.0, above);
                glass = win;
                c = lerp(c, _Trim.rgb, saturate(fr + sill + band));
                // ground floor: shop windows between piers, plinth
                float shop = Box(float2(bx, uv.y), float2(0.5, 0.6), float2(bw - 0.5, gf - 0.9)) * step(uv.y, gf);
                glass = max(glass, shop);
                c = lerp(c, _Accent.rgb, step(abs(uv.y - (gf - 0.45)), 0.3) * 0.85);
                c = lerp(c, wall * 0.75, step(uv.y, 0.45));
            }
            return c;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            Dither(input.positionCS);
            half3 n = normalize(input.normalWS);
            half glass;
            half3 albedo = Facade(input.uv.xy, max(input.uv.z, 1.0), input.color.rgb, glass);
            half3 c = CTWorldLight(albedo, input.positionWS, n, 1.0h, _Toon);
            if (glass > 0.0h)
            {
                half3 view = normalize(_WorldSpaceCameraPos - input.positionWS);
                half fres = pow(1.0h - saturate(dot(n, view)), 3.0h);
                half sky = saturate(frac(input.uv.y / max(_Floors.y, 0.1)) * 0.8h + 0.2h);
                half3 g = lerp(_Glass.rgb * 0.55h, _Glass.rgb * 1.15h, sky) + fres * 0.25h;
                Light sun = GetMainLight();
                half3 hv = normalize(sun.direction + view);
                g += sun.color * pow(saturate(dot(n, hv)), 60.0h) * 0.6h;
                c = lerp(c, g * (SampleSH(n) * 0.6h + sun.color * 0.35h), glass);
            }
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
