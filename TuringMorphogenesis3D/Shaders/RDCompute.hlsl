RWStructuredBuffer<float2> GridSrc : register(u0);
RWStructuredBuffer<float2> GridDst : register(u1);
RWTexture2D<float4>        OutTex  : register(u2);

cbuffer Params : register(b0)
{
    int2   GridSize;
    float  FeedRate;
    float  KillRate;
    float  DiffusionU;
    float  DiffusionV;
    float  DeltaTime;
    float  GradientMode;   // 0 none, 1 linear, 2 radial
    float  RangeLo;
    float  RangeHi;
    float  SchemeF;        // 0..4
    float  Padding2;
};

// ─────────── Сим шаг ───────────
[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)GridSize.x || id.y >= (uint)GridSize.y)
        return;

    int w = GridSize.x, h = GridSize.y;
    int idx = id.y * w + id.x;

    int xl = (id.x == 0)         ? w - 1 : (int)id.x - 1;
    int xr = (id.x == (uint)w-1) ? 0     : (int)id.x + 1;
    int yu = (id.y == 0)         ? h - 1 : (int)id.y - 1;
    int yd = (id.y == (uint)h-1) ? 0     : (int)id.y + 1;

    float2 c  = GridSrc[idx];
    float2 cl = GridSrc[id.y * w + xl];
    float2 cr = GridSrc[id.y * w + xr];
    float2 cu = GridSrc[yu * w + id.x];
    float2 cd = GridSrc[yd * w + id.x];

    float2 lap = cl + cr + cu + cd - 4.0 * c;

    float u = c.x, v = c.y;
    float uvv = u * v * v;

    float f = FeedRate;
    if (GradientMode > 0.5)
    {
        float t;
        if (GradientMode < 1.5)
            t = 0.5 - 0.5 * cos(6.28318530718 * (float)id.y / (float)h);
        else
        {
            float2 ctr = float2(w * 0.5, h * 0.5);
            float2 d = (float2(id.x, id.y) - ctr) / ctr;
            t = length(d) / 1.41421356;
        }
        f = FeedRate * (0.7 + 0.6 * clamp(t, 0.0, 1.0));
    }

    float du = DiffusionU * lap.x - uvv + f * (1.0 - u);
    float dv = DiffusionV * lap.y + uvv - (f + KillRate) * v;

    GridDst[idx] = float2(clamp(u + DeltaTime * du, 0, 1),
                          clamp(v + DeltaTime * dv, 0, 1));
}

// ─────────── Колоризация на GPU ───────────
float3 SchemeColor(float t, int s)
{
    float3 c0, c1, c2, c3, c4;
    if (s == 0)      { c0=float3(0.012,0.031,0.125); c1=float3(0.031,0.196,0.471); c2=float3(0.000,0.588,0.804); c3=float3(0.431,0.882,0.941); c4=float3(0.949,0.992,1.000); }
    else if (s == 1) { c0=float3(0.031,0.000,0.000); c1=float3(0.392,0.031,0.000); c2=float3(0.843,0.216,0.000); c3=float3(1.000,0.667,0.078); c4=float3(1.000,0.980,0.804); }
    else if (s == 2) { c0=float3(0.012,0.063,0.020); c1=float3(0.031,0.275,0.110); c2=float3(0.137,0.588,0.216); c3=float3(0.588,0.863,0.353); c4=float3(0.933,1.000,0.765); }
    else if (s == 3) { c0=float3(0.024,0.024,0.024); c1=float3(0.255,0.255,0.255); c2=float3(0.529,0.529,0.529); c3=float3(0.804,0.804,0.804); c4=float3(0.988,0.988,0.988); }
    else             { c0=float3(0.024,0.000,0.094); c1=float3(0.235,0.059,0.745); c2=float3(1.000,0.000,0.686); c3=float3(1.000,0.431,0.471); c4=float3(0.000,1.000,0.961); }

    float pos = t * 4.0;
    int i = (int)min(pos, 3.0);
    float fr = pos - i;
    float3 a = (i == 0) ? c0 : (i == 1) ? c1 : (i == 2) ? c2 : c3;
    float3 b = (i == 0) ? c1 : (i == 1) ? c2 : (i == 2) ? c3 : c4;
    return lerp(a, b, fr);
}

[numthreads(16, 16, 1)]
void CSColorize(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)GridSize.x || id.y >= (uint)GridSize.y)
        return;

    float v = GridSrc[id.y * GridSize.x + id.x].y;
    float t = saturate((v - RangeLo) / max(RangeHi - RangeLo, 0.08));
    t = t * t * (3 - 2 * t);
    t = t * t * (3 - 2 * t);

    OutTex[id.xy] = float4(SchemeColor(t, (int)SchemeF), 1.0);
}