// FILEPATH: Assets/Shaders/Stroke_Unlit_NoTexture.shader
Shader "Custom/Stroke_Unlit_NoTexture"
{
    Properties
    {
        _Tint("Tint (RGB) & Opacity (A)", Color) = (0.18,0.18,0.18,1)

        // Fraction of the ribbon width that stays fully opaque (0..1).
        // 1.0 = fill the entire geometry width before edge feather.
        _CoreFill("Core Fill (0..1)", Range(0.0, 1.0)) = 1.0

        // Feather only the last fraction of each side, in *fraction of width* (0..0.5).
        _EdgeSoft("Edge Feather", Range(0.0, 0.5)) = 0.10

        // Optional: if > 0, forces the visible core to this world width (meters), independent of geometry.
        // Leave 0 to use the geometry world width (auto).
        _DesiredWorldWidth("Desired World Width (m)", Float) = 0.0

        // Subtle sharpening of the core profile (1 = off).
        _Hardness("Core Hardness", Range(1.0, 4.0)) = 1.0

        // Grit
        _NoiseAmp("Grit Amount", Range(0.0, 1.0)) = 0.20
        _NoiseScale("Grit Scale", Range(1.0, 200.0)) = 40.0

        _Alpha("Overall Alpha", Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        // Transparent, but write depth so the background doesn’t wash it out.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Tint;
            float _CoreFill, _EdgeSoft, _DesiredWorldWidth, _Hardness, _NoiseAmp, _NoiseScale, _Alpha;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0; // uv.x across width [0..1], uv.y along
            };

            struct v2f {
                float4 pos        : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos        = UnityObjectToClipPos(v.vertex);
                o.uv         = v.uv;
                o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // tiny deterministic noise
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float4 frag (v2f i) : SV_Target
            {
                // -------------------------------------------
                // 1) Estimate geometry width in WORLD meters.
                //    We compute d(positionWS)/d(uv.x) via screen derivatives.
                // -------------------------------------------
                float du_dx  = ddx(i.uv.x),  du_dy  = ddy(i.uv.x);
                float3 dp_dx = ddx(i.positionWS), dp_dy = ddy(i.positionWS);

                float denom  = max(1e-6, du_dx*du_dx + du_dy*du_dy);
                float3 dp_du = (dp_dx * du_dx + dp_dy * du_dy) / denom;

                // |dp/du| is meters per 1.0 of uv.x (i.e., full width).
                float worldWidthGeom = length(dp_du);

                // Choose target world width:
                // - if _DesiredWorldWidth > 0, lock to it;
                // - else fill the whole geometry width.
                float worldWidthTarget = (_DesiredWorldWidth > 0.0) ? _DesiredWorldWidth : worldWidthGeom;

                // Map uv.x distance-from-center into *world meters* using the geometry width.
                float halfGeom = max(1e-6, 0.5 * worldWidthGeom);
                float sMeters  = abs(i.uv.x - 0.5) * (worldWidthGeom); // 0 at center → ~halfGeom at side

                // -------------------------------------------
                // 2) Core region & feather in world meters.
                //    We want the fully opaque core to occupy _CoreFill of the TARGET width.
                // -------------------------------------------
                float halfTargetCore = 0.5 * worldWidthTarget * saturate(_CoreFill);
                float halfTargetSoft = 0.5 * worldWidthTarget * _EdgeSoft * 2.0; // feather band on each side

                // If we *only* want to fill geometry, worldWidthTarget==worldWidthGeom → perfect 1:1 core.
                // Now build a piecewise alpha: 1 inside core, smooth fade in the feather band.
                float innerMeters = max(0.0, halfTargetCore - halfTargetSoft);
                float edgeAlpha   = smoothstep(halfTargetCore, innerMeters, sMeters);

                float alphaCore = pow(edgeAlpha, _Hardness);

                // -------------------------------------------
                // 3) Grit (multiplies alpha), using stable world-ish coords.
                // -------------------------------------------
                float n = hash21(i.positionWS.xz * _NoiseScale);
                float grit = 1.0 - _NoiseAmp * (n * 0.8 + 0.2);

                float a = saturate(alphaCore * grit) * _Tint.a * _Alpha;
                float3 col = _Tint.rgb;

                return float4(col, a);
            }
            ENDHLSL
        }
    }
}
