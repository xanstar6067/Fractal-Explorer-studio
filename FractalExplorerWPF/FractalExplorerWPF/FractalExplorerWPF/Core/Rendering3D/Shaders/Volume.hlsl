// Общий объёмный шейдер: камера, зонд расстояния, девять стилей освещения и окраска по
// трёхмерной текстуре Density. Режим задаёт VOLUME_KIND (VolumeShader.Build): IFS и
// аттракторы, DLA и фигуры Лихтенберга, Flame и Буддаброт 4D, живые моделирования на ГП.

// Живые моделирования: срез по оси (ShapeB.x — ось, ShapeB.y — положение), грани куба
// как поверхность, размер сетки — ShapeA.x, уровень поверхности — ShapeA.y.
#define VOLUME_GPU_FIELD (VOLUME_KIND == KIND_GrayScott3D || VOLUME_KIND == KIND_CahnHilliard3D || \
    VOLUME_KIND == KIND_Excitable3D || VOLUME_KIND == KIND_GinzburgLandau3D || \
    VOLUME_KIND == KIND_Kobayashi3D || VOLUME_KIND == KIND_PhaseFieldCrystal3D || \
    VOLUME_KIND == KIND_SwiftHohenberg3D || VOLUME_KIND == KIND_Lenia3D || \
    VOLUME_KIND == KIND_Physarum3D || VOLUME_KIND == KIND_Lyapunov3D)
// Срез и грани — как у моделирований; у Тьюринга своя маска области и порог 0,5.
#define VOLUME_CUT (VOLUME_GPU_FIELD || VOLUME_KIND == KIND_Turing3D)
// Излучение и поглощение по цвету текселя (rgb — сумма цветов, a — плотность).
#define VOLUME_FLAME (VOLUME_KIND == KIND_Flame3D || VOLUME_KIND == KIND_Buddhabrot4D)

cbuffer Frame : register(b0)
{
    float4 Resolution;
    float4 CameraPosition;
    float4 CameraRight;
    float4 CameraUp;
    float4 CameraForward;
    float4 March;
    float4 ShapeA;
    float4 ShapeB;
    float4 ShapeC;
    float4 Light;
    float4 Surface;
    float4 BackgroundTop;
    float4 BackgroundBottom;
    float4 Flags;
    float4 Probe;
    float4 PickerMarker; // общий буфер кадра; для объёмного IFS не используется
    float4 Style;
    float4 LightColor;
    float4 PaletteInfo;
    float4 HomeCamera;  // общий буфер кадра: сохраняем смещение Palette
    float4 BoxInversion; // общий буфер кадра: сохраняем смещение Palette
    float4 Palette[16];
};

#if VOLUME_FLAME || VOLUME_KIND == KIND_Excitable3D || VOLUME_KIND == KIND_GinzburgLandau3D
Texture3D<float4> Density : register(t0);
#elif VOLUME_KIND == KIND_Dla3D || VOLUME_KIND == KIND_Turing3D || \
    VOLUME_KIND == KIND_Kobayashi3D || VOLUME_KIND == KIND_PhaseFieldCrystal3D
Texture3D<float2> Density : register(t0);
#else
Texture3D<float> Density : register(t0);
#endif
SamplerState DensitySampler : register(s0);

struct PSInput { float4 position : SV_POSITION; };

PSInput VSMain(uint id : SV_VertexID)
{
    PSInput output;
    output.position = float4((id == 2 ? 3.0 : -1.0),
                             (id == 1 ? -3.0 : 1.0), 0.0, 1.0);
    return output;
}

float4 PackFloat(float value)
{
    uint bits = asuint(value);
    return float4((bits >> 16) & 255, (bits >> 8) & 255,
                  bits & 255, (bits >> 24) & 255) / 255.0;
}

#if VOLUME_FLAME
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
#endif

float3 LinearToSrgb(float3 color)
{
    color = saturate(color);
    float3 low = color * 12.92;
    float3 high = 1.055 * pow(max(color, 1e-8), 1.0 / 2.4) - 0.055;
    return lerp(low, high, step(0.0031308, color));
}

