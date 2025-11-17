// FILEPATH: Assets/Shaders/SimpleBrushBlit.shader
Shader "Custom/SimpleBrushBlit"
{
    Properties
    {
        _MainTex        ("Paint RT", 2D) = "black" {}
        _BrushColor     ("Brush Color", Color) = (0,0,0,1)
        _BrushCenter    ("Brush Center (UV)", Vector) = (0.5,0.5,0,0)

        // HALF size of the square in UV (0..1).
        // Example: (0.05, 0.05) = square 0.1×0.1 in UV space.
        _BrushHalfSize  ("Brush Half-Size (UV)", Vector) = (0.05, 0.05, 0, 0)

        _BrushHardness  ("Brush Hardness", Range(0,1)) = 0.5
        _BrushOpacity   ("Brush Opacity", Range(0,1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

            float4 _BrushColor;
            float4 _BrushCenter;
            float4 _BrushHalfSize;
            float  _BrushHardness;
            float  _BrushOpacity;

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
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 oldCol = tex2D(_MainTex, i.uv);

                float2 halfSize = _BrushHalfSize.xy;
                if (halfSize.x <= 1e-6 || halfSize.y <= 1e-6)
                    return oldCol;

                // Distance from center in UV
                float2 d = abs(i.uv - _BrushCenter.xy);

                // Normalized distance inside square: 0 at center, 1 at edges
                float2 norm = d / halfSize;
                float  edgeDist = max(norm.x, norm.y); // 0 center, 1 at border, >1 outside square

                // Hardness: inner fully opaque zone vs feathered border
                // hardness = 0.5 → inner 0..0.5 solid, 0.5..1 smooth fade
                float inner = 1.0 - _BrushHardness;
                float outer = 1.0;

                float mask = saturate(1.0 - smoothstep(inner, outer, edgeDist));

                float a = mask * _BrushOpacity * _BrushColor.a;
                if (a <= 0.0)
                    return oldCol;

                fixed4 brushCol = _BrushColor;
                brushCol.a = a;

                fixed4 outCol;
                outCol.rgb = lerp(oldCol.rgb, brushCol.rgb, brushCol.a);
                outCol.a   = saturate(oldCol.a + brushCol.a);

                return outCol;
            }
            ENDCG
        }
    }
}
