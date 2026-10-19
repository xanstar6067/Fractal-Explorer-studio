// A real three-coordinate section of the complex (C1, C2) parameter space.
// This is not the unrelated spatial Phoenix3D recurrence.
cbuffer Settings : register(b0)
{
    float4 Grid;       // resolution, first Z layer, C1/C2 section, variant
    float4 Formula;    // primary power, secondary power, iterations, bailout squared
    float4 Parameters; // Re C1, Im C1, Re C2, Im C2
    float4 Initial;    // z0, z(-1)
    float4 Eye;        // camera position, field of view scale
    float4 Forward;
    float4 Right;
    float4 Up;
    float4 Screen;     // width, height, first row, rows
    float4 Marker;     // selected parameter triple, sphere radius
    float4 Lighting;   // shadows enabled
};
RWTexture3D<float> FieldOut : register(u0);
Texture3D<float> Field : register(t0);
SamplerState LinearClamp : register(s0);
RWStructuredBuffer<uint2> Pixels : register(u1); // BGRA and surface distance, excluding marker

float2 Multiply(float2 a, float2 b) { return float2(a.x*b.x-a.y*b.y, a.x*b.y+a.y*b.x); }
float2 Power(float2 z, int exponent)
{
    int variant = (int)Grid.w;
    if (variant == 1) z.y = -z.y;
    if (variant == 2) z = float2(abs(z.x), -abs(z.y));
    if (variant == 4) z = abs(z);
    float2 result = float2(1, 0);
    [loop] while (exponent > 0)
    {
        if (exponent & 1) result = Multiply(result, z);
        exponent >>= 1;
        if (exponent > 0) z = Multiply(z, z);
    }
    if (variant == 3) result.x = abs(result.x);
    return result;
}
float Bounded(float3 p)
{
    float2 c1 = Grid.z == 0 ? p.xy : float2(p.z, Parameters.y);
    float2 c2 = Grid.z == 0 ? float2(p.z, Parameters.w) : p.xy;
    float2 z = Initial.xy, previous = Initial.zw;
    [loop] for (int i = 0; i < (int)Formula.z; i++)
    {
        if (!all(isfinite(z)) || dot(z,z) > Formula.w) return 0;
        float2 next = Power(z, (int)Formula.x) + Multiply(c1, Power(z, (int)Formula.y)) + Multiply(c2, previous);
        previous = z; z = next;
    }
    // Match the CPU's finite-iteration classification: reaching the limit is interior.
    return 1;
}
[numthreads(8,8,1)]
void Build(uint3 id : SV_DispatchThreadID)
{
    id.z += (uint)Grid.y;
    if (any(id >= (uint)Grid.x)) return;
    float3 p = -2 + 4 * (float3)id / (Grid.x - 1);
    FieldOut[id] = Bounded(p);
}
float Density(float3 p)
{
    if (any(abs(p) > 2)) return 0;
    float3 uv = ((p + 2) * 0.25 * (Grid.x - 1) + 0.5) / Grid.x;
    return Field.SampleLevel(LinearClamp, uv, 0);
}
float3 Normal(float3 p, float3 fallback)
{
    float e = 4 / (Grid.x - 1);
    float3 n = float3(Density(p-float3(e,0,0))-Density(p+float3(e,0,0)),
        Density(p-float3(0,e,0))-Density(p+float3(0,e,0)),
        Density(p-float3(0,0,e))-Density(p+float3(0,0,e)));
    return dot(n,n) > 1e-8 ? normalize(n) : fallback;
}
bool Box(float3 origin, float3 ray, out float start, out float end)
{
    float3 safeRay = float3(abs(ray.x)<1e-8 ? 1e-8 : ray.x,
        abs(ray.y)<1e-8 ? 1e-8 : ray.y, abs(ray.z)<1e-8 ? 1e-8 : ray.z);
    float3 a = (-2-origin)/safeRay, b = (2-origin)/safeRay;
    float3 lo = min(a,b), hi = max(a,b);
    start = max(0, max(lo.x,max(lo.y,lo.z)));
    end = min(hi.x,min(hi.y,hi.z));
    return end >= start;
}
float Trace(float3 origin, float3 ray)
{
    float start, end;
    if (!Box(origin, ray, start, end)) return -1;
    float stepSize = 2 / (Grid.x - 1);
    float before = start;
    [loop] for (int i=0; i<768; i++)
    {
        float t = start + i * stepSize;
        if (t > end) break;
        if (Density(origin+ray*t) >= 0.5)
        {
            float after = t;
            [unroll] for (int j=0; j<7; j++)
            {
                float middle = (before+after)*0.5;
                if (Density(origin+ray*middle)>=0.5) after=middle; else before=middle;
            }
            return after;
        }
        before = t;
    }
    return -1;
}
[numthreads(8,8,1)]
void Render(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)Screen.x || id.y >= (uint)Screen.w) return;
    uint y = id.y + (uint)Screen.z;
    float2 plane = (float2(id.x+0.5,y+0.5)-Screen.xy*0.5)/(Screen.y*0.5);
    float3 ray = normalize(Forward.xyz*Eye.w + Right.xyz*plane.x - Up.xyz*plane.y);
    float distance = Trace(Eye.xyz,ray);
    float3 color = float3(0.045,0.055,0.075);
    float3 light = normalize(-Forward.xyz+Right.xyz*0.6+Up.xyz*0.9);
    if (distance >= 0)
    {
        float3 p = Eye.xyz+ray*distance;
        float3 normal = Normal(p,-ray);
        float diffuse = max(0,dot(normal,light));
        float shadow = 1;
        if (Lighting.x > 0 && diffuse > 0 && Trace(p+normal*(8/(Grid.x-1)),light)>=0) shadow=0.35;
        float specular = pow(max(0,dot(normal,normalize(light-ray))),32)*0.18;
        color = float3(0.82,0.76,0.63)*(0.24+0.76*diffuse*shadow)+specular;
    }
    // The sphere participates in depth compositing, never in the surface picking buffer.
    float3 offset = Eye.xyz-Marker.xyz;
    float b=dot(offset,ray), c=dot(offset,offset)-Marker.w*Marker.w;
    float discriminant=b*b-c;
    if (discriminant>=0)
    {
        float hit=-b-sqrt(discriminant);
        if (hit>0 && (distance<0 || hit<distance))
        {
            float3 n=normalize(Eye.xyz+ray*hit-Marker.xyz);
            color=float3(0.08,0.85,0.18)*(0.3+0.7*max(0,dot(n,light)))+
                pow(max(0,dot(n,normalize(light-ray))),32)*0.5;
        }
    }
    uint3 rgb=(uint3)round(saturate(color)*255);
    Pixels[id.y*(uint)Screen.x+id.x]=uint2(rgb.z|(rgb.y<<8)|(rgb.x<<16)|0xff000000,asuint(distance));
}