#if VOLUME_KIND == KIND_Turing3D
// Область показа — куб, шар или сферическая оболочка — с мягким краем в полторы клетки.
// Радиус 0.97 записан литералом: шар не упирается в грани куба.
float RegionMask(float3 p)
{
    if (any(abs(p) > 1.0001)) return 0.0;
    int axis = (int)ShapeB.x;
    if (axis > 0 && p[axis - 1] > ShapeB.y + .0001) return 0.0;
    int region = (int)ShapeB.z;
    if (region == 0) return 1.0;
    float edge = 3.0 / ShapeA.x, radius = length(p);
    float mask = saturate((0.97 - radius) / edge + 0.5);
    if (region == 2) mask *= saturate((radius - (0.97 - ShapeB.w)) / edge + 0.5);
    return mask;
}

// Filtered, so borders between scales blend instead of showing cubic cells.
float ScaleAt(float3 p)
{
    return Density.SampleLevel(DensitySampler, saturate(p * 0.5 + 0.5), 0).y;
}
#elif VOLUME_KIND == KIND_Lyapunov3D
// Поле содержит настоящую экспоненту λ.
float ExponentAt(float3 p) {
    return Density.SampleLevel(DensitySampler, saturate(p*.5+.5), 0);
}
float ExponentColor(float3 p) {
    return saturate(.5+ExponentAt(p)/(2*ShapeA.z));
}
#endif

float SampleDensity(float3 samplePosition)
{
#if VOLUME_KIND == KIND_Turing3D
    // Поле переводится в заполненность с уровнем поверхности 0,5: у пустой фазы плотность
    // нулевая, и тени, затенение складок и облако ведут себя как у остальных объёмов.
    float region = RegionMask(samplePosition);
    if (region <= 0.0) return 0.0;
    float field = Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).x;
    // ShapeA.z > 0: a two-sided sheet of that half-thickness around the level.
    float shape = ShapeA.z > 0.0 ? saturate((ShapeA.z - abs(field - ShapeA.y)) * 5.0 + 0.5)
        : saturate((field - ShapeA.y) * 2.5 + 0.5);
    return shape * region;
#elif VOLUME_GPU_FIELD
    if (any(abs(samplePosition) > 1.0001)) return 0.0;
    int axis = (int)ShapeB.x;
    if (axis > 0 && samplePosition[axis - 1] > ShapeB.y + .0001) return 0.0;
#if VOLUME_KIND == KIND_CahnHilliard3D
    // ShapeB.z — показ дополнительной фазы.
    float c = Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0); return ShapeB.z > .5 ? 1.0-c : c;
#elif VOLUME_KIND == KIND_Excitable3D
    // Фронты активатора, нити с дискретной намоткой фазы или их объединение.
    float4 field = Density.SampleLevel(DensitySampler, saturate(samplePosition * .5 + .5), 0);
    if (ShapeB.z > .5 && ShapeB.z < 1.5) return field.w;
    return ShapeB.z > 1.5 ? max(field.x, field.w) : field.x;
#elif VOLUME_KIND == KIND_GinzburgLandau3D
    // Нити, провалы амплитуды или сама амплитуда.
    float4 field = Density.SampleLevel(DensitySampler, saturate(samplePosition * .5 + .5), 0);
    if (ShapeB.z < .5) return field.w;
    return ShapeB.z < 1.5 ? saturate(1-field.x) : field.x;
#elif VOLUME_KIND == KIND_Kobayashi3D
    // x — доля твёрдой фазы, y — температура.
    return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).x;
#elif VOLUME_KIND == KIND_PhaseFieldCrystal3D
    float2 field=Density.SampleLevel(DensitySampler,saturate(samplePosition*.5+.5),0);
    if(ShapeB.z>1.5) return field.y*2.5/ShapeA.z;
    float delta=(field.x-ShapeB.w)/ShapeA.z;
    return max(ShapeB.z>.5 ? -delta : delta,0);
#elif VOLUME_KIND == KIND_SwiftHohenberg3D
    // Сырое знаковое поле.
    float u=Density.SampleLevel(DensitySampler,saturate(samplePosition*.5+.5),0)/ShapeA.z;
    if(ShapeB.z>1.5) return saturate(1-abs(u));
    return max(ShapeB.z>.5 ? -u : u,0);
#elif VOLUME_KIND == KIND_Physarum3D
    return 1-exp(-max(Density.SampleLevel(DensitySampler, saturate(samplePosition * .5 + .5), 0),0)*ShapeB.z);
#elif VOLUME_KIND == KIND_Lyapunov3D
    float delta = ExponentAt(samplePosition)-BoxInversion.x;
    int region = (int)ShapeB.z;
    if (region == 2) return saturate(1-abs(delta)/(2*ShapeB.w));
    return saturate(.5+(region == 1 ? delta : -delta)/(2*ShapeA.z));
