Shader "CT Life/Character Masked Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Authored albedo", 2D) = "white" {}
        _TintMask0("Skin / hair / top / bottom", 2D) = "black" {}
        _TintMask1("Shoes / accent / brows / hat", 2D) = "black" {}
        _SkinTint("Skin (linear RGB, A = enabled)", Vector) = (0,0,0,0)
        _HairTint("Hair (linear RGB, A = enabled)", Vector) = (0,0,0,0)
        _TopTint("Top (linear RGB, A = enabled)", Vector) = (0,0,0,0)
        _BottomTint("Bottom (linear RGB, A = enabled)", Vector) = (0,0,0,0)
        _ShoesTint("Shoes (linear RGB, A = enabled)", Vector) = (0,0,0,0)
        _BrowsTint("Brows (linear RGB, A = enabled)", Vector) = (0,0,0,0)
        _SkinReference("Skin reference (linear)", Vector) = (1,1,1,1)
        _HairReference("Hair reference (linear)", Vector) = (1,1,1,1)
        _TopReference("Top reference (linear)", Vector) = (1,1,1,1)
        _BottomReference("Bottom reference (linear)", Vector) = (1,1,1,1)
        _ShoesReference("Shoes reference (linear)", Vector) = (1,1,1,1)
        _BrowsReference("Brows reference (linear)", Vector) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 0.22
        [Normal] _BumpMap("Authored normal", 2D) = "bump" {}
        _BumpScale("Normal scale", Float) = 1
        _UseNormalMap("Use authored normal", Float) = 0
        _MetallicGlossMap("Authored metallic / smoothness", 2D) = "white" {}
        _Metallic("Metallic", Range(0,1)) = 0
        _UseMetallicMap("Use authored metallic map", Float) = 0
        _EmissionMap("Authored emission", 2D) = "black" {}
        _EmissionColor("Emission", Color) = (0,0,0,0)
        _ReferenceFloor("Minimum source reference luminance", Float) = 0.03
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Surface sides", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_TintMask0); SAMPLER(sampler_TintMask0);
        TEXTURE2D(_TintMask1); SAMPLER(sampler_TintMask1);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _SkinTint, _HairTint, _TopTint, _BottomTint, _ShoesTint, _BrowsTint;
            float4 _SkinReference, _HairReference, _TopReference, _BottomReference, _ShoesReference, _BrowsReference;
            float _Smoothness, _BumpScale, _UseNormalMap, _Metallic, _UseMetallicMap, _ReferenceFloor;
            half4 _EmissionColor;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1;
            float2 uv:TEXCOORD2; half fog:TEXCOORD3; half3 vertexLight:TEXCOORD4;
            half4 tangentWS:TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings PaletteVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input, output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = position.positionCS; output.positionWS = position.positionWS;
            VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.normalWS = normal.normalWS;
            output.tangentWS = half4(normal.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.fog = ComputeFogFactor(position.positionCS.z);
            output.vertexLight = VertexLighting(position.positionWS, output.normalWS);
            return output;
        }
        half3 MaskedTint(half3 source, float4 tint, float3 reference, half mask)
        {
            // Hermes v3 formula, in linear light. An unselected region remains exactly the source albedo.
            const float3 luminance = float3(.2126, .7152, .0722);
            half shade = min(dot(source, luminance) / max(_ReferenceFloor, dot(reference, luminance)), 1.6);
            return lerp(source, tint.rgb * shade, saturate(mask * tint.a));
        }
        half4 PaletteFragment(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
            #if defined(UNITY_COLORSPACE_GAMMA)
                albedo = SRGBToLinear(albedo);
            #endif
            half4 mask0 = SAMPLE_TEXTURE2D(_TintMask0, sampler_TintMask0, input.uv);
            half4 mask1 = SAMPLE_TEXTURE2D(_TintMask1, sampler_TintMask1, input.uv);
            albedo = MaskedTint(albedo, _SkinTint, _SkinReference.rgb, mask0.r);
            albedo = MaskedTint(albedo, _HairTint, _HairReference.rgb, mask0.g);
            albedo = MaskedTint(albedo, _TopTint, _TopReference.rgb, mask0.b);
            albedo = MaskedTint(albedo, _BottomTint, _BottomReference.rgb, mask0.a);
            albedo = MaskedTint(albedo, _ShoesTint, _ShoesReference.rgb, mask1.r);
            albedo = MaskedTint(albedo, _BrowsTint, _BrowsReference.rgb, mask1.b);
            #if defined(UNITY_COLORSPACE_GAMMA)
                albedo = LinearToSRGB(albedo);
            #endif
            InputData lighting = (InputData)0;
            lighting.positionWS = input.positionWS;
            half3 normalWS = input.normalWS;
            if (_UseNormalMap > .5)
            {
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                half3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS));
            }
            // Wardrobe shells have no hidden inner wall. Their deliberately enabled back face
            // needs an inward-facing lighting normal, while the opaque alpha/depth remain unchanged.
            lighting.normalWS = NormalizeNormalPerPixel(normalWS) * IS_FRONT_VFACE(facing, 1.0h, -1.0h);
            lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            lighting.fogCoord = input.fog; lighting.vertexLighting = input.vertexLight;
            lighting.bakedGI = SampleSH(lighting.normalWS);
            lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            lighting.shadowMask = half4(1,1,1,1);
            SurfaceData surface = (SurfaceData)0;
            surface.albedo = albedo; surface.specular = half3(.2,.2,.2); surface.smoothness = _Smoothness;
            surface.metallic = _Metallic;
            if (_UseMetallicMap > .5)
            {
                half4 metalSmooth = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                surface.metallic = metalSmooth.r;
                surface.smoothness *= metalSmooth.a;
            }
            surface.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
            surface.normalTS = half3(0,0,1); surface.occlusion = 1; surface.alpha = 1;
            half4 color = UniversalFragmentPBR(lighting, surface);
            color.rgb = MixFog(color.rgb, input.fog);
            return color;
        }
        half4 PaletteDepth(Varyings input):SV_Target { return 0; }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PaletteVertex
            #pragma fragment PaletteFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex PaletteVertex
            #pragma fragment PaletteDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
    }
    FallBack Off
}
