Shader "CG/Water"
{
    Properties
    {
        _CubeMap("Reflection Cube Map", Cube) = "" {}
        _NoiseScale("Texture Scale", Range(1, 100)) = 10 
        _TimeScale("Time Scale", Range(0.1, 5)) = 3 
        _BumpScale("Bump Scale", Range(0, 0.5)) = 0.05
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM

                #pragma vertex vert
                #pragma fragment frag
                #include "UnityCG.cginc"
                #include "CGUtils.cginc"
                #include "CGRandom.cginc"

                #define DELTA 0.01

                // Declare used properties
                uniform samplerCUBE _CubeMap;
                uniform float _NoiseScale;
                uniform float _TimeScale;
                uniform float _BumpScale;

                struct appdata
                { 
                    float4 vertex   : POSITION;
                    float3 normal   : NORMAL;
                    float4 tangent  : TANGENT;
                    float2 uv       : TEXCOORD0;
                };

                struct v2f
                {
                    float4 pos      : SV_POSITION;
                    float2 uv         : TEXCOORD0;
                    float3 worldPos   : TEXCOORD1;
                    float3 worldNormal: TEXCOORD2;
                    float3 worldTangent: TEXCOORD3;
                };

                // Returns the value of a noise function simulating water, at coordinates uv and time t
                float waterNoise(float2 uv, float t)
                {
                    // 3D Perlin layers at different scales & speeds:
                    // Perlin3D(0.5u, 0.5v, 0.5t)
                    // + 0.5 * Perlin3D(u, v, t)
                    // + 0.2 * Perlin3D(2u, 2v, 3t)
                    float3 p1 = float3(0.5 * uv.x, 0.5 * uv.y, 0.5 * t);
                    float3 p2 = float3(uv.x,       uv.y,       t);
                    float3 p3 = float3(2.0 * uv.x, 2.0 * uv.y, 3.0 * t);

                    return perlin3d(p1)
                         + 0.5 * perlin3d(p2)
                         + 0.2 * perlin3d(p3);
                }

                // Returns the world-space bump-mapped normal for the given bumpMapData and time t
                float3 getWaterBumpMappedNormal(bumpMapData i, float t)
                {
                    // Sample procedural "height" from waterNoise instead of a texture
                    float hL = waterNoise(i.uv - float2(i.du, 0.0), t);
                    float hR = waterNoise(i.uv + float2(i.du, 0.0), t);
                    float hD = waterNoise(i.uv - float2(0.0, i.dv), t);
                    float hU = waterNoise(i.uv + float2(0.0, i.dv), t);

                    // Central differences
                    float dhdu = (hR - hL) / (2.0 * i.du);
                    float dhdv = (hU - hD) / (2.0 * i.dv);

                    // Tangent-space bumped normal
                    float3 n_tangent = normalize(float3(-dhdu * i.bumpScale,
                                                        -dhdv * i.bumpScale,
                                                         1.0));

                    // World-space TBN basis
                    float3 N = normalize(i.normal);
                    float3 T = normalize(i.tangent);
                    float3 B = normalize(cross(N, T));

                    // Transform to world space
                    float3 worldN = normalize(
                        n_tangent.x * T +
                        n_tangent.y * B +
                        n_tangent.z * N
                    );

                    return worldN;
                }


                v2f vert (appdata input)
                {
                    v2f output;

                    // Time for animation
                    float t = _Time.y * _TimeScale;

                    // --- Sample Perlin noise for displacement ---
                    float h = waterNoise(input.uv * _NoiseScale, t);

                    // Map noise from [-1,1] → [0,1]
                    h = h * 0.5 + 0.5;

                    // Height displacement
                    float displacement = h * _BumpScale;

                    // Move vertex along its normal (object space)
                    float3 displacedPosOS = input.vertex.xyz + input.normal * displacement;

                    // World-space position & normal/tangent
                    float3 worldPos    = mul(unity_ObjectToWorld, float4(displacedPosOS, 1.0)).xyz;
                    float3 worldNormal = UnityObjectToWorldNormal(input.normal);
                    float3 worldTangent= UnityObjectToWorldDir(input.tangent.xyz);

                    output.pos          = UnityObjectToClipPos(float4(displacedPosOS, 1.0));
                    output.uv           = input.uv;
                    output.worldPos     = worldPos;
                    output.worldNormal  = worldNormal;
                    output.worldTangent = worldTangent;
                    return output;
                }

                fixed4 frag (v2f input) : SV_Target
                {
                    // Build bumpMapData in world space
                    bumpMapData bm;
                    bm.normal    = input.worldNormal;
                    bm.tangent   = input.worldTangent;
                    bm.uv        = input.uv * _NoiseScale;
                    bm.du        = DELTA;
                    bm.dv        = DELTA;
                    bm.bumpScale = _BumpScale;

                    // Time (scaled) for animated normals
                    float t = _Time.y * _TimeScale;

                    // Bump-mapped normal from procedural water noise
                    float3 n = normalize(getWaterBumpMappedNormal(bm, t));

                    // View direction (surface -> camera)
                    float3 v = normalize(_WorldSpaceCameraPos - input.worldPos);

                    // Reflected view direction
                    float3 r = reflect(-v, n);

                    // Sample cube map with reflected direction
                    fixed4 reflectedColor = texCUBE(_CubeMap, r);

                    // Fresnel-like term: (1 - max{0, n·v} + 0.2)
                    float ndotv = max(0.0, dot(n, v));
                    float factor = 1.0 - ndotv + 0.2;

                    return reflectedColor * factor;
                }
            ENDCG
        }
    }
}
