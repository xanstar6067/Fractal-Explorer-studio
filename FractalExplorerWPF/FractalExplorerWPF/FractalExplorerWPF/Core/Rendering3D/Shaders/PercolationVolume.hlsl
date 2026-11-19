// Точный обход вокселей по границам ячеек. Отсечение выровнено по слоям решётки.
float3 PercolationTint(uint4 voxel, float3 normal, float3 position, float depth, float facing, float stepsRatio)
{
    int mode = (int)Flags.x;
    if (mode == 0) return Surface.rgb;
    if (mode == 1) return abs(normal);
    float value = voxel.g / 255.0; // Идентификатор кластера.
    if (mode == 3) value = saturate(depth / max(2.0 * length(CameraPosition.xyz), 1.0));
    else if (mode == 4) value = voxel.b / 255.0;
    else if (mode == 7) value = position.y * .5 + .5;
    else if (mode == 8) value = facing;
    else if (mode == 9) value = 1.0 - facing;
    else if (mode == 10) value = stepsRatio;
    return SamplePalette(value * ShapeC.y + ShapeC.z, (int)PaletteInfo.y);
}

float3 PercolationLight(float3 tint, float3 normal, float3 ray, float edge)
{
    int style = (int)Style.x;
    float facing = saturate(dot(normal, -ray));
    float diffuse = saturate(dot(normal, Light.xyz));
    float cameraFill = .28 + .37 * facing;
    float ambient = Flags.w + cameraFill;
    float specular = Light.w * pow(saturate(dot(normal, normalize(Light.xyz - ray))), 22.0);
    if (style == 1) return tint * (ambient + .55 * sqrt(diffuse)) * edge;
    if (style == 2 || style == 8)
    {
        float3 reflected = SkyAt(reflect(ray, normal));
        return (tint * (ambient * .6 + diffuse * LightColor.rgb * .4 + reflected * Style.y) +
            specular * LightColor.rgb) * edge;
    }
    if (style == 3) return tint * (ambient + .7 + .4 * Style.y) * edge;
    if (style == 5)
    {
        float key = saturate(.48 + .48 * dot(normal, normalize(CameraUp.xyz - CameraRight.xyz)));
        return tint * (ambient + key * .75) * edge + specular * LightColor.rgb * .4;
    }
    if (style == 6) diffuse = floor(diffuse * 4.0) * .25;
    if (style == 7) ambient += .35 * Style.y * saturate(dot(-normal, Light.xyz));
    return tint * (ambient + diffuse * LightColor.rgb * .7) * edge + specular * LightColor.rgb * .55;
}

float3 PercolationAxisVector(int axis)
{
    return float3(axis == 0, axis == 1, axis == 2);
}

