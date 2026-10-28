// Textured piece sets in a finish: the model's own colours, with its main colour (the mask's red)
// swapped for the side's colour at the original's shading (the mask's green, halved). Standard
// lighting so the finishes keep their shine; Glow adds emission, Crystal blends (_SrcBlend etc.).
Shader "Chess/RecolorPiece"
{
    Properties
    {
        _MainTex ("Original colours", 2D) = "white" {}
        _MaskTex ("Main colour mask (R) and shading (G)", 2D) = "black" {}
        _Tint ("Side colour", Color) = (1, 1, 1, 1)
        _Color ("Finish colour (multiplies; alpha for Crystal)", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
        _EmissionColor ("Glow (multiplies the colour)", Color) = (0, 0, 0, 1)
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 0
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows keepalpha
        #pragma target 3.0
        sampler2D _MainTex, _MaskTex;
        fixed4 _Tint, _Color, _EmissionColor;
        half _Glossiness, _Metallic;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed3 original = tex2D(_MainTex, IN.uv_MainTex).rgb;
            fixed2 mask = tex2D(_MaskTex, IN.uv_MainTex).rg;
            fixed3 col = lerp(original, saturate(_Tint.rgb * mask.g * 2), mask.r) * _Color.rgb;
            o.Albedo = col;
            o.Emission = col * _EmissionColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = _Color.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
