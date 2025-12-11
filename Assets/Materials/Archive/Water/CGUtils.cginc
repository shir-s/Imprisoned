#ifndef CG_UTILS_INCLUDED
#define CG_UTILS_INCLUDED

#define PI 3.141592653

// A struct containing all the data needed for bump-mapping
struct bumpMapData
{ 
    float3 normal;       // Mesh surface normal at the point
    float3 tangent;      // Mesh surface tangent at the point
    float2 uv;           // UV coordinates of the point
    sampler2D heightMap; // Heightmap texture to use for bump mapping
    float du;            // Increment size for u partial derivative approximation
    float dv;            // Increment size for v partial derivative approximation
    float bumpScale;     // Bump scaling factor
};


// Receives pos in 3D cartesian coordinates (x, y, z)
// Returns UV coordinates corresponding to pos using spherical texture mapping
float2 getSphericalUV(float3 pos)
{
    // Normalize position to lie on the unit sphere
    float3 p = normalize(pos);

    // Longitude (around Y axis), range (-PI, PI]
    float longitude = atan2(p.z, p.x);

    // Latitude (from equator to poles), range [-PI/2, PI/2]
    float latitude  = asin(-p.y);

    // Map longitude, latitude to [0,1]x[0,1]
    float u = 0.5 + longitude / (2.0 * PI);  // 0..1 around the sphere
    float v = 0.5 - latitude  / PI;          // 1 at north pole, 0 at south pole

    return float2(u, v);
}

// Implements an adjusted version of the Blinn-Phong lighting model
fixed3 blinnPhong(float3 n, float3 v, float3 l, float shininess, fixed4 albedo, fixed4 specularity, float ambientIntensity)
{
    // Make sure all direction vectors are normalized
    float3 N = normalize(n);
    float3 V = normalize(v);
    float3 L = normalize(l);

    // Halfway vector between view and light directions
    float3 H = normalize(V + L);

    // ----- Ambient -----
    fixed3 ambient = ambientIntensity * albedo.rgb;

    // ----- Diffuse -----
    float NdotL = max(0.0, dot(N, L));
    fixed3 diffuse = NdotL * albedo.rgb;

    // ----- Specular (Blinn-Phong) -----
    float NdotH = max(0.0, dot(N, H));
    fixed3 specular = pow(NdotH, shininess) * specularity.rgb;

    // Sum of all components
    return ambient + diffuse + specular;
}

// Returns the world-space bump-mapped normal for the given bumpMapData
float3 getBumpMappedNormal(bumpMapData i)
{
    // --- Sample height values around the current UV ---
    float hL = tex2D(i.heightMap, i.uv - float2(i.du, 0.0)).r;
    float hR = tex2D(i.heightMap, i.uv + float2(i.du, 0.0)).r;
    float hD = tex2D(i.heightMap, i.uv - float2(0.0, i.dv)).r;
    float hU = tex2D(i.heightMap, i.uv + float2(0.0, i.dv)).r;

    // Central differences: partial derivatives of height w.r.t. u and v
    float dhdu = (hR - hL) / (2.0 * i.du);
    float dhdv = (hU - hD) / (2.0 * i.dv);

    // --- Build tangent-space bumped normal ---
    // BumpScale controls how strong the pertubation is
    float3 n_tangent = normalize(float3(-dhdu * i.bumpScale,
                                        dhdv * i.bumpScale,
                                         1.0));

    // --- Construct TBN basis in world space ---
    float3 N = normalize(i.normal);
    float3 T = normalize(i.tangent);
    float3 B = normalize(cross(N, T));

    // Transform tangent-space normal to world space
    float3 worldN = normalize(
        n_tangent.x * T +
        n_tangent.y * B +
        n_tangent.z * N
    );

    return worldN;
}


#endif // CG_UTILS_INCLUDED
