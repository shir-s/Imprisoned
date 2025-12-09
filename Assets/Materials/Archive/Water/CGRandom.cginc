#ifndef CG_RANDOM_INCLUDED
// Upgrade NOTE: excluded shader from DX11 because it uses wrong array syntax (type[size] name)
#pragma exclude_renderers d3d11
#define CG_RANDOM_INCLUDED

// Returns a psuedo-random float between -1 and 1 for a given float c
float random(float c)
{
    return -1.0 + 2.0 * frac(43758.5453123 * sin(c));
}

// Returns a psuedo-random float2 with componenets between -1 and 1 for a given float2 c 
float2 random2(float2 c)
{
    c = float2(dot(c, float2(127.1, 311.7)), dot(c, float2(269.5, 183.3)));

    float2 v = -1.0 + 2.0 * frac(43758.5453123 * sin(c));
    return v;
}

// Returns a psuedo-random float3 with componenets between -1 and 1 for a given float3 c 
float3 random3(float3 c)
{
    float j = 4096.0 * sin(dot(c, float3(17.0, 59.4, 15.0)));
    float3 r;
    r.z = frac(512.0*j);
    j *= .125;
    r.x = frac(512.0*j);
    j *= .125;
    r.y = frac(512.0*j);
    r = -1.0 + 2.0 * r;
    return r.yzx;
}

// Interpolates a given array v of 4 float values using bicubic interpolation
// at the given ratio t (a float2 with components between 0 and 1)
//
// [0]=====o==[1]
//         |
//         t
//         |
// [2]=====o==[3]
//
float bicubicInterpolation(float v[4], float2 t)
{
    float2 u = t * t * (3.0 - 2.0 * t); // Cubic interpolation

    // Interpolate in the x direction
    float x1 = lerp(v[0], v[1], u.x);
    float x2 = lerp(v[2], v[3], u.x);

    // Interpolate in the y direction and return
    return lerp(x1, x2, u.y);
}

// Interpolates a given array v of 4 float values using biquintic interpolation
// at the given ratio t (a float2 with components between 0 and 1)
float biquinticInterpolation(float v[4], float2 t)
{
    // Quintic Hermite smoothing: 6t^5 - 15t^4 + 10t^3
    float2 u = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    // Interpolate in the x direction
    float x1 = lerp(v[0], v[1], u.x);
    float x2 = lerp(v[2], v[3], u.x);

    // Interpolate in the y direction and return
    return lerp(x1, x2, u.y);
}

// Interpolates a given array v of 8 float values using triquintic interpolation
// at the given ratio t (a float3 with components between 0 and 1)
float triquinticInterpolation(float v[8], float3 t)
{
    // Quintic smoothstep per component
    float3 u = t * t * t * (t * (t * 6.0 - 15.0) + 10.0);

    // Interpolate along X for each Y/Z slice
    float x00 = lerp(v[0], v[1], u.x); // z=0, y=0
    float x10 = lerp(v[2], v[3], u.x); // z=0, y=1
    float x01 = lerp(v[4], v[5], u.x); // z=1, y=0
    float x11 = lerp(v[6], v[7], u.x); // z=1, y=1

    // Interpolate along Y on each Z slice
    float y0 = lerp(x00, x10, u.y);    // z=0
    float y1 = lerp(x01, x11, u.y);    // z=1

    // Interpolate along Z
    return lerp(y0, y1, u.z);
}

// Returns the value of a 2D value noise function at the given coordinates c
float value2d(float2 c)
{
    // Integer grid cell containing c
    float2 cell = floor(c);

    // Fractional part inside the cell, in [0,1]^2
    float2 f = frac(c);

    // Sample random2 at the 4 corners of the cell
    float v[4];
    v[0] = random2(cell + float2(0.0, 0.0)).x; // bottom-left
    v[1] = random2(cell + float2(1.0, 0.0)).x; // bottom-right
    v[2] = random2(cell + float2(0.0, 1.0)).x; // top-left
    v[3] = random2(cell + float2(1.0, 1.0)).x; // top-right

    // Bicubic interpolation of the 4 corner values
    return bicubicInterpolation(v, f);
}

// Returns the value of a 2D Perlin noise function at the given coordinates c
float perlin2d(float2 c)
{
    // Grid cell
    float2 cell = floor(c);

    // Fraction inside the cell
    float2 f = frac(c);

    // 4 Corners
    float2 c00 = cell + float2(0.0, 0.0);
    float2 c10 = cell + float2(1.0, 0.0);
    float2 c01 = cell + float2(0.0, 1.0);
    float2 c11 = cell + float2(1.0, 1.0);

    // Offsets from each corner to c
    float2 d00 = c - c00;
    float2 d10 = c - c10;
    float2 d01 = c - c01;
    float2 d11 = c - c11;

    // Gradients from random2()
    float2 g00 = random2(c00);
    float2 g10 = random2(c10);
    float2 g01 = random2(c01);
    float2 g11 = random2(c11);

    // Dot products gradient · offset
    float v[4];
    v[0] = dot(g00, d00);   // bottom-left
    v[1] = dot(g10, d10);   // bottom-right
    v[2] = dot(g01, d01);   // top-left
    v[3] = dot(g11, d11);   // top-right

    // Now use biquintic interpolation for smoother Perlin noise
    return biquinticInterpolation(v, f);
}

// Returns the value of a 3D Perlin noise function at the given coordinates c
float perlin3d(float3 c)
{                    
    // Integer grid cell
    float3 cell = floor(c);

    // Fractional position inside the cell
    float3 f = frac(c);

    // Corner positions
    float3 c000 = cell + float3(0.0, 0.0, 0.0);
    float3 c100 = cell + float3(1.0, 0.0, 0.0);
    float3 c010 = cell + float3(0.0, 1.0, 0.0);
    float3 c110 = cell + float3(1.0, 1.0, 0.0);
    float3 c001 = cell + float3(0.0, 0.0, 1.0);
    float3 c101 = cell + float3(1.0, 0.0, 1.0);
    float3 c011 = cell + float3(0.0, 1.0, 1.0);
    float3 c111 = cell + float3(1.0, 1.0, 1.0);

    // Offsets from each corner to c
    float3 d000 = c - c000;
    float3 d100 = c - c100;
    float3 d010 = c - c010;
    float3 d110 = c - c110;
    float3 d001 = c - c001;
    float3 d101 = c - c101;
    float3 d011 = c - c011;
    float3 d111 = c - c111;

    // Gradient vectors from random3()
    float3 g000 = random3(c000);
    float3 g100 = random3(c100);
    float3 g010 = random3(c010);
    float3 g110 = random3(c110);
    float3 g001 = random3(c001);
    float3 g101 = random3(c101);
    float3 g011 = random3(c011);
    float3 g111 = random3(c111);

    // Dot products gradient · offset at each corner
    float v[8];
    v[0] = dot(g000, d000); // (0,0,0)
    v[1] = dot(g100, d100); // (1,0,0)
    v[2] = dot(g010, d010); // (0,1,0)
    v[3] = dot(g110, d110); // (1,1,0)
    v[4] = dot(g001, d001); // (0,0,1)
    v[5] = dot(g101, d101); // (1,0,1)
    v[6] = dot(g011, d011); // (0,1,1)
    v[7] = dot(g111, d111); // (1,1,1)

    // Smoothly interpolate the 8 corner values
    return triquinticInterpolation(v, f);
}


#endif // CG_RANDOM_INCLUDED
