// Abyss Glow pieces: the model's detail map (grayscale) picks a color between the side's
// dark and light tints, lit by the sun and the sky, with a fresnel rim glowing in the side's
// color. A plain vertex/fragment pass (one light, fog) so it compiles quickly everywhere.
Shader "Chess/AbyssPiece"
{
    Properties
    {
        _DetailTex ("Detail (grayscale)", 2D) = "white" {}
        _DarkTint ("Dark tint", Color) = (0.04, 0.02, 0.08, 1)
        _LightTint ("Light tint", Color) = (0.35, 0.18, 0.5, 1)
        _RimColor ("Rim glow", Color) = (1, 0.2, 0.8, 1)
        _RimPower ("Rim tightness", Float) = 2.6
        _RimStrength ("Rim strength", Float) = 1.3
        _Gloss ("Shine", Range(0, 1)) = 0.5
        _Color ("Color (unused)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _DetailTex;
            float4 _DetailTex_ST;
            fixed4 _DarkTint, _LightTint, _RimColor;
            half _RimPower, _RimStrength, _Gloss;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 world : TEXCOORD2;
                UNITY_FOG_COORDS(3)
            };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _DetailTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half detail = tex2D(_DetailTex, i.uv).r;
                half3 albedo = lerp(_DarkTint.rgb, _LightTint.rgb, detail);
                half3 n = normalize(i.normal);
                half3 l = normalize(_WorldSpaceLightPos0.xyz);
                half3 v = normalize(_WorldSpaceCameraPos - i.world);
                half diffuse = saturate(dot(n, l));
                half spec = pow(saturate(dot(n, normalize(l + v))), 40) * _Gloss;
                half rim = pow(1 - saturate(dot(n, v)), _RimPower) * _RimStrength;
                half3 col = albedo * (ShadeSH9(half4(n, 1)) + _LightColor0.rgb * diffuse) + _LightColor0.rgb * spec * 0.6 + _RimColor.rgb * rim;
                fixed4 c = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
