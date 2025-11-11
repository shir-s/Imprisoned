// FILEPATH: Assets/Shaders/JamStrokeURP.shader
Shader "URP/JamStroke"
{
    Properties
    {
        _JamColor        ("Jam Color (HDR, alpha=thickness)", Color) = (0.58, 0.0, 0.11, 0.85)
        _EdgeDarken      ("Edge Darkening 0..1", Range(0,1)) = 0.6
        _EdgeWidth       ("Edge Width (side UV)", Range(0.0,0.5)) = 0.22
        _Specular        ("Specular Strength", Range(0,2)) = 1.0
        _Smoothness      ("Smoothness (0..1)", Range(0,1)) = 0.9
        _Transmission    ("Transmission (soft back light)", Range(0,1)) = 0.5
        _RefractStrength ("Fake Refraction (needs OpaqueTex)", Range(0,0.05)) = 0.01
        _NoiseTex        ("Streak/Bubble Noise (R)", 2D) = "gray" {}
        _NoiseScale      ("Noise Scale", Float) = 6.0
        _NoiseAmount     ("Noise Amount", Range(0,1)) = 0.25

        _BaseLift        ("Base Lift above Paper (m)", Range(0,0.01)) = 0.001
        _MaxDisplace     ("Max Vertex Displace (m)",  Range(0,0.02)) = 0.006

        // NEW controls for readability
        _UnlitMix        ("Unlit Base Mix (0..1)", Range(0,1)) = 0.35
        _AlphaBoost      ("Alpha Boost (x)", Range(0.25,4)) = 1.6
        _HeightToAlpha   ("Height → Alpha Weight", Range(0,1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        // Keep standard alpha blending, but write depth so paper doesn't wash through.
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _RECEIVE_SHADOWS_OFF
            #pragma multi_compile _ _USE_OPAQUE_TEXTURE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_NoiseTex);               SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_JamVolumeTex);           SAMPLER(sampler_JamVolumeTex);
            TEXTURE2D_X(_CameraOpaqueTexture);  SAMPLER(sampler_CameraOpaqueTexture);
            float4 _JamVolumeTex_TexelSize;

            // globals set by VolumeMap
            float4x4 _JamWorldToPaper;
            float3   _JamPaperRightWS;
            float3   _JamPaperFwdWS;

            CBUFFER_START(UnityPerMaterial)
                half4 _JamColor;
                half  _EdgeDarken;
                half  _EdgeWidth;
                half  _Specular;
                half  _Smoothness;
                half  _Transmission;
                half  _RefractStrength;
                half  _NoiseScale;
                half  _NoiseAmount;
                half  _BaseLift;
                half  _MaxDisplace;
                half  _UnlitMix;
                half  _AlphaBoost;
                half  _HeightToAlpha;
            CBUFFER_END

            struct Attributes {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0; // x=along, y=side [0..1]
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                float2 uvPaper    : TEXCOORD4;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                float3 ws  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);

                // small lift from the paper
                ws += nWS * _BaseLift;

                // paper uv
                float4 paper = mul(_JamWorldToPaper, float4(ws,1));
                float2 uvPaper = paper.xz;

                // height-based vertex push
                half h = SAMPLE_TEXTURE2D_LOD(_JamVolumeTex, sampler_JamVolumeTex, uvPaper, 0).r;
                ws += nWS * (_MaxDisplace * h);

                OUT.positionWS = ws;
                OUT.positionCS = TransformWorldToHClip(ws);
                OUT.normalWS   = nWS;
                OUT.uv         = IN.uv;
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                OUT.uvPaper    = uvPaper;
                return OUT;
            }

            half3 SpecularTerm(half3 n, half3 v, half3 l, half smoothness, half specMul)
            {
                half3 h = normalize(l + v);
                half  ndh = saturate(dot(n,h));
                half  ndl = saturate(dot(n,l));
                half  ndv = saturate(dot(n,v));
                half  rough = saturate(1.0h - smoothness);
                half  m = rough*rough;
                half  d = max(1e-4h, (ndh*ndh)*(m-1.h)+1.h);
                half  spec = (smoothness*smoothness) / (d*d);
                spec *= ndl * ndv;
                half s = spec * specMul;
                return half3(s, s, s);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // sample height and gradient
                half hC  = SAMPLE_TEXTURE2D(_JamVolumeTex, sampler_JamVolumeTex, IN.uvPaper).r;
                half hPx = SAMPLE_TEXTURE2D(_JamVolumeTex, sampler_JamVolumeTex, IN.uvPaper + float2(_JamVolumeTex_TexelSize.x, 0)).r;
                half hPy = SAMPLE_TEXTURE2D(_JamVolumeTex, sampler_JamVolumeTex, IN.uvPaper + float2(0, _JamVolumeTex_TexelSize.y)).r;
                float dhdx = (hPx - hC);
                float dhdy = (hPy - hC);

                float3 bumpWS = normalize(IN.normalWS
                                          - (_MaxDisplace * dhdx) * _JamPaperRightWS
                                          - (_MaxDisplace * dhdy) * _JamPaperFwdWS);

                // base jam color & edge darken
                half4 jam = _JamColor;
                half side = abs(IN.uv.y - 0.5h) / max(1e-4h, _EdgeWidth);
                half edgeMask = saturate(1.0h - side);
                half edgeDark = lerp(1.0h - _EdgeDarken, 1.0h, edgeMask);

                // noise
                float2 nUV = IN.uv * _NoiseScale;
                half noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nUV).r;
                half jamMod = lerp(1.0h - _NoiseAmount, 1.0h, noise);

                half3 baseCol = jam.rgb * edgeDark * jamMod;

                // lighting
                half3 n = bumpWS;
                half3 v = normalize(GetWorldSpaceViewDir(IN.positionWS));
                Light mainLight = GetMainLight();
                half3 l = normalize(mainLight.direction);
                half  ndl = saturate(dot(n, l));
                half  wrap = 0.35h;
                half  diff = saturate((ndl + wrap) / (1.0h + wrap));
                half  back = saturate(dot(-n, l));
                half  transmit = back * _Transmission;
                half3 spec = SpecularTerm(n, v, l, _Smoothness, _Specular);

                half3 lit = baseCol * (diff * mainLight.color.rgb + transmit * mainLight.color.rgb) + spec;

                #if defined(_USE_OPAQUE_TEXTURE)
                float2 uvSS = IN.screenPos.xy / IN.screenPos.w;
                float2 offset = n.xy * _RefractStrength;
                half3 refr = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uvSS + offset).rgb;
                lit = lerp(lit, refr * baseCol, 0.25h);
                #endif

                // --- visibility fixes ---
                // 1) keep some unlit tint so it doesn't disappear in bad lighting
                lit = lerp(lit, baseCol, _UnlitMix);

                // 2) stronger alpha, optionally scaled by height so thicker strokes look denser
                half alphaFromHeight = lerp(1.0h, saturate(0.2h + hC * 0.8h), _HeightToAlpha);
                half outA = saturate(jam.a * (edgeMask * 0.7h + 0.5h) * alphaFromHeight * _AlphaBoost);

                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                    lit *= lerp(0.4h, 1.0h, shadow);
                #endif

                return half4(lit, outA);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
