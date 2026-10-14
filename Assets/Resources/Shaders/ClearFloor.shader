// See-through floor squares (the default board look): one flat colour with its alpha, drawn
// from both sides, so squares above the camera show from underneath too.
Shader "3D Chess/See-through Floor"
{
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.9, 1, 0.2)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            float4 vert (float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag () : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
