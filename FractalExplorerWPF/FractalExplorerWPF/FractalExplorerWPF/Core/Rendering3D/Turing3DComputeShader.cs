using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Core.Rendering;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Шаг многомасштабной модели Маккейба в кубе: для каждого масштаба — сглаженные активатор и
/// ингибитор, их сглаженное различие, выбор масштаба с наименьшей вариацией; затем симметрия и
/// нормировка поля в [-1, 1]. Сглаживание — три прохода квадратного окна по каждой оси (как в 2D),
/// скользящей суммой: стоимость не зависит от радиуса.
/// </summary>
internal static class Turing3DComputeShader
{
    public const uint CellGroup = 256, VolumeX = 8, VolumeY = 8, VolumeZ = 4;

    public static readonly string[] EntryPoints = ["Blur", "Difference", "Choose", "Compose", "Symmetry", "Reduce", "Normalize", "Paint", "Publish", "ReactionStep", "ReactionDisplay"];

    public static IReadOnlyList<ShaderCacheEntry> CacheEntries { get; } = EntryPoints
        .Select(entry => new ShaderCacheEntry("turing3d-" + entry, Source, entry, "cs_5_0")).ToArray();

    public const string Source = """
        // P[0]: edge n, unused, edges (0 closed, 1 mirrored), blur axis
        // P[1]: first-scale flag, scale number, signed response, group size
        // P[2]: brush centre (0..1 of the edge), brush radius (share of the edge)
        // P[3]: brush strength, brush kind, seed bits, step bits
        // P[4]: reduce count, reduce stage (0 field, 1 ranges)
        // P[5], P[6]: display value of the scale map for scales 0..7
        // P[7]: radii of the three successive box windows (0 skips a window)
        cbuffer Parameters : register(b0) { float4 P[12]; }
        StructuredBuffer<float> A : register(t0);
        StructuredBuffer<float> B : register(t1);
        StructuredBuffer<float> C : register(t2);
        StructuredBuffer<float2> D : register(t3);
        StructuredBuffer<float4> Group : register(t4);
        RWStructuredBuffer<float> OutA : register(u0);
        RWStructuredBuffer<float> OutB : register(u1);
        RWStructuredBuffer<float2> OutD : register(u2);
        RWTexture3D<float2> Display : register(u3);
        groupshared float2 Ranges[256];
        // Lines are at most 160 cells (Turing3DField.MaxSize); the scan ping-pongs between halves.
        groupshared float Line[256];
        groupshared float Prefix[512];

        """ + TuringReactionKinetics.Shader + """
        uint Edge() { return (uint)P[0].x; }
        uint Count() { uint n = Edge(); return n * n * n; }
        uint Index(uint3 p) { uint n = Edge(); return (p.z * n + p.y) * n + p.x; }
        // Positions far outside the cube occur for wide windows and rotated samples.
        int Fold(int p, int n)
        {
            if ((uint)p < (uint)n) return p;
            if (P[0].z == 0) { int r = p % n; return r < 0 ? r + n : r; }
            int period = 2 * n; int q = p % period; if (q < 0) q += period;
            return q < n ? q : period - 1 - q;
        }

        // Sum of the first `index` values of the line continued past its ends: periodically for
        // closed edges, as a mirrored copy for mirrored ones. Prefix[k] holds the first k + 1 values.
        float Integral(int index, uint origin)
        {
            int n = (int)Edge(), period = P[0].z == 0 ? n : n * 2;
            int cycles = (int)floor((float)index / period), r = index - cycles * period;
            float total = Prefix[origin + n - 1];
            float part = r == 0 ? 0 : r <= n ? Prefix[origin + r - 1] : total * 2 - Prefix[origin + 2 * n - r - 1];
            return cycles * total * (P[0].z == 0 ? 1 : 2) + part;
        }

        // One group per line along P[0].w; the line stays in group memory while all three box
        // windows (P[7].xyz) run over it, so each axis reads and writes the volume once.
        // Neighbouring groups take neighbouring lines, and their reads share cache lines.
        [numthreads(64,1,1)]
        void Blur(uint3 group : SV_GroupID, uint tid : SV_GroupIndex)
        {
            uint n = Edge(), axis = (uint)P[0].w, start, stride;
            if (axis == 0) { start = (group.y * n + group.x) * n; stride = 1; }
            else if (axis == 1) { start = group.y * n * n + group.x; stride = n; }
            else { start = group.y * n + group.x; stride = n * n; }
            for (uint x = tid; x < n; x += 64) Line[x] = A[start + x * stride];
            GroupMemoryBarrierWithGroupSync();
            [unroll] for (uint box = 0; box < 3; box++)
            {
                int radius = (int)P[7][box];
                if (radius == 0) continue;
                for (uint x = tid; x < n; x += 64) Prefix[x] = Line[x];
                GroupMemoryBarrierWithGroupSync();
                // Ping-pong Hillis–Steele scan: one barrier per stage.
                uint origin = 0;
                for (uint offset = 1; offset < n; offset *= 2)
                {
                    uint target = 256 - origin;
                    for (uint x = tid; x < n; x += 64)
                        Prefix[target + x] = Prefix[origin + x] + (x >= offset ? Prefix[origin + x - offset] : 0);
                    GroupMemoryBarrierWithGroupSync();
                    origin = target;
                }
                for (uint x = tid; x < n; x += 64)
                    Line[x] = (Integral((int)x + radius + 1, origin) - Integral((int)x - radius, origin)) / (radius * 2 + 1);
                GroupMemoryBarrierWithGroupSync();
            }
            for (uint x = tid; x < n; x += 64) OutA[start + x * stride] = Line[x];
        }

        [numthreads(256,1,1)]
        void Difference(uint3 id : SV_DispatchThreadID)
        {
            if (id.x < Count()) OutA[id.x] = abs(A[id.x] - B[id.x]);
        }

        // A: smoothed variation, B: activator, C: inhibitor; the winner's scale goes to OutB.
        [numthreads(256,1,1)]
        void Choose(uint3 id : SV_DispatchThreadID)
        {
            uint i = id.x; if (i >= Count()) return;
            float variation = A[i];
            if (P[1].x != 0 || variation < OutD[i].x)
            {
                OutD[i] = float2(variation, B[i] > C[i] ? P[1].z : -P[1].z);
                OutB[i] = P[1].y;
            }
        }

        [numthreads(256,1,1)]
        void Compose(uint3 id : SV_DispatchThreadID)
        {
            uint i = id.x; if (i < Count()) OutA[i] = A[i] + D[i].y;
        }

        float SampleField(float3 q)
        {
            int n = (int)Edge(); int3 b = (int3)floor(q); float3 t = q - b;
            int x0 = Fold(b.x, n), x1 = Fold(b.x + 1, n), y0 = Fold(b.y, n), y1 = Fold(b.y + 1, n);
            int z0 = Fold(b.z, n), z1 = Fold(b.z + 1, n);
            float v00 = lerp(A[Index(uint3(x0,y0,z0))], A[Index(uint3(x1,y0,z0))], t.x);
            float v10 = lerp(A[Index(uint3(x0,y1,z0))], A[Index(uint3(x1,y1,z0))], t.x);
            float v01 = lerp(A[Index(uint3(x0,y0,z1))], A[Index(uint3(x1,y0,z1))], t.x);
            float v11 = lerp(A[Index(uint3(x0,y1,z1))], A[Index(uint3(x1,y1,z1))], t.x);
            return lerp(lerp(v00, v10, t.y), lerp(v01, v11, t.y), t.z);
        }

        // Every cell copies its representative: the image of the orbit that lies furthest along
        // the domain direction (Group[g].w row holds gᵀd). A cell of the fundamental domain is its
        // own representative and keeps its exact value; the field becomes exactly invariant
        // with one sample per cell instead of an average over up to 120 images.
        // In a ball or a shell (P[4].z = 1 or 2, shell thickness P[4].w) a cell beyond a sphere
        // copies its radial mirror image inside: the spheres become mirrored edges, as the
        // cube faces are, so the pattern meets them at right angles and pierces a thin shell.
        [numthreads(8,8,4)]
        void Symmetry(uint3 id : SV_DispatchThreadID)
        {
            uint n = Edge(); if (any(id >= n)) return;
            float c = (n - 1) * .5; float3 p = (float3)id - c;
            uint count = (uint)P[1].w, chosen = 0; float best = dot(p, Group[3].xyz);
            for (uint g = 1; g < count; g++)
            {
                float s = dot(p, Group[g * 4 + 3].xyz);
                if (s > best) { best = s; chosen = g; }
            }
            float3 q = float3(dot(Group[chosen * 4].xyz, p), dot(Group[chosen * 4 + 1].xyz, p), dot(Group[chosen * 4 + 2].xyz, p));
            if (P[4].z != 0)
            {
                float r = length(q), outer = 0.97 * n * .5, inner = P[4].z == 2 ? (0.97 - P[4].w) * n * .5 : 0;
                float folded = clamp(r > outer ? 2 * outer - r : r < inner ? 2 * inner - r : r, inner, outer);
                if (r > 1e-4) q *= folded / r;
            }
            q += c;
            uint i = Index(id);
            OutA[i] = SampleField(q);
            int3 nearest = int3(Fold((int)round(q.x), n), Fold((int)round(q.y), n), Fold((int)round(q.z), n));
            OutB[i] = B[Index((uint3)nearest)];
        }

        [numthreads(256,1,1)]
        void Reduce(uint3 group : SV_GroupID, uint tid : SV_GroupIndex)
        {
            uint i = group.x * 256 + tid;
            float2 range = float2(3.402823e38, -3.402823e38);
            if (i < (uint)P[4].x) range = P[4].y == 0 ? float2(A[i], A[i]) : D[i];
            Ranges[tid] = range; GroupMemoryBarrierWithGroupSync();
            for (uint offset = 128; offset > 0; offset /= 2)
            {
                if (tid < offset) Ranges[tid] = float2(min(Ranges[tid].x, Ranges[tid + offset].x), max(Ranges[tid].y, Ranges[tid + offset].y));
                GroupMemoryBarrierWithGroupSync();
            }
            if (tid == 0) OutD[group.x] = Ranges[0];
        }

        [numthreads(256,1,1)]
        void Normalize(uint3 id : SV_DispatchThreadID)
        {
            uint i = id.x; if (i >= Count()) return;
            float2 range = D[0]; float value = OutA[i];
            OutA[i] = range.y - range.x < 1e-12 ? 0 : clamp((value - range.x) / (range.y - range.x) * 2 - 1, -1, 1);
        }

        // A spherical stroke repeated over the symmetry group; noise is a hash of cell, seed and time.
        [numthreads(8,8,4)]
        void Paint(uint3 id : SV_DispatchThreadID)
        {
            uint n = Edge(); if (any(id >= n)) return;
            uint i = Index(id); float value = OutA[i];
            float c = (n - 1) * .5; float3 centre = (P[2].xyz - .5) * n; float radius = P[2].w * n;
            uint hash = (i * 374761393u + asuint(P[3].z)) ^ asuint(P[3].w); hash = (hash ^ (hash >> 13)) * 1274126177u;
            float target = P[3].y == 1 ? -1 : P[3].y == 2 ? (hash >> 8) / 16777215.0 * 2 - 1 : 1;
            if(P[8].x!=0) target=(target+1)*P[9].z;
            for (uint g = 0; g < (uint)P[1].w; g++)
            {
                float3 image = float3(dot(Group[g * 4].xyz, centre), dot(Group[g * 4 + 1].xyz, centre), dot(Group[g * 4 + 2].xyz, centre));
                float3 delta = abs((float3)id - c - image);
                if (P[0].z == 0) delta = min(delta, n - delta);
                value = lerp(value, target, P[3].x * saturate(1 - length(delta) / radius));
            }
            OutA[i] = value;
        }

        [numthreads(8,8,4)]
        void ReactionStep(uint3 id : SV_DispatchThreadID) {
            uint n=Edge(); if(any(id>=n)) return;
            uint i=Index(id); float2 uv=float2(A[i],B[i]),lap=0;
            for(uint axis=0;axis<3;axis++) {
                int3 lo=(int3)id,hi=(int3)id; lo[axis]=Fold(lo[axis]-1,n); hi[axis]=Fold(hi[axis]+1,n);
                uint l=Index((uint3)lo),h=Index((uint3)hi);
                lap+=float2(A[l]+A[h],B[l]+B[h])-2*uv;
            }
            uv=max(float2(1e-6,1e-6),uv+P[8].w*(P[9].xy*lap+Reaction(uv)));
            OutA[i]=uv.x; OutB[i]=uv.y;
        }
        [numthreads(256,1,1)]
        void ReactionDisplay(uint3 id : SV_DispatchThreadID) {
            if(id.x>=Count()) return;
            OutA[id.x]=ReactionDisplayValue(A[id.x]); OutB[id.x]=0;
        }

        // The renderer samples the field (0..1) and the display value of the winning scale.
        [numthreads(8,8,4)]
        void Publish(uint3 id : SV_DispatchThreadID)
        {
            uint n = Edge(); if (any(id >= n)) return;
            uint i = Index(id), scale = min((uint)B[i], 7u);
            float shown = scale < 4 ? P[5][scale] : P[6][scale - 4];
            if (P[8].x != 0) shown = A[i] * .5 + .5;
            Display[id] = float2(A[i] * .5 + .5, shown);
        }
        """;
}
