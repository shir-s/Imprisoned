// FILEPATH: Assets/Shaders/UI/RadialSpriteDonut.shader
Shader "UI/RadialSpriteDonut"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // --- Unity UI required (Mask, etc.) ---
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // --- Our controls ---
        _Mode ("Mode (0=center, 1=ring)", Float) = 1

        // Radii normalized so that 0.5 ~= edge of the smallest rect half-size
        _InnerRadius ("Inner Radius (0..0.49)", Range(0,0.49)) = 0.20
        _OuterRadius ("Outer Radius (0.01..0.50)", Range(0.01,0.50)) = 0.50

        _Fill ("Fill (0..1)", Range(0,1)) = 0
        _StartAngleDeg ("Start Angle (deg)", Float) = 90
        _Clockwise ("Clockwise (1/0)", Float) = 1

        // Provided by script
        _RectSize ("Rect Size", Vector) = (100,100,0,0)
        _Pivot01 ("Pivot (0..1)", Vector) = (0.5,0.5,0,0)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;   // UI local space (pivot-relative)
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
                float2 localXY  : TEXCOORD2;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            float4 _ClipRect;
            float _UseUIAlphaClip;

            float _Mode;
            float _InnerRadius;
            float _OuterRadius;

            float _Fill;
            float _StartAngleDeg;
            float _Clockwise;

            float4 _RectSize; // xy used
            float4 _Pivot01;  // xy used

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPos = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                o.localXY = v.vertex.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;

                // Respect UI clipping/masks
                c.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                if (c.a <= 0.0001)
                    discard;

                // Compute position relative to rect center (independent of atlas UVs)
                float2 size = max(_RectSize.xy, float2(1e-4, 1e-4));

                // localXY is pivot-relative, so center is at:
                // centerLocal = (0.5 - pivot) * size
                float2 centerLocal = (float2(0.5, 0.5) - _Pivot01.xy) * size;

                float2 p = i.localXY - centerLocal;

                // Normalize: edge of smallest half-size ~= 0.5
                float halfMin = 0.5 * min(size.x, size.y);
                halfMin = max(halfMin, 1e-4);

                float2 pn = p / (2.0 * halfMin); // roughly -0.5..+0.5
                float r = length(pn);

                if (_Mode < 0.5)
                {
                    // center only
                    if (r > _InnerRadius)
                        discard;

                    if (_UseUIAlphaClip > 0.5)
                        clip(c.a - 0.001);

                    return c;
                }

                // ring only
                if (r < _InnerRadius || r > _OuterRadius)
                    discard;

                // angle in [0..2pi)
                float ang = atan2(pn.y, pn.x);
                if (ang < 0) ang += 6.2831853;

                float start = radians(_StartAngleDeg);
                start = fmod(start, 6.2831853);
                if (start < 0) start += 6.2831853;

                float rel = ang - start;
                if (rel < 0) rel += 6.2831853;

                float t = rel / 6.2831853;
                if (_Clockwise > 0.5)
                    t = 1.0 - t;

                if (t > _Fill)
                    discard;

                if (_UseUIAlphaClip > 0.5)
                    clip(c.a - 0.001);

                return c;
            }
            ENDCG
        }
    }
}
