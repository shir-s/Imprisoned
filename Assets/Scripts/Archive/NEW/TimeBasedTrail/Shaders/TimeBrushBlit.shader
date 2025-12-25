// This shader writes ONLY to the time texture (_PaintTimeTex)
// Stores WHEN each pixel was painted in RAW SECONDS
Shader "Custom/TimeBrushBlit"
{
    Properties
    {
        _MainTex       ("Source (Time RT)", 2D)       = "black" {}
        _BrushCenter   ("Brush Center",     Vector)   = (0.5, 0.5, 0, 0)
        _BrushHalfSize ("Brush Half Size",  Vector)   = (0.1, 0.1, 0, 0)
        _BrushHardness ("Brush Hardness",   Range(0,1)) = 0.5
        _BrushOpacity  ("Brush Opacity",    Range(0,1)) = 1.0
        _PaintTime     ("Paint Time (seconds)", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Cull Off
        ZWrite Off
        ZTest Always

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

            sampler2D _MainTex;
            float4    _BrushCenter;
            float4    _BrushHalfSize;
            float     _BrushHardness;
            float     _BrushOpacity;
            float     _PaintTime;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float4 existing = tex2D(_MainTex, i.uv);

                float2 halfSize = max(_BrushHalfSize.xy, float2(1e-6, 1e-6));
                float2 rel = (i.uv - _BrushCenter.xy) / halfSize;

                float r = max(abs(rel.x), abs(rel.y));

                if (r >= 1.5)
                    return existing;

                float inner = 0.6;
                float outer = 1.5;
                float t = saturate((r - inner) / (outer - inner));

                float mask = 1.0 - smoothstep(0.0, 1.0, t);
                float hard = saturate(_BrushHardness);
                float power = lerp(1.0, 4.0, hard);
                mask = pow(mask, power);
                mask *= saturate(_BrushOpacity);

                // If we're painting here (mask > threshold), use new time
                // Otherwise keep existing time
                float finalTime = (mask > 0.01) ? _PaintTime : existing.r;

                return float4(finalTime, 0, 0, 1);
            }
            ENDCG
        }
    }
}