#else
    return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);
#endif
#elif VOLUME_KIND == KIND_Dla3D
    // r — заполненность, g — время рождения частицы.
    return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).r;
#elif VOLUME_FLAME
    return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0).a;
#else
    return Density.SampleLevel(DensitySampler, saturate(samplePosition * 0.5 + 0.5), 0);
#endif
}

float3 SkyAt(float3 direction)
{
    return lerp(BackgroundBottom.rgb, BackgroundTop.rgb,
                saturate(direction.y * 0.5 + 0.5));
}

float3 SamplePalette(float value, int repeat)
{
    int count = clamp((int)PaletteInfo.x, 1, 16);
    float t = repeat == 1 ? frac(value) :
        repeat == 2 ? 1.0 - abs(2.0 * frac(value) - 1.0) : saturate(value);
    if (repeat == 2) t = t * t * (3.0 - 2.0 * t);
    t = pow(saturate(t), max(PaletteInfo.w, 1e-3));
    if (count == 1) return Palette[0].rgb;
    if (PaletteInfo.z > 0.5)
        return Palette[clamp((int)(t * count), 0, count - 1)].rgb;
    if (repeat == 1)
    {
        float scaled = t * count;
        int low = clamp((int)scaled, 0, count - 1);
        int high = low + 1 == count ? 0 : low + 1;
        return lerp(Palette[low].rgb, Palette[high].rgb, saturate(scaled - low));
    }
    float span = t * (count - 1);
    int first = clamp((int)span, 0, count - 2);
    return lerp(Palette[first].rgb, Palette[first + 1].rgb, saturate(span - first));
}

float3 SurfaceAlbedo(float3 normal, float3 surfacePoint, float3 rayDirection,
                     float depth, float occlusion, float stepsRatio)
{
#if VOLUME_FLAME
    return FlameColor(surfacePoint);
#else
    int mode = (int)Flags.x;
    if (mode == 0) return Surface.rgb;
    if (mode == 1) return abs(normal);
    float value;
#if VOLUME_CUT
    // Источник цвета 2 (и свободные 4–6) — величина поля режима.
#if VOLUME_KIND == KIND_Turing3D
    if (mode == 2) value = ScaleAt(surfacePoint);
    else if (mode == 4) value = length(surfacePoint);
#elif VOLUME_KIND == KIND_Excitable3D || VOLUME_KIND == KIND_GinzburgLandau3D
    if (mode == 2) {
        float4 field = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);
        float phase = (atan2(field.z, field.y) + 3.14159265359) / 6.28318530718;
        return SamplePalette(phase * ShapeC.y + ShapeC.z, 1);
    }
    else if (mode == 4) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0).x;
#elif VOLUME_KIND == KIND_Kobayashi3D
    if (mode == 2) value = saturate((Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0).y + ShapeB.z) / max(ShapeB.w, .001));
#elif VOLUME_KIND == KIND_PhaseFieldCrystal3D
    if(mode==2) value=.5+.5*(Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0).x-ShapeB.w)/ShapeA.z;
    else if(mode==4) value=Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0).y*2.5/ShapeA.z;
#elif VOLUME_KIND == KIND_SwiftHohenberg3D
    if (mode == 2) value=.5+.5*Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0)/ShapeA.z;
    else if (mode == 4) value=abs(Density.SampleLevel(DensitySampler,saturate(surfacePoint*.5+.5),0))/ShapeA.z;
#elif VOLUME_KIND == KIND_Lenia3D || VOLUME_KIND == KIND_Physarum3D
    if (mode == 2) value = SampleDensity(surfacePoint);
#elif VOLUME_KIND == KIND_Lyapunov3D
    if (mode == 2) value = ExponentColor(surfacePoint);
    else if (mode == 4) value = surfacePoint.x*.5+.5;
    else if (mode == 5) value = surfacePoint.y*.5+.5;
    else if (mode == 6) value = surfacePoint.z*.5+.5;
#else
    if (mode == 2) value = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0);
#endif
    else if (mode == 3) value = depth * 3.0;
#else
    if (mode == 3) value = depth * 3.0;
#endif
#if VOLUME_KIND == KIND_Dla3D
    else if (mode == 5) { float2 birthData = Density.SampleLevel(DensitySampler, saturate(surfacePoint * .5 + .5), 0); value = birthData.g / max(birthData.r, 1e-5); }
