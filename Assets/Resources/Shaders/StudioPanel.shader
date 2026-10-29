// The backdrop reflections' light panels (Backdrops.RefreshReflections): plain bright colour, no
// fog, so the far-away panels still show in the reflection probe on misty backdrops.
Shader "Chess/StudioPanel"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            half4 _Color;
            float4 vert (float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            half4 frag () : SV_Target { return _Color; }
            ENDCG
        }
    }
}
