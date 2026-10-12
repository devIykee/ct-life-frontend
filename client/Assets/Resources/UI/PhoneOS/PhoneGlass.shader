Shader "Hidden/CTLife/PhoneGlass"
{
    Properties
    {
        _MainTex ("Wallpaper", 2D) = "white" {}
        _UvRect ("Wallpaper crop", Vector) = (0, 0, 1, 1)
        _BlurStep ("Blur step in wallpaper UV", Vector) = (0, 0, 0, 0)
        _GlassTint ("Surface wash", Color) = (1, 1, 1, 0.28)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _UvRect;
            float4 _BlurStep;
            fixed4 _GlassTint;

            half3 SampleRow(float2 uv)
            {
                return tex2D(_MainTex, saturate(uv - float2(2 * _BlurStep.x, 0))).rgb
                    + 4 * tex2D(_MainTex, saturate(uv - float2(_BlurStep.x, 0))).rgb
                    + 6 * tex2D(_MainTex, saturate(uv)).rgb
                    + 4 * tex2D(_MainTex, saturate(uv + float2(_BlurStep.x, 0))).rgb
                    + tex2D(_MainTex, saturate(uv + float2(2 * _BlurStep.x, 0))).rgb;
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = _UvRect.xy + input.uv * _UvRect.zw;
                half3 colour = SampleRow(uv - float2(0, 2 * _BlurStep.y))
                    + 4 * SampleRow(uv - float2(0, _BlurStep.y))
                    + 6 * SampleRow(uv)
                    + 4 * SampleRow(uv + float2(0, _BlurStep.y))
                    + SampleRow(uv + float2(0, 2 * _BlurStep.y));
                colour /= 256;
                return fixed4(lerp(colour, _GlassTint.rgb, _GlassTint.a), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