#endif
    else if (mode == 7) value = surfacePoint.y;
    else if (mode == 8) value = 1.0 - occlusion;
    else if (mode == 9) value = 1.0 - saturate(dot(normal, -rayDirection));
    else value = stepsRatio;
    return SamplePalette(value * ShapeC.y + ShapeC.z, (int)PaletteInfo.y);
#endif
}

float3 EstimateNormal(float3 p, float cell, float3 rayDirection)
{
#if VOLUME_CUT
    // На плоскости среза и гранях куба поверхность — сама плоскость.
    int axis = (int)ShapeB.x;
    if (axis > 0 && abs(p[axis - 1] - ShapeB.y) < .0002)
        return axis == 1 ? float3(1,0,0) : axis == 2 ? float3(0,1,0) : float3(0,0,1);
    if (abs(abs(p.x) - 1.0) < .0002) return float3(sign(p.x),0,0);
    if (abs(abs(p.y) - 1.0) < .0002) return float3(0,sign(p.y),0);
    if (abs(abs(p.z) - 1.0) < .0002) return float3(0,0,sign(p.z));
#endif
    float3 gradient = float3(
        SampleDensity(p + float3(cell, 0, 0)) - SampleDensity(p - float3(cell, 0, 0)),
        SampleDensity(p + float3(0, cell, 0)) - SampleDensity(p - float3(0, cell, 0)),
        SampleDensity(p + float3(0, 0, cell)) - SampleDensity(p - float3(0, 0, cell)));
    return dot(gradient, gradient) > 1e-8 ? normalize(-gradient) : -rayDirection;
}

float LocalOcclusion(float3 p, float3 normal, float cell)
{
    // Sample the neighborhood around the outward side of the surface. A single
    // normal ray misses most of the adjacent IFS branches and gives a flat AO map.
    float3 q = p + normal * cell * 2.0;
    float nearRadius = cell * 8.0;
    float farRadius = cell * 24.0;
    float nearDensity =
        SampleDensity(q + float3(nearRadius, 0, 0)) + SampleDensity(q - float3(nearRadius, 0, 0)) +
        SampleDensity(q + float3(0, nearRadius, 0)) + SampleDensity(q - float3(0, nearRadius, 0)) +
        SampleDensity(q + float3(0, 0, nearRadius)) + SampleDensity(q - float3(0, 0, nearRadius));
    float farDensity =
        SampleDensity(q + float3(farRadius, 0, 0)) + SampleDensity(q - float3(farRadius, 0, 0)) +
        SampleDensity(q + float3(0, farRadius, 0)) + SampleDensity(q - float3(0, farRadius, 0)) +
        SampleDensity(q + float3(0, 0, farRadius)) + SampleDensity(q - float3(0, 0, farRadius));
    return saturate(1.0 - nearDensity * 0.45 - farDensity * 0.2);
}

float ShadowVisibility(float3 p, float3 lightDirection, float cell)
{
    float occupied = 0.0;
    [loop]
    for (int i = 1; i <= 32; i++)
        occupied += SampleDensity(p + lightDirection * (cell * (i * 4.0 + 2.0)));
    return exp(-occupied * cell * 1.5 * Flags.y);
}

float BackThickness(float3 p, float3 normal, float cell)
{
    float occupied = 0.0;
    [unroll]
    for (int i = 1; i <= 12; i++)
        occupied += SampleDensity(p - normal * cell * (i * 2.0));
    return exp(-occupied * cell * 12.0);
}

// A reflected ray that escapes the volume also sees the lamp, as in the metal style: the
// march itself showed that nothing blocks it. Without the lamp the dark sky, tinted by
// the coloring, dimmed the mirrored figure almost down to the background.
float3 MirrorSky(float3 reflected)
{
    float sheen = saturate(dot(reflected, normalize(Light.xyz)));
    return SkyAt(reflected) * 2.0 +
        LightColor.rgb * (pow(sheen, 128.0) * 5.0 + pow(sheen, 6.0) * 0.45);
}

