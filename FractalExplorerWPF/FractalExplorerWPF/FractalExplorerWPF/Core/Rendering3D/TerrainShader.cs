namespace FractalExplorerWPF.Core.Rendering3D;

internal static class TerrainShader
{
    // ShapeA: patch width, maximum height, grid resolution. Each cell has two triangles.
    public const string Source = """
        Texture2D<float> TerrainTexture : register(t1);

        float4 TerrainCell(int2 cell)
        {
            return float4(TerrainTexture.Load(int3(cell, 0)),
                TerrainTexture.Load(int3(cell + int2(1, 0), 0)),
                TerrainTexture.Load(int3(cell + int2(0, 1), 0)),
                TerrainTexture.Load(int3(cell + int2(1, 1), 0)));
        }

        float TerrainHeight(float2 positionXZ, out float2 slope)
        {
            float cells = ShapeA.z - 1.0;
            float2 grid = clamp((positionXZ / ShapeA.x + 0.5) * cells, 0.0, cells);
            int2 cell = min((int2)floor(grid), (int)cells - 1);
            float2 uv = grid - cell;
            float4 h = TerrainCell(cell);
            if (uv.x + uv.y <= 1.0)
            {
                slope = float2(h.y - h.x, h.z - h.x);
                float value = h.x + dot(slope, uv);
                slope *= cells / ShapeA.x;
                return value;
            }
            slope = float2(h.w - h.z, h.w - h.y);
            float value = h.w + dot(slope, uv - 1.0);
            slope *= cells / ShapeA.x;
            return value;
        }

        bool TerrainSlab(float origin, float direction, float low, float high, inout float enter, inout float leave)
        {
            if (abs(direction) < 1e-10) return origin >= low && origin <= high;
            float a = (low - origin) / direction;
            float b = (high - origin) / direction;
            enter = max(enter, min(a, b));
            leave = min(leave, max(a, b));
            return leave >= enter;
        }

        bool TerrainTriangle(float3 origin, float3 ray, float2 base, float2 slope,
            float height, float cellSize, bool upper, float enter, float leave, out float t)
        {
            float denominator = ray.y - dot(slope, ray.xz);
            t = -1;
            if (abs(denominator) < 1e-10) return false;
            t = (height + dot(slope, origin.xz - base) - origin.y) / denominator;
            if (t < max(enter - 1e-5, 0.0) || t > leave + 1e-5) return false;
            float2 uv = (origin.xz + ray.xz * t - base) / cellSize;
            if (upper) uv += 1.0;
            return all(uv >= -1e-4) && all(uv <= 1.0001) &&
                (upper ? uv.x + uv.y >= 0.9999 : uv.x + uv.y <= 1.0001);
        }

        // Exact grid traversal: no distance-estimator tolerance, no skipped thin ridges.
        bool TraceTerrain(float3 origin, float3 ray, float limit, out float distance, out int steps)
        {
            distance = 0;
            steps = 0;
            float enter = 0, leave = limit;
            float halfSize = ShapeA.x * 0.5;
            if (!TerrainSlab(origin.x, ray.x, -halfSize, halfSize, enter, leave) ||
                !TerrainSlab(origin.z, ray.z, -halfSize, halfSize, enter, leave) ||
                !TerrainSlab(origin.y, ray.y, 0.0, ShapeA.y, enter, leave)) return false;
            int cells = (int)ShapeA.z - 1;
            float cellSize = ShapeA.x / cells;
            float2 grid = (origin.xz + ray.xz * enter + halfSize) / cellSize;
            int2 cell = clamp((int2)floor(grid), 0, cells - 1);
            int2 direction = int2(ray.x >= 0 ? 1 : -1, ray.z >= 0 ? 1 : -1);
            float2 nextEdge = (cell + int2(direction.x > 0 ? 1 : 0, direction.y > 0 ? 1 : 0)) * cellSize - halfSize;
            float2 next = float2(abs(ray.x) < 1e-10 ? 1e30 : (nextEdge.x - origin.x) / ray.x,
                                abs(ray.z) < 1e-10 ? 1e30 : (nextEdge.y - origin.z) / ray.z);
            float2 delta = cellSize / max(abs(ray.xz), 1e-30);
            [loop]
            for (int index = 0; index < cells * 2 + 2; index++)
            {
                steps = index + 1;
                float end = min(leave, min(next.x, next.y));
                float4 h = TerrainCell(cell);
                float2 base = cell * cellSize - halfSize;
                float a, b;
                bool first = TerrainTriangle(origin, ray, base, float2(h.y-h.x, h.z-h.x) / cellSize,
                    h.x, cellSize, false, enter, end, a);
                bool second = TerrainTriangle(origin, ray, base + cellSize, float2(h.w-h.z, h.w-h.y) / cellSize,
                    h.w, cellSize, true, enter, end, b);
                if (first || second)
                {
                    distance = first && second ? min(a, b) : (first ? a : b);
                    return true;
                }
                if (end >= leave) break;
                bool crossX = next.x <= next.y;
                bool crossZ = next.y <= next.x;
                enter = end;
                if (crossX) { cell.x += direction.x; next.x += delta.x; }
                if (crossZ) { cell.y += direction.y; next.y += delta.y; }
                if (any(cell < 0) || any(cell >= cells)) break;
            }
            return false;
        }
        """;
}
