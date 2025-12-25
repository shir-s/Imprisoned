Shader "Custom/PaintSurfaceWithAging"
{
    Properties
    {
        _BaseMap    ("Base Texture", 2D)    = "white" {}
        _BaseColor  ("Base Color",   Color) = (1,1,1,1)
        _PaintTex   ("Paint Texture", 2D)   = "black" {}
        _PaintTimeTex ("Paint Time Texture", 2D) = "black" {}
        
        // Aging settings
        _CurrentTime ("Current Time (0-1)", Float) = 0.0
        _FreshColor  ("Fresh Paint Color", Color) = (0.2, 0.8, 0.3, 1) // Green
        _OldColor    ("Old Paint Color",   Color) = (0.5, 0.5, 0.5, 1) // Gray
        _AgingSpeed  ("Aging Speed", Float) = 0.1
        _MaxAge      ("Max Age (for full gray)", Float) = 0.3
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
            sampler2D _PaintTimeTex;
            
            float  _CurrentTime;
            float4 _FreshColor;
            float4 _OldColor;
            float  _AgingSpeed;
            float  _MaxAge;

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
                fixed4 timeData = tex2D(_PaintTimeTex, i.uv);
                
                float paintTime = timeData.r;  // When this pixel was painted
                float hasPaint = timeData.g;   // 1 if painted, 0 if not
                
                fixed paintAlpha = saturate(paint.a);
                
                // If no paint, just show base
                if (paintAlpha < 0.01 || hasPaint < 0.5)
                {
                    return fixed4(baseCol.rgb, 1.0);
                }
                
                // Calculate age (current time - paint time)
                // Times are normalized 0-1, wrapping handled
                float age = _CurrentTime - paintTime;
                
                // Handle time wrapping (if current < paint, we wrapped around)
                if (age < 0) age += 1.0;
                
                // Normalize age to 0-1 range based on MaxAge
                float normalizedAge = saturate(age / _MaxAge);
                
                // Lerp from fresh color to old color based on age
                fixed3 paintColor = lerp(_FreshColor.rgb, _OldColor.rgb, normalizedAge);
                
                // You can also use the original paint.rgb and just desaturate it:
                // float gray = dot(paint.rgb, float3(0.299, 0.587, 0.114));
                // fixed3 paintColor = lerp(paint.rgb, float3(gray, gray, gray), normalizedAge);
                
                // Blend paint over base
                fixed3 finalColor = lerp(baseCol.rgb, paintColor, paintAlpha);

                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }
}
