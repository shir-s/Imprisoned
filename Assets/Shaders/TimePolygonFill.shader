// Writes to time texture for FILLED AREAS
// R = Paint Time, G = 1 (fill marker)
Shader "Custom/TimePolygonFill"
{
    Properties
    {
        _PaintTime ("Paint Time (seconds)", Float) = 0.0
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _PaintTime;

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 vertex : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // R = time, G = 1 (this is a FILLED area, not trail)
                return float4(_PaintTime, 1.0, 0, 1);
            }
            ENDCG
        }
    }
}
