// A soft edge light in the side's colour, drawn over a piece's own material (a second material
// slot, see PieceSets.CreateRimMaterial). It doesn't depend on the scene's lights, so pieces keep
// a readable outline on dark backdrops, and in see-through or mirror finishes.
Shader "Chess/PieceRim"
{
    Properties
    {
        _RimColor ("Rim colour", Color) = (0, 1, 1, 1)
        _RimPower ("Rim tightness", Float) = 2.5
        _RimStrength ("Rim strength", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _RimColor;
            half _RimPower, _RimStrength;
            struct v2f { float4 pos : SV_POSITION; float3 normal : TEXCOORD0; float3 view : TEXCOORD1; };
            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                half facing = saturate(dot(normalize(i.normal), normalize(i.view)));
                half rim = pow(1 - facing, _RimPower) * _RimStrength;
                return fixed4(_RimColor.rgb * rim, 0);
            }
            ENDCG
        }
    }
}
