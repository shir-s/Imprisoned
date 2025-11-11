// FILEPATH: Assets/Shaders/Hidden/VolumeStamp.shader
Shader "Hidden/VolumeStamp"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        // No blending; we add inside the shader so we can read previous height
        Pass
        {
            Name "Stamp"
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _CenterST;             // xy = uv center
            float4 _RadiusStrengthSoft;   // x = radius (normalized), y = strength, z = softness 0..1
            TEXTURE2D(_SourceTex); SAMPLER(sampler_SourceTex); // previous height

            struct VOut { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            VOut vert(uint id:SV_VertexID)
            {
                VOut o;
                float2 p = float2((id<<1)&2, id&2); // full-screen tri
                o.pos = float4(p*2-1,0,1);
                o.uv  = p;
                return o;
            }

            float smoothDisk(float2 uv, float2 c, float r, float soft)
            {
                float d = length(uv - c);
                float i0 = r*(1.0-soft);
                float i1 = r;
                return saturate((i1 - d)/max(1e-5, i1 - i0));
            }

            half4 frag(VOut i):SV_Target
            {
                float2 c = _CenterST.xy;
                float r  = _RadiusStrengthSoft.x;
                float s  = _RadiusStrengthSoft.y;
                float soft = _RadiusStrengthSoft.z;

                half prev = SAMPLE_TEXTURE2D(_SourceTex, sampler_SourceTex, i.uv).r;
                half add  = s * smoothDisk(i.uv, c, r, soft);
                return half4(prev + add, 0,0,0); // output new height into R
            }
            ENDHLSL
        }
    }
    Fallback Off
}
