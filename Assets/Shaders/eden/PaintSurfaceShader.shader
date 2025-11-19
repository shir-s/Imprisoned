Shader "Custom/PaintSurface"
{
    Properties
    {
        _BaseMap   ("Base Texture", 2D)   = "white" {}
        _BaseColor ("Base Color",  Color) = (1,1,1,1)
        _PaintTex  ("Paint Texture", 2D)  = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        LOD 100

        Pass
        {
            CGPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv     : TEXCOORD0;
            };

            sampler2D _BaseMap;
            float4    _BaseMap_ST;
            float4    _BaseColor;

            sampler2D _PaintTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_BaseMap, i.uv) * _BaseColor;

                fixed4 paint = tex2D(_PaintTex, i.uv);

                fixed a = saturate(paint.a);

                fixed3 color = lerp(baseCol.rgb, paint.rgb, a);

                return fixed4(color, 1.0);
            }

            ENDCG
        }
    }
}
