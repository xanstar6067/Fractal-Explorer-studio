float3 FlameColor(float3 p)
{
    float4 v = Density.SampleLevel(DensitySampler, saturate(p * 0.5 + 0.5), 0);
    return v.rgb / max(v.a, 1e-6);
}

float3 FlameTone(float3 radiance)
{
    // Compress the peak channel to preserve hue without clipping the bright cores.
    float peak = max(radiance.r, max(radiance.g, radiance.b));
    float mapped = 1.0 - exp(-peak * ShapeA.x);
    float3 color = radiance * (mapped / max(peak, 1e-6));
    color = pow(max(color, 0), 2.2 / max(ShapeA.y, 0.5));
    float gray = dot(color, float3(0.2126, 0.7152, 0.0722));
    return max(0, lerp(gray.xxx, color, ShapeC.w));
}