float3 MirrorReflection(float3 surfacePoint, float3 normal, float3 incoming, float cell)
{
    float3 reflected = normalize(reflect(incoming, normal));
    float3 origin = surfacePoint + normal * cell * 4.0;
    float3 safe = reflected + (abs(reflected) < 1e-6) * 1e-6;
    float3 nearPlane = (-1.0 - origin) / safe;
#if VOLUME_CUT
    int cutAxis = (int)ShapeB.x;
    float3 boxMax = float3(cutAxis == 1 ? ShapeB.y : 1.0,
        cutAxis == 2 ? ShapeB.y : 1.0, cutAxis == 3 ? ShapeB.y : 1.0);
    float3 farPlane = (boxMax - origin) / safe;
#else
    float3 farPlane = (1.0 - origin) / safe;
#endif
    float3 lo = min(nearPlane, farPlane);
    float3 hi = max(nearPlane, farPlane);
    float entry = max(0.0, max(lo.x, max(lo.y, lo.z)));
    float exitDistance = min(hi.x, min(hi.y, hi.z));
    if (entry >= exitDistance) return MirrorSky(reflected);

    float stepLength = cell * 0.65;
    float distance = entry;
#if VOLUME_KIND == KIND_Turing3D
    const float threshold = 0.5;
#elif VOLUME_GPU_FIELD
    float threshold = ShapeA.y;
#elif VOLUME_KIND == KIND_Dla3D
    const float threshold = 0.18;
#else
    const float threshold = 0.075;
#endif
    float previousDensity = SampleDensity(origin + reflected * distance);
    bool outside = previousDensity < threshold;
    [loop]
    for (int i = 0; i < 1536 && distance < exitDistance; i++)
    {
        float nextDistance = min(distance + stepLength, exitDistance);
        float density = SampleDensity(origin + reflected * nextDistance);
        if (outside && density >= threshold)
        {
            float hitDistance = lerp(distance, nextDistance,
                saturate((threshold - previousDensity) / max(density - previousDensity, 1e-6)));
            float3 hitPoint = origin + reflected * hitDistance;
            float3 hitNormal = EstimateNormal(hitPoint, cell, reflected);
            float3 albedo = SurfaceAlbedo(hitNormal, hitPoint, reflected,
                hitDistance - entry, 1.0, saturate((float)i / 1536.0));
            float diffuse = saturate(dot(hitNormal, normalize(Light.xyz)));
            return albedo * (Flags.w * 0.7 + diffuse * LightColor.rgb);
        }
        if (density < threshold) outside = true;
        previousDensity = density;
        distance = nextDistance;
    }
    return MirrorSky(reflected);
}

