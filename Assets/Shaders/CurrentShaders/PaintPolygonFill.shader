// FILEPATH: Assets/Shaders/PaintPolygonFill.shader
Shader "Custom/PaintPolygonFill"
{
    Properties
    {
        _FillColor ("Fill Color", Color) = (0,0,0,1)
        _Opacity   ("Opacity", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _FillColor;
            float  _Opacity;

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 vertex : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = _FillColor;
                c.a *= saturate(_Opacity);
                return c;
            }
            ENDCG
        }
    }
}
