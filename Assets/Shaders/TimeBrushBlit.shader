Shader "Custom/TimeBrushBlit"
{
    Properties
    {
        _MainTex       ("Source (Time RT)", 2D)       = "black" {}
        _BrushCenter   ("Brush Center",     Vector)   = (0.5, 0.5, 0, 0)
        _BrushHalfSize ("Brush Half Size",  Vector)   = (0.1, 0.1, 0, 0)
        
        _PaintTime     ("Paint Time", Float) = 0.0
        _IsFill        ("Is Fill", Float) = 0.0
        _CornerRadius  ("Corner Radius", Range(0, 1)) = 0.2
        
        // value to clear the texture to when we want to reset it. 
        _MaxAge        ("Max Age", Float) = 10.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f     { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _MainTex;
            float4    _BrushCenter;
            float4    _BrushHalfSize;
            float     _PaintTime;
            float     _IsFill;
            float     _CornerRadius;

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

                // 1. geometry check - is the pixel within the brush shape?
                float2 halfSize = max(_BrushHalfSize.xy, float2(1e-6, 1e-6));
                float2 rel = (i.uv - _BrushCenter.xy) / halfSize;
                float2 d = abs(rel) - (1.0 - _CornerRadius);
                float dist = length(max(d, 0.0));

                // if were outside the brush shape, just return the existing value without modification.
                if (dist > _CornerRadius)
                    return existing;

                // 2. age check - if the existing paint is newer than the current paint, don't overwrite it.
                // we write the "age" of the paint in the R channel, so if the existing R value is greater than the current paint time, we skip it.
                // is it a fill? G = 1.0, is it a trail? G = 0.0
                float isFillMarker = (_IsFill > 0.5) ? 1.0 : 0.0;
                
                return float4(_PaintTime, isFillMarker, 0, 1);
            }
            ENDCG
        }
    }
}