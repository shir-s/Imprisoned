// Writes to time texture for TRAILS and FILLS
// R = Paint Time, G = IsFill (0 = trail, 1 = fill)
// NEW: When painting fill, skip fresh trail pixels
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
        _IsFill        ("Is Fill (0=trail, 1=fill)", Float) = 0.0
        _MaxAge        ("Max Age (seconds)", Float) = 10.0
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
            float     _IsFill;
            float     _MaxAge;

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

                // NEW: Fresh trail protection
                if (_IsFill > 0.5 && mask > 0.01)
                {
                    float existingIsFill = existing.g;
                    float existingTime = existing.r;
                    
                    // If existing pixel is a trail (G < 0.5) and has been painted (time > 0)
                    if (existingIsFill < 0.5 && existingTime > 0.0)
                    {
                        float age = _PaintTime - existingTime;
                        if (age < _MaxAge)
                        {
                            // Fresh trail - don't paint over it
                            return existing;
                        }
                    }
                }

                float finalTime = lerp(existing.r, _PaintTime, step(0.01, mask));
                float fillFlag = lerp(existing.g, _IsFill, step(0.01, mask));

                return float4(finalTime, fillFlag, 0, 1);
            }
            ENDCG
        }
    }
}