float4 PSMain(PSInput input) : SV_TARGET
{
    float2 plane = (input.position.xy + Resolution.zw - 0.5 * Resolution.xy) / (0.5 * Resolution.y);
    plane.y = -plane.y;
    float3 ray = normalize(CameraForward.xyz * CameraPosition.w + CameraRight.xyz * plane.x + CameraUp.xyz * plane.y);
    float3 direction = (ray < 0 ? -1.0 : 1.0) * max(abs(ray), 1e-6);
    float3 origin = CameraPosition.xyz;
    float3 sky = SkyAt(ray);
    int side = (int)ShapeA.x;
    int view = (int)ShapeA.y;
    float cellSize = 2.0 / side;

    int cut = (int)ShapeB.x;
    int cutAxis = (int)ShapeB.y;
    float3 boxMin = -1.0;
    float3 boxMax = 1.0;
    if (cut != 0)
    {
        float3 cutDirection = PercolationAxisVector(cutAxis);
        boxMax -= cutDirection * (2.0 - (ShapeB.z + 1.0) * cellSize);
        if (cut == 2) boxMin += cutDirection * ShapeB.z * cellSize;
    }
    float3 a = (boxMin - origin) / direction;
    float3 b = (boxMax - origin) / direction;
    float3 lo = min(a, b), hi = max(a, b);
    float entry = max(0.0, max(lo.x, max(lo.y, lo.z)));
    float exit = min(hi.x, min(hi.y, hi.z));
    if (entry >= exit)
        return Probe.x > .5 ? PackFloat(-1.0) : float4(LinearToSrgb(sky), 1.0);

    int3 stepDir = direction < 0 ? -1 : 1;
    float3 delta = cellSize / abs(direction);
    float3 gridOrigin = (origin + 1.0) / cellSize;
    float3 gridEntry = gridOrigin + (direction / cellSize) * entry;
    int3 cell = clamp((int3)floor(gridEntry + stepDir * 1e-4), 0, side - 1);
    float3 next = (cell + max(0, stepDir) - gridOrigin) * cellSize / direction;
    int entryAxis = lo.x >= lo.y && lo.x >= lo.z ? 0 : lo.y >= lo.z ? 1 : 2;
    float3 normal = PercolationAxisVector(entryAxis) * -stepDir;

    float depth = entry, opacity = 0.0;
    float3 color = 0, routeColor = 0;
    bool routeSeen = false;
    bool showPath = ShapeB.w > .5;
    [loop]
    for (int i = 0; i < side * 3 && depth < exit; i++)
    {
        float nextDepth = min(next.x, min(next.y, next.z));
        float chord = max(0.0, (min(nextDepth, exit) - depth) / cellSize);
        uint4 voxel = Density.Load(int4(cell, 0));
        bool largest = (voxel.r & 2) != 0;
        bool spanning = (voxel.r & 4) != 0;
        bool path = showPath && voxel.a != 0;
        bool visible = voxel.r != 0 && (view == 0 || (view == 1 && largest) ||
            (view == 2 && spanning) || (view == 3 && (largest || spanning)));
        if (visible && chord > 1e-4)
        {
            if (Probe.x > .5) return PackFloat(depth);
            float3 position = origin + direction * depth;
            float3 local = saturate(gridOrigin + (direction / cellSize) * depth - cell);
            float3 borders = min(local, 1.0 - local) + abs(normal);
            float border = min(borders.x, min(borders.y, borders.z));
            float edge = lerp(Flags.z > .5 ? 1.0 - .42 * Surface.w : .9, 1.0, smoothstep(.0, .11, border));
            float facing = saturate(dot(normal, -ray));
            float3 tint = PercolationTint(voxel, normal, position, depth, facing, i / (side * 3.0));
            if (view == 3) tint = lerp(tint, float3(.018, .22, .42), .8);
            float3 shaded = PercolationLight(tint, normal, ray, edge);
            // В режиме маршрута его цвет остаётся виден сквозь окружающий кластер.
            if (path && !routeSeen)
            {
                float progress = (voxel.a - 1) / 254.0;
                float3 trace = lerp(float3(.10, .90, .68), float3(1.0, .24, .034), progress);
                if (voxel.a == 1 || voxel.a == 255) trace = lerp(trace, 1.0, .6);
                routeColor = trace * (.70 + .35 * facing) * edge;
                routeSeen = true;
                if (view != 3)
                {
                    color += (1.0 - opacity) * routeColor;
                    opacity = 1.0;
                    break;
                }
            }
            float strength = view == 3 ? (cut == 2 ? .35 : .072) : view == 0 ? (spanning ? 1.15 : .52) : 2.7;
            if ((int)Style.x == 4) strength *= .32;
            float alpha = 1.0 - exp(-strength * chord);
            color += (1.0 - opacity) * alpha * shaded;
            opacity += (1.0 - opacity) * alpha;
            if (opacity >= .992 && (view != 3 || routeSeen || !showPath)) break;
        }

        depth = nextDepth;
        int axis = next.x <= next.y && next.x <= next.z ? 0 : next.y <= next.z ? 1 : 2;
        int3 advance = next <= nextDepth + 1e-6;
        cell += advance * stepDir;
        if (any(cell < 0) || any(cell >= side)) break;
        next += advance * delta;
        normal = PercolationAxisVector(axis) * -stepDir;
    }
    if (Probe.x > .5) return PackFloat(-1.0);

    // Каркас даёт масштаб пустым и разреженным образцам.
    float3 framePoint = origin + direction * entry;
    float3 edges = max(0.0, min(framePoint - boxMin, boxMax - framePoint));
    float secondEdge = max(min(edges.x, edges.y), min(max(edges.x, edges.y), edges.z));
    float frame = entry > 0.0 ? 1.0 - smoothstep(.005, .022, secondEdge) : 0;
    color += sky * (1.0 - opacity);
    color = lerp(color, float3(.13, .29, .43), .52 * frame);
    if (view == 3 && routeSeen) color = lerp(color, routeColor, .94);
    return float4(LinearToSrgb(color), 1.0);
}
