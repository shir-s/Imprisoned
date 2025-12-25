Shader "Custom/PaintBrushBlit"
{
    Properties
    {
        _MainTex       ("Source (Paint RT)", 2D)      = "black" {}
        _BrushCenter   ("Brush Center",      Vector)  = (0.5, 0.5, 0, 0)
        _BrushHalfSize ("Brush Half Size",   Vector)  = (0.1, 0.1, 0, 0)
        _BrushHardness ("Brush Hardness",    Range(0,1)) = 0.5
        _BrushOpacity  ("Brush Opacity",     Range(0,1)) = 1.0
        _BrushColor    ("Brush Color",       Color)   = (0,0,0,1)
        
        // NEW: Time data for aging system
        _PaintTime     ("Paint Time (0-1 normalized)", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

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
            float4    _BrushCenter;    // xy
            float4    _BrushHalfSize;  // xy
            float     _BrushHardness;
            float     _BrushOpacity;
            float4    _BrushColor;
            float     _PaintTime;      // NEW: normalized time when painting

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 existing = tex2D(_MainTex, i.uv);

                float2 halfSize = max(_BrushHalfSize.xy, float2(1e-6, 1e-6));
                float2 rel = (i.uv - _BrushCenter.xy) / halfSize;

                // square distance instead of circular
                float r = max(abs(rel.x), abs(rel.y));

                // soft square fade
                if (r >= 1.5)
                    return existing;

                float inner = 0.6;
                float outer = 1.5;

                float t = saturate((r - inner) / (outer - inner));

                // softness
                float mask = 1.0 - smoothstep(0.0, 1.0, t);
                float hard = saturate(_BrushHardness);
                float power = lerp(1.0, 4.0, hard);
                mask = pow(mask, power);

                // opacity
                mask *= saturate(_BrushOpacity);

                fixed3 brushRGB = _BrushColor.rgb;

                fixed3 finalRGB = lerp(existing.rgb, brushRGB, mask);
                fixed  finalA   = saturate(existing.a + mask * (1.0 - existing.a));
                
                // NEW: Store paint time in a way that new paint overwrites old
                // We use the Blue channel since your poison effect is green-based
                // Or we can use a separate texture (see TimeBrushBlit shader)
                
                return fixed4(finalRGB, finalA);
            }

            ENDCG
        }
    }
}