float4 PSMain(PSInput input) : SV_TARGET
{
    float2 pixel = input.position.xy + Resolution.zw;
    float2 plane = (pixel - 0.5 * Resolution.xy) / (0.5 * Resolution.y);
    plane.y = -plane.y;
    float3 direction = normalize(CameraForward.xyz * CameraPosition.w +
                                 CameraRight.xyz * plane.x + CameraUp.xyz * plane.y);
    float3 origin = CameraPosition.xyz;
    float3 safe = direction + (abs(direction) < 1e-6) * 1e-6;
    float3 nearPlane = (-1.0 - origin) / safe;
#if VOLUME_CUT
    int cutAxis = (int)ShapeB.x;
    float3 boxMax = float3(cutAxis == 1 ? ShapeB.y : 1.0,
        cutAxis == 2 ? ShapeB.y : 1.0, cutAxis == 3 ? ShapeB.y : 1.0);
    float3 farPlane = (boxMax - origin) / safe;
#else
    float3 farPlane = (1.0 - origin) / safe;
#endif
    float3 lo = min(nearPlane, farPlane);
    float3 hi = max(nearPlane, farPlane);
    float entry = max(0.0, max(lo.x, max(lo.y, lo.z)));
    float exitDistance = min(hi.x, min(hi.y, hi.z));
    float3 sky = SkyAt(direction);
#if VOLUME_KIND == KIND_Lyapunov3D
    // Плоское сечение (BoxInversion.y): цвет λ на плоскости среза без трассировки объёма.
    if (BoxInversion.y > .5) {
        int axis = (int)ShapeB.x-1;
        if (axis < 0 || abs(direction[axis]) < 1e-7)
            return Probe.x > .5 ? PackFloat(-1) : float4(LinearToSrgb(sky),1);
        float hit = (ShapeB.y-origin[axis])/direction[axis];
        float3 p = origin+direction*hit;
        if (hit < 0 || any(abs(p)>1.0001))
            return Probe.x > .5 ? PackFloat(-1) : float4(LinearToSrgb(sky),1);
        if (Probe.x > .5) return PackFloat(hit);
        float3 tint = SamplePalette(ExponentColor(p)*ShapeC.y+ShapeC.z,(int)PaletteInfo.y);
        return float4(LinearToSrgb(tint),1);
    }
#endif
    if (entry >= exitDistance)
        return Probe.x > 0.5 ? PackFloat(-1.0) : float4(LinearToSrgb(sky), 1.0);

    // The traversal must cover the whole box. A fixed user ray-step limit used to
    // terminate halfway through it, leaving camera-centered circular silhouettes.
#if VOLUME_CUT
    float cell = 2.0 / ShapeA.x;
#elif VOLUME_FLAME
    float cell = 2.0 / FLAME_SIDE;
#elif VOLUME_KIND == KIND_Dla3D
    float cell = 2.0 / 256.0;
#else
    float cell = 2.0 / 512.0;
#endif
    float stepLength = cell * 0.65;
#if VOLUME_KIND == KIND_Turing3D
    const float threshold = 0.5;
#elif VOLUME_GPU_FIELD
    float threshold = ShapeA.y;
#elif VOLUME_KIND == KIND_Dla3D
    const float threshold = 0.18;
#else
    const float threshold = 0.075;
#endif
    float distance = entry;
    float previousDensity = SampleDensity(origin + direction * distance);
    float integratedDensity = 0.0;
    float glowPath = 0.0;
#if VOLUME_KIND == KIND_Dla3D
    // Источник цвета 5 — среднее время рождения частиц вдоль луча.
    float birthSum = 0.0; float birthWeight = 0.0;
#endif
    float firstHit = -1.0;
    int usedSteps = 0;
#if VOLUME_KIND == KIND_Lenia3D || VOLUME_KIND == KIND_Physarum3D
    // Свечение интегрирует излучение вдоль всего луча и показывает клетки, внутренние
    // полости и нити глубоко в сети. Остальные стили и зонд — та же поверхность.
    if ((int)Style.x == 3 && Probe.x < .5) {
        float3 emission = 0; float transmittance = 1;
        float ds = 2.0 / ShapeA.x * .65;
        [loop] for (int j=0; j<640 && distance<exitDistance; j++) {
            float3 samplePoint=origin+direction*(distance+ds*.5);
            float density=SampleDensity(samplePoint);
            float visible=saturate((density-ShapeA.y)/max(1-ShapeA.y,.01));
            float alpha=1-exp(-visible*visible*ds*12*max(Style.y,.1));
            float3 tint=SamplePalette(saturate(density*.58+.12)*ShapeC.y+ShapeC.z,0);
            emission+=transmittance*alpha*tint*1.4;
            transmittance*=1-alpha*.85;
            distance+=ds;
        }
        float3 color=sky*transmittance+emission;
        return float4(LinearToSrgb(color/(1+color*.35)),1);
    }
#elif VOLUME_KIND == KIND_Lyapunov3D
    // Облако по заполненности, окрашенное величиной λ.
    if ((int)Style.x == 4 && Probe.x < .5) {
        float3 emission = 0; float transmission = 1;
        float ds = cell*.65;
        [loop] for (int j=0; j<1536 && distance<exitDistance && transmission>.005; j++) {
            float3 p = origin+direction*min(distance+ds*.5,exitDistance);
            float occupancy = saturate((SampleDensity(p)-.5)*2);
            float alpha = 1-exp(-occupancy*ds*8*max(Style.y,.001));
            float3 tint = SamplePalette(ExponentColor(p)*ShapeC.y+ShapeC.z,(int)PaletteInfo.y);
            emission += transmission*alpha*tint;
            transmission *= 1-alpha;
            distance += ds;
        }
        return float4(LinearToSrgb(sky*transmission+emission),1);
    }
#endif
    int style = (int)Style.x;
    float strength = max(Style.y, 0.0);
#if VOLUME_FLAME
    // Свечение и облако — излучение и поглощение вдоль луча; у Буддаброта свечение
    // аддитивно: вид спереди складывает все слои глубины, как двумерный Буддаброт.
    if (style == 3 || style == 4)
    {
        float3 radiance = 0;
        float transmission = 1;
        float probeHit = -1;
        [loop]
        for (int j = 0; j < 1536 && distance < exitDistance; j++)
        {
            float dt = min(stepLength, exitDistance - distance);
            float3 p = origin + direction * (distance + dt * 0.5);
            float4 v = Density.SampleLevel(DensitySampler, saturate(p * 0.5 + 0.5), 0);
            if (probeHit < 0 && v.a >= threshold) probeHit = distance + dt * 0.5;
#if VOLUME_KIND == KIND_Buddhabrot4D
            float alpha = style == 3 ? 0 : 1 - exp(-v.a * dt * ShapeA.z * 48);
#else
            float alpha = 1 - exp(-v.a * dt * ShapeA.z * (style == 3 ? 18 : 48));
#endif
            float3 tint = v.rgb / max(v.a, 1e-6);
#if VOLUME_KIND == KIND_Buddhabrot4D
            radiance += style == 3 ? v.rgb * dt * ShapeA.z * 12 * max(strength, 0.001) : transmission * alpha * tint * 1.5 * max(strength, 0.001);
#else
            radiance += transmission * alpha * tint * (style == 3 ? 3.0 : 1.5) * max(strength, 0.001);
#endif
            transmission *= 1 - alpha;
            distance += dt;
            if (transmission < 0.002) break;
        }
        if (Probe.x > 0.5) return PackFloat(probeHit);
        return float4(LinearToSrgb(FlameTone(radiance) + sky * transmission), 1);
    }
#endif
    [loop]
    for (int i = 0; i < 1536 && distance < exitDistance; i++)
    {
        float nextDistance = min(distance + stepLength, exitDistance);
        float density = SampleDensity(origin + direction * nextDistance);
        float segmentDensity = 0.5 * (previousDensity + density);
        integratedDensity += segmentDensity * (nextDistance - distance);
#if VOLUME_KIND == KIND_Dla3D
        float2 birth = Density.SampleLevel(DensitySampler, saturate((origin + direction * nextDistance) * .5 + .5), 0); birthSum += birth.g * (nextDistance - distance); birthWeight += birth.r * (nextDistance - distance);
#endif
        if (style == 3 && (i & 3) == 0)
        {
            float3 samplePosition = origin + direction * nextDistance;
            float reach = cell * 8.0;
            float nearby = max(segmentDensity,
                max(max(SampleDensity(samplePosition + CameraRight.xyz * reach),
                        SampleDensity(samplePosition - CameraRight.xyz * reach)),
                    max(SampleDensity(samplePosition + CameraUp.xyz * reach),
                        SampleDensity(samplePosition - CameraUp.xyz * reach))));
            glowPath += nearby * (nextDistance - distance) * 4.0;
        }
        if (firstHit < 0.0 && (previousDensity >= threshold || density >= threshold))
        {
            // Interpolate the crossing to keep both shading and the camera probe
            // stable as the ray sampling phase changes during movement.
            firstHit = previousDensity >= threshold ? distance :
                lerp(distance, nextDistance,
                     saturate((threshold - previousDensity) /
                              max(density - previousDensity, 1e-6)));
            usedSteps = i + 1;
            if (Probe.x > 0.5) return PackFloat(firstHit);
            if (style != 4) break;
        }
        previousDensity = density;
        distance = nextDistance;
    }
    if (Probe.x > 0.5) return PackFloat(-1.0);

    if (style == 4)
    {
        // This style uses the actual orbit density, unlike the DE-based modes.
        float opacity = 1.0 - exp(-integratedDensity * max(strength, 1e-3) * 8.0);
#if VOLUME_KIND == KIND_Dla3D
        float3 tint = SamplePalette(((int)Flags.x == 5 ? birthSum / max(birthWeight, 1e-5) : opacity) * ShapeC.y + ShapeC.z, (int)PaletteInfo.y);
#else
        float3 tint = SamplePalette(opacity * ShapeC.y + ShapeC.z, 0);
#endif
        return float4(LinearToSrgb(lerp(sky, tint, opacity)), 1.0);
    }

    float glow = style == 3 ? saturate(glowPath * 24.0 * strength) : 0.0;
#if VOLUME_KIND == KIND_Dla3D
    float3 glowTint = style == 3 ? SamplePalette(((int)Flags.x == 5 ? birthSum / max(birthWeight, 1e-5) : glow) * ShapeC.y + ShapeC.z, (int)PaletteInfo.y) : 0.0;
#else
    float3 glowTint = style == 3 ? SamplePalette(glow * ShapeC.y + ShapeC.z, 0) : 0.0;
#endif
    if (firstHit < 0.0)
        return float4(LinearToSrgb(sky + glowTint * glow), 1.0);

    float3 surfacePoint = origin + direction * firstHit;
    float3 normal = EstimateNormal(surfacePoint, cell, direction);
    float3 lightDirection = normalize(Light.xyz);
    float3 lightTint = LightColor.rgb;
    float diffuse = saturate(dot(normal, lightDirection));
    float shadow = Flags.y > 0.0 && diffuse > 0.0
        ? ShadowVisibility(surfacePoint + normal * cell * 2.0, lightDirection, cell)
        : 1.0;
    float rawOcclusion = ((int)Flags.x == 8 || Flags.z > 0.5)
        ? LocalOcclusion(surfacePoint, normal, cell) : 1.0;
    float occlusion = Flags.z > 0.5
        ? lerp(1.0, rawOcclusion, saturate(Surface.a)) : 1.0;
    float3 ambientTint = lerp(float3(1.0, 1.0, 1.0), SkyAt(normal) * 3.0,
                               saturate(Style.z));
    float3 ambient = Flags.w * ambientTint;
    float3 albedo = SurfaceAlbedo(normal, surfacePoint, direction,
        firstHit - entry, rawOcclusion, saturate((float)usedSteps / 1536.0));
    float3 halfVector = normalize(lightDirection - direction);
    float specular = Light.w * pow(saturate(dot(normal, halfVector)), 32.0) * shadow;

    float3 color;
    if (style == 1)
    {
        float wrapped = saturate(dot(normal, lightDirection) * 0.5 + 0.5);
        color = albedo * (ambient * 0.8 + wrapped * shadow * lightTint) *
            occlusion * occlusion;
    }
    else if (style == 2)
    {
        float3 reflected = reflect(direction, normal);
        float fresnel = pow(1.0 - saturate(dot(normal, -direction)), 5.0);
        float sheen = saturate(dot(reflected, lightDirection));
        float3 environment = SkyAt(reflected) * 2.0 +
            lightTint * (pow(sheen, 128.0) * 5.0 + pow(sheen, 6.0) * 0.45);
        color = albedo * (ambient + diffuse * shadow * lightTint * 0.25) * occlusion * 0.6 +
            albedo * environment * lerp(0.35, 1.0, fresnel) * shadow * strength;
    }
    else if (style == 5)
    {
        float2 screenNormal = float2(dot(normal, CameraRight.xyz), dot(normal, CameraUp.xyz));
        float key = saturate(dot(screenNormal, normalize(float2(-0.55, 0.62))) * 0.6 + 0.55);
        float fill = saturate(dot(screenNormal, normalize(float2(0.7, -0.3))) * 0.5 + 0.5) * 0.25;
        float rim = pow(saturate(length(screenNormal)), 6.0) * 0.55 * strength;
        color = albedo * (key + fill + Flags.w * 0.3) * occlusion + rim * lightTint;
    }
    else if (style == 6)
    {
        float levels = max(2.0, floor(2.0 + strength * 2.0));
        float lit = floor(saturate(diffuse * shadow) * levels) / max(levels - 1.0, 1.0);
        float edge = 1.0 - smoothstep(0.12, 0.42,
            saturate(dot(normal, -direction)));
        color = albedo * (ambient + lit * lightTint) * occlusion * (1.0 - edge);
    }
    else if (style == 7)
    {
        float thickness = BackThickness(surfacePoint, normal, cell);
        float back = pow(saturate(dot(-normal, lightDirection)) * 0.6 + 0.4, 2.0);
        color = albedo * (ambient + diffuse * shadow * lightTint * 0.6) * occlusion +
            albedo * back * thickness * strength * lightTint * 2.5 + specular * lightTint;
    }
    else if (style == 8)
    {
        float3 reflected = MirrorReflection(surfacePoint, normal, direction, cell);
        float3 baseColor = albedo * (ambient + diffuse * shadow * lightTint) * occlusion +
            specular * lightTint;
        color = lerp(baseColor, reflected * albedo, saturate(strength));
    }
    else
    {
        color = albedo * (ambient + diffuse * shadow * lightTint) * occlusion +
            specular * lightTint;
    }
    if (style == 3) color += glowTint * glow * 1.2;
#if VOLUME_FLAME
    return float4(LinearToSrgb(FlameTone(color)), 1.0);
#else
    return float4(LinearToSrgb(color), 1.0);
#endif
}
