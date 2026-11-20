float StandingWaveAt(float3 position, out float3 gradient)
{
    float u = 0;
    gradient = 0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        float4 mode = WaveModes[i];
        if (mode.w != 0)
        {
            float3 k = 3.14159265359 * mode.xyz;
            float3 s, c;
            sincos(k * position, s, c);
            u += mode.w * s.x * s.y * s.z;
            gradient += mode.w * k * c * s.yzx * s.zxy;
        }
    }
    return u;
}
