using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Core.Rendering;

namespace FractalExplorerWPF.Core.Rendering3D;

internal static class TuringComputeShader
{
    public static readonly string[] EntryPoints = ["Blur", "Blur256", "Blur512", "Blur1024", "Difference", "Choose", "Compose", "Symmetry", "Reduce", "Normalize", "Paint", "Render", "ReactionStep", "ReactionSymmetry"];
    public static IEnumerable<ShaderCacheEntry> CacheEntries => EntryPoints.Select(name =>
        new ShaderCacheEntry("turing-" + name, BlurSource(name), name, "cs_5_0"));

    private static string BlurSource(string name)
    {
        if (!name.StartsWith("Blur", StringComparison.Ordinal) || name == "Blur") return Source;
        int capacity = int.Parse(name.AsSpan(4));
        return Source.Replace("Prefix[4096]", $"Prefix[{capacity * 2}]")
            .Replace("ScanCapacity = 2048", $"ScanCapacity = {capacity}")
            .Replace("2048 - origin", $"{capacity} - origin")
            .Replace("void Blur(", $"void {name}(");
    }

    // Float4 buffers share a pool. Only the committed field owns value and scale together.
    // Prefix sums perform every blur in O(N²), independent of the selected radius.
    public const string Source = """
        cbuffer Parameters : register(b0) { float4 P[12]; }
        StructuredBuffer<float4> A : register(t0);
        StructuredBuffer<float4> B : register(t1);
        StructuredBuffer<float4> C : register(t2);
        StructuredBuffer<float4> Palette : register(t3);
        RWStructuredBuffer<float4> Out : register(u0);
        RWStructuredBuffer<uint> Pixels : register(u1);
        // Ping-pong scans keep the same addition order without a private array
        // of eight values per thread or two barriers for every scan stage.
        groupshared float Prefix[4096];
        static const uint ScanCapacity = 2048;
        groupshared float2 Ranges[256];
        """ + TuringReactionKinetics.Shader + """
        uint Size() { return (uint)P[0].x; }
        int Edge(int p) {
            int n = (int)Size();
            if ((uint)p < (uint)n) return p;
            // Rotated grid points and relief samples remain in [-n,2n).
            // One fold suffices; no dynamic integer remainder is needed.
            if (P[0].z == 0) return p < 0 ? p + n : p - n;
            return p < 0 ? -1 - p : 2 * n - 1 - p;
        }
        float Sample(float2 p) {
            int2 q = (int2)floor(p); float2 t = p - q; uint n = Size();
            float v0 = lerp(A[Edge(q.y) * n + Edge(q.x)].x, A[Edge(q.y) * n + Edge(q.x + 1)].x, t.x);
            float v1 = lerp(A[Edge(q.y + 1) * n + Edge(q.x)].x, A[Edge(q.y + 1) * n + Edge(q.x + 1)].x, t.x);
            return lerp(v0, v1, t.y);
        }
        float Integral(int index, uint origin) {
            int n = (int)Size(), period = P[0].z == 0 ? n : n * 2;
            int cycles = (int)floor((float)index / period), r = index - cycles * period;
            float total = Prefix[origin + n - 1];
            float part = r == 0 ? 0 : r <= n ? Prefix[origin + r - 1] : total * 2 - Prefix[origin + 2 * n - r - 1];
            return cycles * total * (P[0].z == 0 ? 1 : 2) + part;
        }
        [numthreads(256,1,1)]
        void Blur(uint3 group : SV_GroupID, uint tid : SV_GroupIndex) {
            uint n = Size(), row = group.x; bool vertical = P[1].y != 0;
            for (uint x = tid; x < n; x += 256) Prefix[x] = A[vertical ? x * n + row : row * n + x].x;
            GroupMemoryBarrierWithGroupSync();
            uint origin = 0;
            [unroll] for (uint offset = 1; offset < ScanCapacity; offset *= 2) {
                if (offset >= n) break;
                uint target = 2048 - origin;
                [unroll] for (uint k = 0; k < ScanCapacity; k += 256) {
                    uint x = tid + k;
                    if (x < n) Prefix[target + x] = Prefix[origin + x] + (x >= offset ? Prefix[origin + x - offset] : 0);
                }
                GroupMemoryBarrierWithGroupSync();
                origin = target;
            }
            int radius = (int)P[0].y;
            for (uint x = tid; x < n; x += 256) {
                float mean = (Integral((int)x + radius + 1, origin) - Integral((int)x - radius, origin)) / (radius * 2 + 1);
                Out[vertical ? x * n + row : row * n + x] = float4(mean,0,0,0);
            }
        }
        [numthreads(256,1,1)]
        void Difference(uint3 id : SV_DispatchThreadID) {
            if (id.x < Size() * Size()) Out[id.x] = float4(abs(A[id.x].x - B[id.x].x),0,0,0);
        }
        [numthreads(256,1,1)]
        void Choose(uint3 id : SV_DispatchThreadID) {
            if (id.x >= Size() * Size()) return;
            float4 best = Out[id.x]; float variation = A[id.x].x;
            if (P[1].x != 0 || variation < best.x) Out[id.x] = float4(variation, B[id.x].x > C[id.x].x ? P[2].x : -P[2].x, P[1].z,0);
        }
        [numthreads(256,1,1)]
        void Compose(uint3 id : SV_DispatchThreadID) {
            if (id.x < Size() * Size()) Out[id.x] = float4(A[id.x].x + B[id.x].y, B[id.x].z,0,0);
        }
        [numthreads(16,16,1)]
        void Symmetry(uint3 id : SV_DispatchThreadID) {
            uint n = Size(); if (id.x >= n || id.y >= n) return;
            uint i = id.y * n + id.x, arms = (uint)P[1].z; bool mirror = P[1].w != 0;
            float center = (n - 1) * .5; float2 d = (float2)id.xy - center; float value = 0;
            for (uint arm = 0; arm < arms; arm++) {
                float s,c; sincos(arm * 6.28318530718 / arms,s,c);
                value += Sample(center + float2(d.x*c-d.y*s,d.x*s+d.y*c));
                if (mirror) value += Sample(center + float2(d.x*c+d.y*s,d.x*s-d.y*c));
            }
            Out[i] = float4(value / (arms * (mirror ? 2 : 1)),A[i].y,0,0);
        }
        [numthreads(256,1,1)]
        void Reduce(uint3 group : SV_GroupID, uint tid : SV_GroupIndex) {
            uint i = group.x * 256 + tid;
            float2 range = float2(3.402823e38,-3.402823e38);
            if (i < (uint)P[0].w) range = P[1].x == 0 ? A[i].xx : A[i].xy;
            Ranges[tid] = range; GroupMemoryBarrierWithGroupSync();
            for (uint offset = 128; offset > 0; offset /= 2) {
                if (tid < offset) Ranges[tid] = float2(min(Ranges[tid].x,Ranges[tid+offset].x),max(Ranges[tid].y,Ranges[tid+offset].y));
                GroupMemoryBarrierWithGroupSync();
            }
            if (tid == 0) Out[group.x] = float4(Ranges[0],0,0);
        }
        [numthreads(256,1,1)]
        void Normalize(uint3 id : SV_DispatchThreadID) {
            if (id.x >= Size() * Size()) return;
            float4 value = Out[id.x]; float2 range = A[0].xy;
            value.x = range.y - range.x < 1e-12 ? 0 : clamp((value.x-range.x)/(range.y-range.x)*2-1,-1,1);
            Out[id.x] = value;
        }
        [numthreads(16,16,1)]
        void Paint(uint3 id : SV_DispatchThreadID) {
            uint n = Size(); if (id.x >= n || id.y >= n) return;
            uint i = id.y * n + id.x; float4 value = Out[i]; float2 d = P[3].xy - .5;
            uint hash = (i * 374761393u + asuint(P[3].z)) ^ asuint(P[3].w); hash = (hash ^ (hash >> 13)) * 1274126177u;
            float target = P[2].w == 1 ? -1 : P[2].w == 2 ? (hash >> 8) / 16777215.0 * 2 - 1 : 1;
            if(P[8].x!=0) target=(target+1)*P[9].z;
            for (uint arm = 0; arm < (uint)P[1].z; arm++) {
                float s,c; sincos(arm * 6.28318530718 / P[1].z,s,c);
                for (uint reflection = 0; reflection < (P[1].w != 0 ? 2u : 1u); reflection++) {
                    float yy = reflection == 0 ? d.y : -d.y;
                    float2 center = (.5 + float2(d.x*c-yy*s,d.x*s+yy*c)) * (n-1);
                    float weight = saturate(1-distance((float2)id.xy,center)/(P[2].z*n));
                    if(P[8].x!=0) value.z=lerp(value.z,target,P[2].y*weight);
                    else value.x = lerp(value.x,target,P[2].y*weight);
                }
            }
            if(P[8].x!=0) value.x=ReactionDisplayValue(value.z);
            Out[i] = value;
        }
        [numthreads(16,16,1)]
        void ReactionStep(uint3 id : SV_DispatchThreadID) {
            uint n=Size(); if (id.x>=n || id.y>=n) return;
            uint i=id.y*n+id.x; float2 uv=A[i].zw;
            float2 lap=A[id.y*n+Edge((int)id.x-1)].zw + A[id.y*n+Edge((int)id.x+1)].zw
                + A[Edge((int)id.y-1)*n+id.x].zw + A[Edge((int)id.y+1)*n+id.x].zw - 4*uv;
            uv=max(float2(1e-6,1e-6),uv+P[8].w*(P[9].xy*lap+Reaction(uv)));
            Out[i]=float4(ReactionDisplayValue(uv.x),0,uv);
        }
        float2 SampleReaction(float2 p) {
            int2 q=(int2)floor(p); float2 t=p-q; uint n=Size();
            return lerp(lerp(A[Edge(q.y)*n+Edge(q.x)].zw,A[Edge(q.y)*n+Edge(q.x+1)].zw,t.x),
                lerp(A[Edge(q.y+1)*n+Edge(q.x)].zw,A[Edge(q.y+1)*n+Edge(q.x+1)].zw,t.x),t.y);
        }
        [numthreads(16,16,1)]
        void ReactionSymmetry(uint3 id : SV_DispatchThreadID) {
            uint n=Size(); if (id.x>=n || id.y>=n) return;
            float center=(n-1)*.5; float2 d=(float2)id.xy-center,uv=0;
            uint arms=(uint)P[1].z;
            for(uint arm=0;arm<arms;arm++) {
                float s,c; sincos(arm*6.28318530718/arms,s,c);
                uv+=SampleReaction(center+float2(d.x*c-d.y*s,d.x*s+d.y*c));
                if(P[1].w!=0) uv+=SampleReaction(center+float2(d.x*c+d.y*s,d.x*s-d.y*c));
            }
            uv/=arms*(P[1].w!=0?2:1);
            Out[id.y*n+id.x]=float4(ReactionDisplayValue(uv.x),0,uv);
        }
        [numthreads(16,16,1)]
        void Render(uint3 id : SV_DispatchThreadID) {
            uint width = (uint)P[4].x, height = (uint)P[4].y; if (id.x >= width || id.y >= height) return;
            float logicalWidth = P[6].z > 0 ? height*P[6].z : width;
            float side = min(logicalWidth,height)*P[5].z;
            float2 left = (float2(logicalWidth,height)-side)*.5-P[6].xy*side;
            float2 uv = (float2((id.x+.5)*logicalWidth/width,id.y+.5)-left)/side;
            float3 color = float3(10,15,22);
            if (all(uv >= 0) && all(uv <= 1) && (P[1].z == 1 || dot(uv-.5,uv-.5) <= .25)) {
                float2 p = uv*(Size()-1); float t = saturate(Sample(p)*.5*P[5].x+.5); if (P[4].w != 0) t = 1-t;
                uint scale = (uint)A[(uint)round(p.y)*Size()+(uint)round(p.x)].y;
                static const float3 colors[8] = { float3(72,208,199),float3(68,137,229),float3(170,110,220),float3(232,149,84),float3(231,208,103),float3(220,93,129),float3(135,199,93),float3(219,218,227) };
                color = P[4].z == 2 ? colors[scale%8] : Palette[(uint)round(t*1023)].rgb;
                if (P[4].z == 1) {
                    float dx = (Sample(p+float2(1,0))-Sample(p-float2(1,0)))*P[5].y*Size()/32;
                    float dy = (Sample(p+float2(0,1))-Sample(p-float2(0,1)))*P[5].y*Size()/32;
                    color *= clamp(.55+.55*(.5*dx+.6*dy+.62)/sqrt(dx*dx+dy*dy+1),.25,1.1);
                }
            }
            uint3 rgb = (uint3)clamp(color,0,255);
            Pixels[id.y*width+id.x] = rgb.b | (rgb.g<<8) | (rgb.r<<16) | 0xff000000u;
        }
        """;
}
