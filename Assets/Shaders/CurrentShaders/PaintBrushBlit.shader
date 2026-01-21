Shader "Custom/PaintBrushBlit"
{
    Properties
    {
        _MainTex       ("Source (Paint RT)", 2D)      = "black" {}
        // נוסיף את טקסטורת הזמן כדי שנוכל לבדוק אותה
        _TimeTex       ("Time Texture (Logic)", 2D)   = "black" {} 
        
        _BrushCenter   ("Brush Center",      Vector)  = (0.5, 0.5, 0, 0)
        _BrushHalfSize ("Brush Half Size",   Vector)  = (0.1, 0.1, 0, 0)
        _BrushOpacity  ("Brush Opacity",     Range(0,1)) = 1.0
        _BrushColor    ("Brush Color",       Color)   = (0,0,0,1)
        _CornerRadius  ("Corner Radius",     Range(0, 1)) = 0.2
        
        _PaintTime     ("Current Time", Float) = 0.0
        _PaintType     ("Paint Type (0=Trail, 1=Fill)", Float) = 0.0
        _MaxAge        ("Trail Protection Age", Float) = 10.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

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
            sampler2D _TimeTex; // הטקסטורה החדשה לקריאה
            
            float4    _BrushCenter;
            float4    _BrushHalfSize;
            float     _BrushOpacity;
            float     _CornerRadius;
            float     _PaintTime;
            float     _PaintType;
            float     _MaxAge;

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

                // --- 1. גיאומטריה ---
                float2 halfSize = max(_BrushHalfSize.xy, float2(1e-6, 1e-6));
                float2 rel = (i.uv - _BrushCenter.xy) / halfSize;
                float2 d = abs(rel) - (1.0 - _CornerRadius);
                float dist = length(max(d, 0.0));

                if (dist > _CornerRadius)
                    return existing;

                // --- 2. לוגיקת הגנה (Protection Logic) ---
                // אנחנו מייבאים את "המוח" של שיידר הזמן לכאן!
                
                // אם אנחנו מנסים לצייר מילוי (Fill)...
                if (_PaintType > 0.5)
                {
                    // נקרא את הנתונים מטקסטורת הזמן
                    float4 timeData = tex2D(_TimeTex, i.uv);
                    float existingIsFill = timeData.g; // G מחזיק את הסוג (0 או 1)
                    float existingTime = timeData.r;   // R מחזיק את הזמן
                    
                    // אם הפיקסל הנוכחי הוא שובל (0) והוא קיים...
                    if (existingIsFill < 0.5 && existingTime > 0.0)
                    {
                        // נבדוק בן כמה הוא
                        float age = _PaintTime - existingTime;
                        
                        // אם הוא צעיר מידי - אסור לדרוס אותו!
                        // מחזירים את הצבע הקיים ויוצאים (לא מציירים כלום)
                        if (age < _MaxAge)
                            return existing;
                    }
                }

                // --- 3. צביעה (אם עברנו את ההגנה) ---
                float mask = saturate(_BrushOpacity);
                fixed4 result = existing;
                float killFactor = 100.0;

                if (_PaintType < 0.5) 
                {
                    // Trail: כותב לאדום, מוחק ירוק
                    result.r = max(result.r, mask);
                    result.g = saturate(result.g - mask * killFactor);
                }
                else 
                {
                    // Fill: כותב לירוק, מוחק אדום
                    result.g = max(result.g, mask);
                    result.r = saturate(result.r - mask * killFactor);
                }

                result.a = saturate(existing.a + mask * (1.0 - existing.a));
                return result;
            }

            ENDCG
        }
    }
}