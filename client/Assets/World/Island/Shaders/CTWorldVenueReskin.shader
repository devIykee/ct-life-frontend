Shader "CT Life/World/Venue Reskin"
{
    // World v3 (2026-10-22): the venue landmarks' shells on the island (Timeline Square's towers and halls, and the other
    // downtown venues) keep their geometry, doors and palette colours, but stop reading as toys: the flat palette colour
    // becomes real plaster on walls and clay tiles on roofs (CC0 Poly Haven scans, world-projected, normalised so they
    // add grain, relief and roughness while the palette keeps the hue), lit by the island sun, trilight and HDRI like the
    // v3 buildings (CTWorldSurface) instead of the dioramas' own baked light. Roofs: upward faces with a warm, saturated
    // palette colour; walls: everything else. Assigned at run time by IslandVenueReskin to big shell renderers only.
    Properties
    {
        _BaseMap("Palette", 2D) = "white" {}
        _BaseColor("Colour", Color) = (1, 1, 1, 1)
        _Wall("Wall (rgb colour x AO, a smoothness)", 2D) = "grey" {}
        [Normal] _WallNormal("Wall normal", 2D) = "bump" {}
        _WallMid("Wall scan's mean colour (linear)", Color) = (0.383, 0.345, 0.331, 1)
        _Roof("Roof (rgb colour x AO, a smoothness)", 2D) = "grey" {}
        [Normal] _RoofNormal("Roof normal", 2D) = "bump" {}
        _RoofMid("Roof scan's mean colour (linear)", Color) = (0.078, 0.026, 0.009, 1)
        _Tiles("Tile metres (wall, roof), detail strength (wall, roof)", Vector) = (2.0, 2.0, 0.85, 0.9)
        _UseVertexColor("Palette x vertex colour (venue shell roofs)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "CTWorldCommon.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Wall); SAMPLER(sampler_Wall);
        TEXTURE2D(_WallNormal); SAMPLER(sampler_WallNormal);
        TEXTURE2D(_Roof); SAMPLER(sampler_Roof);
        TEXTURE2D(_RoofNormal); SAMPLER(sampler_RoofNormal);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _WallMid, _RoofMid;
            float4 _Tiles;
            half _UseVertexColor;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fog : TEXCOORD3;
            half3 color : TEXCOORD4;
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
            o.fog = ComputeFogFactor(p.positionCS.z);
            o.color = lerp(half3(1, 1, 1), input.color.rgb, _UseVertexColor);
            return o;
        }

        half4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 pal = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb * input.color;
            half3 ng = normalize(input.normalWS);
            float3 p = input.positionWS;
            half mx = max(pal.r, max(pal.g, pal.b)), mn = min(pal.r, min(pal.g, pal.b));
            half sat = (mx - mn) / max(mx, 1e-3h);
            bool roof = ng.y > 0.25h && sat > 0.35h && pal.r > pal.b;
            bool top = abs(ng.y) > 0.6h;
            bool sideX = abs(ng.x) > abs(ng.z);
            float2 w = top ? p.xz : (sideX ? float2(p.z, p.y) : float2(p.x, p.y));
            half4 a; half3 tn; half3 detail;
            if (roof)
            {
                // roof tiles run down the slope: project on the plane across it
                float2 ruv = (abs(ng.x) > abs(ng.z) ? float2(p.z, p.y * 1.6 + p.x * 0.3) : float2(p.x, p.y * 1.6 + p.z * 0.3)) / _Tiles.y;
                a = SAMPLE_TEXTURE2D(_Roof, sampler_Roof, ruv);
                tn = UnpackNormal(SAMPLE_TEXTURE2D(_RoofNormal, sampler_RoofNormal, ruv));
                detail = lerp(half3(1, 1, 1), a.rgb / max(_RoofMid.rgb, 1e-3h), _Tiles.w);
            }
            else
            {
                float2 wuv = w / _Tiles.x;
                a = SAMPLE_TEXTURE2D(_Wall, sampler_Wall, wuv);
                tn = UnpackNormal(SAMPLE_TEXTURE2D(_WallNormal, sampler_WallNormal, wuv));
                half lum = dot(a.rgb / max(_WallMid.rgb, 1e-3h), half3(0.3h, 0.4h, 0.3h));
                detail = lerp(1.0h, lum, _Tiles.z).xxx;
            }
            // the palette's toy saturation eased towards its own grey (plaster and fired clay are never candy-coloured)
            half grey = dot(pal, half3(0.3h, 0.59h, 0.11h));
            pal = lerp(pal, grey.xxx, roof ? 0.2h : 0.32h);
            half3 albedo = pal * clamp(detail, 0.0h, 2.0h);
            half3 n = top ? normalize(ng * tn.z + half3(tn.x, 0.0h, tn.y))
                          : normalize(ng * tn.z + (sideX ? half3(0.0h, tn.y, tn.x * sign(ng.x)) : half3(tn.x * -sign(ng.z), tn.y, 0.0h)) * 0.7h);
            half3 c = CTWorldSurface(albedo, saturate(a.a), p, n, 1.0h, 0.8h);
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
