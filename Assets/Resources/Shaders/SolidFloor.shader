// Solid floor squares (Customize > Board > Solid floors): one flat colour, drawn from both
// sides, writing depth like any solid object so nothing see-through sorts in front of it.
Shader "3D Chess/Solid Floor"
{
    Properties
    {
        _Color ("Color", Color) = (0.2, 0.3, 0.4, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off
        ZWrite On
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
                return fixed4(_Color.rgb, 1);
            }
            ENDCG
        }
    }
}
