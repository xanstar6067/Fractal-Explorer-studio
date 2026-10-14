cbuffer Fft : register(b0) { uint N; uint LogN; uint Axis; uint Inverse; };
StructuredBuffer<float4> Input : register(t0);
RWStructuredBuffer<float4> Output : register(u0);
groupshared float4 Line[128];
uint Address(uint lane, uint2 coords)
{
    uint3 p = Axis == 0 ? uint3(lane,coords.x,coords.y) :
        Axis == 1 ? uint3(coords.x,lane,coords.y) : uint3(coords.x,coords.y,lane);
    return (p.z * N + p.y) * N + p.x;
}
float2 Multiply(float2 a, float2 b) { return float2(a.x*b.x-a.y*b.y, a.x*b.y+a.y*b.x); }
[numthreads(128,1,1)]
void Transform(uint lane : SV_GroupIndex, uint3 group : SV_GroupID)
{
    if (lane < N) Line[reversebits(lane) >> (32 - LogN)] = Input[Address(lane,group.xy)];
    GroupMemoryBarrierWithGroupSync();
    for (uint span = 2; span <= N; span <<= 1)
    {
        if (lane < N/2)
        {
            uint halfSpan = span/2, j = lane % halfSpan;
            uint a = (lane/halfSpan)*span + j, b = a + halfSpan;
            float angle = (Inverse != 0 ? 1.0 : -1.0) * 6.283185307179586 * j/span;
            float sn, cs; sincos(angle,sn,cs); float2 w = float2(cs,sn);
            float4 u = Line[a], v = Line[b];
            float4 t = float4(Multiply(v.xy,w),Multiply(v.zw,w));
            Line[a] = u+t; Line[b] = u-t;
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (lane < N) Output[Address(lane,group.xy)] = Line[lane] * (Inverse != 0 ? 1.0/N : 1.0);
}
// Four contiguous rows per group. Each pass cycles XYZ -> YZX, so the next
// pass also reads contiguous X rows. Three passes restore the original layout.
// Reassign lanes on write: each consecutive four threads write adjacent cells.
groupshared float4 Tile[512];
[numthreads(128,4,1)]
void TransformTiled(uint3 thread : SV_GroupThreadID, uint index : SV_GroupIndex, uint3 group : SV_GroupID)
{
    uint lane = thread.x, row = thread.y, baseRow = row*N;
    if (lane < N) Tile[baseRow+(reversebits(lane) >> (32-LogN))] = Input[(group.y*N+group.x*4+row)*N+lane];
    GroupMemoryBarrierWithGroupSync();
    for (uint span = 2; span <= N; span <<= 1)
    {
        if (lane < N/2)
        {
            uint halfSpan = span/2, j = lane % halfSpan;
            uint a = baseRow+(lane/halfSpan)*span+j, b = a+halfSpan;
            float angle = (Inverse != 0 ? 1.0 : -1.0)*6.283185307179586*j/span;
            float sn,cs; sincos(angle,sn,cs); float2 w = float2(cs,sn);
            float4 u = Tile[a], v = Tile[b], t = float4(Multiply(v.xy,w),Multiply(v.zw,w));
            Tile[a] = u+t; Tile[b] = u-t;
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (index < N*4)
    {
        uint outLane = index/4, outRow = index%4;
        Output[(outLane*N+group.y)*N+group.x*4+outRow] = Tile[outRow*N+outLane]*(Inverse != 0 ? 1.0/N : 1.0);
    }
}
