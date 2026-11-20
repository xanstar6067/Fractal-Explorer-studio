cbuffer Field : register(b0)
{
    float4 Grid; // x: side length
    float4 WaveModes[4]; // xyz: mode indices, w: signed coefficient at the current time
};

RWTexture3D<float2> Wave : register(u0);

#include "StandingWavesField.hlsl"

[numthreads(8, 8, 4)]
void Compute(uint3 id : SV_DispatchThreadID)
{
    float3 position = (float3(id) + .5) / Grid.x;
    float3 gradient;
    float u = StandingWaveAt(position, gradient);
    Wave[id] = float2(u, length(gradient));
}
