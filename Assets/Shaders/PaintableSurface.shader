// PSEUDO example for a simple unlit surface shader

Shader "Custom/PaintableSurface"
{
    Properties
    {
        _BaseMap ("Base Texture", 2D) = "white" {}
        _PaintTex ("Paint Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            sampler2D _PaintTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_BaseMap, i.uv);
                fixed4 paint   = tex2D(_PaintTex, i.uv);

                // Overlay: use paint alpha as blend
                fixed4 col;
                col.rgb = lerp(baseCol.rgb, paint.rgb, paint.a);
                col.a   = baseCol.a;
                return col;
            }
            ENDCG
        }
    }
}
