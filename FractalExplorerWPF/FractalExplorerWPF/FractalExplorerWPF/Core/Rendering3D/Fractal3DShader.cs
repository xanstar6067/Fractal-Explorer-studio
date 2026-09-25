using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Исходник пиксельного шейдера трассировки лучей по дистанционной оценке (distance estimation).
/// Вид фрактала подставляется препроцессором: для каждого <see cref="Fractal3DKind"/> компилируется
/// свой шейдер, поэтому в горячем цикле нет ветвления по режиму. Палитра, источник цвета и стиль
/// освещения приходят константами и ветвятся единообразно для всего кадра, поэтому набор встроенных
/// шейдеров не умножает число компиляций.
/// </summary>
internal static class Fractal3DShader
{
    public static string Build(Fractal3DKind kind) =>
        $"#define FRACTAL_KIND {(int)kind}\n" + (kind == Fractal3DKind.Terrain
            ? Source.Replace("float BoxDistance", TerrainShader.Source + "\nfloat BoxDistance") : Source);

    private const string Source = """
        struct PSInput
        {
            float4 position : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        cbuffer FrameConstants : register(b0)
        {
            float4 Resolution;        // xy — размер полного кадра, zw — смещение текущей полосы
            float4 CameraPosition;    // xyz — положение камеры, w — масштаб поля зрения
            float4 CameraRight;
            float4 CameraUp;
            float4 CameraForward;
            float4 March;             // x — шаги, y — детализация, z — дальность, w — итерации
            float4 ShapeA;            // x — степень/масштаб, y — мин. радиус², z — предел свёртки, w — радиус вылета
            float4 ShapeB;            // константа C
            float4 ShapeC;            // x — срез w, y — масштаб окраски, z — сдвиг окраски
            float4 Light;             // xyz — направление на источник, w — сила бликов
            float4 Surface;           // rgb — цвет материала, a — сила затенения
            float4 BackgroundTop;
            float4 BackgroundBottom;
            float4 Flags;             // x — режим окраски, y — жёсткость теней (0 — выкл), z — затенение, w — фоновый свет
            float4 Probe;             // x — 0: обычный кадр, 1: расстояние до поверхности вдоль луча
            float4 PickerMarker;      // xyz — C, w — радиус зелёной минисферы редактора
            float4 Style;             // x — шейдер освещения, y — сила эффекта, z — влияние неба, w — масштаб глубины
            float4 LightColor;        // rgb — цвет источника света
            float4 PaletteInfo;       // x — число цветов, y — повтор, z — полосы, w — гамма
            float4 Palette[16];       // rgb — опорные цвета градиента
        };

        #if FRACTAL_KIND == 6
        cbuffer ApollonianTree : register(b1)
        {
            float4 SphereTree[4095]; // xyz — центр, w — радиус; листья начинаются с 2047
        };
        #endif

        PSInput VSMain(uint id : SV_VertexID)
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            PSInput output;
            output.position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            output.uv = uv;
            return output;
        }

        float BoxDistance(float3 p, float3 b)
        {
            float3 q = abs(p) - b;
            return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
        }

        float3 RepeatSpace(float3 v, float period)
        {
            return v - period * floor(v / period);
        }

        float4 QuaternionSquare(float4 a)
        {
            return float4(a.x * a.x - dot(a.yzw, a.yzw), 2.0 * a.x * a.yzw);
        }

        // Расстояние орбиты до ближайшей координатной плоскости: вторая ловушка, дающая рисунок
        // вдоль осей там, где сферическая ловушка даёт кольца.
        float MinAxis(float3 v)
        {
            float3 a = abs(v);
            return min(a.x, min(a.y, a.z));
        }

        // Во сколько раз шаг луча должен быть короче оценки расстояния из последнего вызова Map.
        // Сама поверхность (где оценка меньше порога) от этого не меняется — только длина шага,
        // чтобы луч не перескакивал тонкие слои там, где оценка завышена.
        static float StepScale = 1.0;
        // Оценка Мандельбокса местами завышена: луч с полным шагом проскакивал поверхностный
        // слой и выедал в стенах тёмные полости. При 0.6 под острым углом ещё пропадали выступы
        // на рёбрах; с 0.35 кадр совпадает с трассировкой шагом 0.1 до шума субпиксельных деталей.
        #define BOX_STEP 0.35
        // Нижняя граница запаса у полюсов бульбов. Произведение множителей по всем итерациям
        // быстро становится крошечным, и тогда луч тратит все шаги, не дойдя до поверхности.
        #define BULB_STEP_FLOOR 0.125

        // Дистанционная оценка до поверхности фрактала. trap — данные орбиты, из которых берётся
        // цвет: x — минимальный радиус, y — минимум по осям, z — номер последней итерации
        // (у вылетающих орбит это итерация вылета, у остальных — итерация минимума),
        // w — радиус на выходе. В тенях и нормалях эти величины не читаются, и компилятор
        // выбрасывает их расчёт вместе с мёртвым кодом.
        float Map(float3 p, out float4 trap)
        {
            int iterations = (int)March.w;
            StepScale = 1.0;
            float trapRadius2 = 1e20;
            float trapAxis = 1e20;
            float trapIndex = 0.0;

        #if FRACTAL_KIND == 10
            float2 slope;
            float height = TerrainHeight(p.xz, slope);
            trap = 0;
            float edge = max(abs(p.x), abs(p.z)) - ShapeA.x * 0.5;
            return max(edge, (p.y - height) / sqrt(1.0 + dot(slope, slope)));
        #elif FRACTAL_KIND == 2

            float scale = ShapeA.x;
            float minRadius2 = ShapeA.y;
            float foldingLimit = ShapeA.z;
            float bailout = ShapeA.w;
            const float fixedRadius2 = 1.0;

            float3 z = p;
            float dr = 1.0;
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                trapIndex = (float)i;
                z = clamp(z, -foldingLimit, foldingLimit) * 2.0 - z;
                float r2 = dot(z, z);
                trapRadius2 = min(trapRadius2, r2);
                trapAxis = min(trapAxis, MinAxis(z));
                if (r2 < minRadius2)
                {
                    float factor = fixedRadius2 / max(minRadius2, 1e-6);
                    z *= factor;
                    dr *= factor;
                }
                else if (r2 < fixedRadius2)
                {
                    float factor = fixedRadius2 / r2;
                    z *= factor;
                    dr *= factor;
                }
                z = scale * z + p;
                dr = dr * abs(scale) + 1.0;
                if (dot(z, z) > bailout) break;
            }
            trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, length(z));
            StepScale = BOX_STEP;
            return length(z) / max(abs(dr), 1e-9);

        #elif FRACTAL_KIND == 3

            float d = BoxDistance(p, float3(1.0, 1.0, 1.0));
            float s = 1.0;
            float3 last = p;
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                float3 a = RepeatSpace(p * s, 2.0) - 1.0;
                s *= 3.0;
                float3 r = abs(1.0 - 3.0 * abs(a));
                float da = max(r.x, r.y);
                float db = max(r.y, r.z);
                float dc = max(r.z, r.x);
                float c = (min(da, min(db, dc)) - 1.0) / s;
                float r2 = dot(a, a);
                if (r2 < trapRadius2) { trapRadius2 = r2; trapIndex = (float)i; }
                trapAxis = min(trapAxis, MinAxis(a));
                last = a;
                d = max(d, c);
            }
            trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, length(last));
            return d;

        #elif FRACTAL_KIND == 8 || FRACTAL_KIND == 9

            // Каждая итерация проверяет один разряд трёхмерной сетки 3×3×3.
            // Для Вицека остаются три пересекающихся осевых бруска (7 кубиков),
            // для пыли Кантора — восемь угловых кубиков. Расстояние до каждого
            // уровня даёт консервативную оценку расстояния до их пересечения.
            float outer = BoxDistance(p, float3(1.0, 1.0, 1.0));
            float detail = -1e20;
            float scale = 1.0;
            float3 last = p;
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                float3 a = RepeatSpace(p * scale + 1.0, 2.0) - 1.0;
        #if FRACTAL_KIND == 8
                float level = min(
                    BoxDistance(a, float3(1.0, 1.0 / 3.0, 1.0 / 3.0)),
                    min(BoxDistance(a, float3(1.0 / 3.0, 1.0, 1.0 / 3.0)),
                        BoxDistance(a, float3(1.0 / 3.0, 1.0 / 3.0, 1.0))));
        #else
                float3 corner = abs(a) - float3(2.0 / 3.0, 2.0 / 3.0, 2.0 / 3.0);
                float level = BoxDistance(corner, float3(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0));
        #endif
                detail = max(detail, level / scale);
                float r2 = dot(a, a);
                if (r2 < trapRadius2) { trapRadius2 = r2; trapIndex = (float)i; }
                trapAxis = min(trapAxis, MinAxis(a));
                last = a;
                scale *= 3.0;
            }
            trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, length(last));
            // Смещение измеряется в единицах самого мелкого кубика; 1 — классика.
            return max(outer, detail - (ShapeA.x - 1.0) / scale);

        #elif FRACTAL_KIND == 4

            float scale = ShapeA.x;
            const float3 a1 = float3(1.0, 1.0, 1.0);
            const float3 a2 = float3(-1.0, -1.0, 1.0);
            const float3 a3 = float3(1.0, -1.0, -1.0);
            const float3 a4 = float3(-1.0, 1.0, -1.0);

            float3 z = p;
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                float3 c = a1;
                float nearest = length(z - a1);
                float d = length(z - a2);
                if (d < nearest) { c = a2; nearest = d; }
                d = length(z - a3);
                if (d < nearest) { c = a3; nearest = d; }
                d = length(z - a4);
                if (d < nearest) { c = a4; nearest = d; }
                z = scale * z - c * (scale - 1.0);
                float r2 = dot(z, z);
                if (r2 < trapRadius2) { trapRadius2 = r2; trapIndex = (float)i; }
                trapAxis = min(trapAxis, MinAxis(z));
            }
            trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, length(z));
            return length(z) * pow(max(abs(scale), 1.0001), -float(iterations));

        #elif FRACTAL_KIND == 6

            // Точная знаковая дистанция до объединения касающихся сфер. Иерархия содержит
            // охватывающие сферы: если её нижняя граница дальше уже найденной поверхности,
            // целая ветка пропускается. Ближнего ребёнка посещаем первым.
            float best = 1e20;
            float4 nearestSphere = 0.0;
            int stack[16];
            int top = 0;
            stack[top++] = 0;
            [loop]
            while (top > 0)
            {
                int node = stack[--top];
                float4 bound = SphereTree[node];
                if (bound.w < 0.0) continue;
                float lower = length(p - bound.xyz) - bound.w;
                if (lower > best) continue;
                if (node >= 2047)
                {
                    if (lower < best)
                    {
                        best = lower;
                        nearestSphere = bound;
                    }
                    continue;
                }
                int left = node * 2 + 1;
                int right = left + 1;
                float4 a = SphereTree[left];
                float4 b = SphereTree[right];
                float da = a.w < 0.0 ? 1e20 : length(p - a.xyz) - a.w;
                float db = b.w < 0.0 ? 1e20 : length(p - b.xyz) - b.w;
                if (da < db)
                {
                    if (db <= best) stack[top++] = right;
                    if (da <= best) stack[top++] = left;
                }
                else
                {
                    if (da <= best) stack[top++] = left;
                    if (db <= best) stack[top++] = right;
                }
            }
            float sphereRadius = max(nearestSphere.w, 1e-5);
            trap = float4(sphereRadius * 2.0, MinAxis(nearestSphere.xyz),
                          max(0.0, log2(0.5 / sphereRadius)), sphereRadius);
            return best;

        #elif FRACTAL_KIND == 11 || FRACTAL_KIND == 12

            // x — действительная часть, y и z — две мнимые координаты кватерниона.
            // При z = 0 кватернионная формула повторяет комплексный Burning Ship.
            float power = ShapeA.x;
            int formula = (int)ShapeA.y;
        #if FRACTAL_KIND == 12
            float3 c = ShapeB.xyz;
        #else
            float3 c = p;
        #endif
            float3 z = p;
            float dr = 1.0;
            float r = length(z);
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                trapIndex = (float)i;
                r = length(z);
                trapRadius2 = min(trapRadius2, r * r);
                trapAxis = min(trapAxis, MinAxis(z));
                if (r > ShapeA.w) break;

                // Все отражения сохраняют длину; производная степени ограничена n*r^(n-1).
                dr = power * pow(max(r, 1e-12), power - 1.0) * dr + 1.0;
                float3 folded = float3(abs(z.x), -abs(z.y),
                    (formula == 1 || formula == 3) ? abs(z.z) : z.z);
                float zr = pow(r, power);
                if (formula < 2)
                {
                    // Кватернионная степень: при z=0 это комплексная степень той же формулы.
                    if (power == 2.0)
                        z = float3(folded.x * folded.x - dot(folded.yz, folded.yz),
                            2.0 * folded.x * folded.y, 2.0 * folded.x * folded.z) + c;
                    else
                    {
                        float imaginaryRadius = length(folded.yz);
                        float angle = atan2(imaginaryRadius, folded.x) * power;
                        float imaginaryScale = imaginaryRadius > 1e-8
                            ? zr * sin(angle) / imaginaryRadius : 0.0;
                        z = float3(zr * cos(angle), folded.yz * imaginaryScale) + c;
                    }
                }
                else
                {
                    // Сферическая степень Мандельбульба после тех же отражений.
                    float theta = acos(clamp(folded.z / max(r, 1e-12), -1.0, 1.0));
                    float phi = atan2(folded.y, folded.x);
                    float sinTheta = abs(sin(theta));
                    float stretch = sinTheta > 1e-4
                        ? abs(sin(theta * power)) / sinTheta : power;
                    StepScale /= max(stretch, 1.0);
                    theta *= power;
                    phi *= power;
                    z = zr * float3(sin(theta) * cos(phi), sin(theta) * sin(phi), cos(theta)) + c;
                }
            }
            r = length(z);
            trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, r);
            StepScale = formula < 2 ? 0.5 : max(min(StepScale, 0.5), BULB_STEP_FLOOR);
            return 0.5 * log(max(r, 1.000001)) * r / max(dr, 1e-9);

        #elif FRACTAL_KIND == 5

            float bailout = ShapeA.w;
            float4 z = float4(p, ShapeC.x);
            float4 c = ShapeB;
            float md2 = 1.0;
            float mz2 = dot(z, z);
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                trapIndex = (float)i;
                md2 *= 4.0 * mz2;
                z = QuaternionSquare(z) + c;
                mz2 = dot(z, z);
                trapRadius2 = min(trapRadius2, mz2);
                trapAxis = min(trapAxis, MinAxis(z.xyz));
                if (mz2 > bailout) break;
            }
            trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, sqrt(mz2));
            return 0.25 * sqrt(mz2 / max(md2, 1e-12)) * log(max(mz2, 1.000001));

        #else

            float power = ShapeA.x;
            float bailout = ShapeA.w;
        #if FRACTAL_KIND == 1
            float3 c = ShapeB.xyz;
        #else
            float3 c = p;
        #endif

            float3 z = p;
            float dr = 1.0;
            float r = length(z);
            [loop]
            for (int i = 0; i < iterations; i++)
            {
                trapIndex = (float)i;
                r = length(z);
                // Здесь копится сам радиус, а не его квадрат: до появления палитр цвет
                // Мандельбульба брался как sqrt(минимального радиуса), и корень ниже возвращает
                // ровно эту величину — иначе вид уже сохранённых кадров сместился бы.
                trapRadius2 = min(trapRadius2, r);
                trapAxis = min(trapAxis, MinAxis(z));
                if (r > bailout) break;

                float invR = 1.0 / max(r, 1e-12);
                float theta = acos(clamp(z.z * invR, -1.0, 1.0));
                float phi = atan2(z.y, z.x);
                // Вдоль параллели возведение в степень растягивает сильнее, чем по радиусу:
                // в sin(n·θ)/sin(θ) раз, у полюса почти в n. Оценка ниже этого не учитывает
                // и там завышена, поэтому луч шагает с запасом (см. StepScale).
                float sinTheta0 = abs(sin(theta));
                float stretch = sinTheta0 > 1e-4
                    ? abs(sin(theta * power)) / sinTheta0
                    : abs(power);
                StepScale /= max(stretch, 1.0);
                dr = pow(r, power - 1.0) * power * dr + 1.0;

                float zr = pow(r, power);
                theta *= power;
                phi *= power;
                float sinTheta = sin(theta);
                z = zr * float3(sinTheta * cos(phi), sinTheta * sin(phi), cos(theta)) + c;
            }
            r = length(z);
            trap = float4(sqrt(trapRadius2), trapAxis, trapIndex, r);
            StepScale = max(StepScale, BULB_STEP_FLOOR);
            return 0.5 * log(max(r, 1.000001)) * r / max(dr, 1e-9);

        #endif
        }

        float3 EstimateNormal(float3 p, float epsilon)
        {
            float2 k = float2(1.0, -1.0);
            float4 trap;
            return normalize(
                k.xyy * Map(p + k.xyy * epsilon, trap) +
                k.yyx * Map(p + k.yyx * epsilon, trap) +
                k.yxy * Map(p + k.yxy * epsilon, trap) +
                k.xxx * Map(p + k.xxx * epsilon, trap));
        }

        float SoftShadow(float3 origin, float3 direction, float minDistance, float maxDistance, float sharpness)
        {
        #if FRACTAL_KIND == 10
            float distance;
            int steps;
            float3 axis = normalize(cross(direction, abs(direction.y) < 0.9 ? float3(0,1,0) : float3(1,0,0)));
            float3 other = cross(direction, axis);
            float visibility = 0.0;
            [unroll]
            for (int sampleIndex = 0; sampleIndex < 4; sampleIndex++)
            {
                float angle = sampleIndex * 1.5707963;
                float3 ray = normalize(direction + (cos(angle) * axis + sin(angle) * other) / (sharpness * 4.0));
                visibility += TraceTerrain(origin + ray * minDistance, ray, maxDistance, distance, steps) ? 0.0 : 0.25;
            }
            return visibility;
        #else
            float result = 1.0;
            float travelled = minDistance;
            float4 trap;
            [loop]
            for (int i = 0; i < 48; i++)
            {
                float stepDistance = Map(origin + direction * travelled, trap);
                result = min(result, sharpness * stepDistance / travelled);
                travelled += clamp(stepDistance, minDistance, 0.25);
                if (result < 0.004 || travelled > maxDistance) break;
            }
            return saturate(result);
        #endif
        }

        float Occlusion(float3 p, float3 normal)
        {
            float occluded = 0.0;
            float weight = 1.0;
            float4 trap;
            [unroll]
            for (int i = 0; i < 5; i++)
            {
                float offset = 0.01 + 0.12 * float(i) / 4.0;
                occluded += (offset - Map(p + normal * offset, trap)) * weight;
                weight *= 0.75;
            }
            return saturate(1.0 - 3.0 * occluded);
        }

        // Цвет по положению на градиенте. Повтор «отражать» со смягчением краёв — это ровно та
        // окраска, которая рисовалась до появления палитр, поэтому старые виды не меняются.
        float3 SamplePalette(float value, int repeat)
        {
            int count = clamp((int)PaletteInfo.x, 1, 16);
            bool ring = repeat == 1;

            float t;
            if (ring)
            {
                t = frac(value);
            }
            else if (repeat == 2)
            {
                t = 1.0 - abs(2.0 * frac(value) - 1.0);
                t = t * t * (3.0 - 2.0 * t);
            }
            else
            {
                t = saturate(value);
            }
            t = pow(saturate(t), max(PaletteInfo.w, 1e-3));

            if (count == 1) return Palette[0].rgb;
            if (PaletteInfo.z > 0.5)
            {
                int band = clamp((int)(t * count), 0, count - 1);
                return Palette[band].rgb;
            }
            if (ring)
            {
                float scaled = t * count;
                int low = clamp((int)scaled, 0, count - 1);
                int high = low + 1 == count ? 0 : low + 1;
                return lerp(Palette[low].rgb, Palette[high].rgb, saturate(scaled - (float)low));
            }
            float span = t * (count - 1);
            int first = clamp((int)span, 0, count - 2);
            return lerp(Palette[first].rgb, Palette[first + 1].rgb, saturate(span - (float)first));
        }

        float3 SamplePalette(float value)
        {
            return SamplePalette(value, (int)PaletteInfo.y);
        }

        float3 SkyAt(float3 direction)
        {
            return lerp(BackgroundBottom.rgb, BackgroundTop.rgb, saturate(direction.y * 0.5 + 0.5));
        }

        // Шкала тумана и окраски по глубине: заданная дальность, сжатая, когда камера ближе
        // стартового расстояния. Иначе при приближении к самоподобной фигуре весь кадр
        // съезжал бы к началу палитры, а туман пропадал.
        float DepthSpan()
        {
            float scale = Style.w > 0.0 ? Style.w : 1.0;
            return max(March.z * scale, 1e-6);
        }

        // Цвет поверхности до освещения. Каждый источник приводится к величине порядка единицы,
        // чтобы масштаб и сдвиг окраски означали примерно одно и то же во всех режимах.
        float3 SurfaceAlbedo(
            float3 normal, float4 trap, float travelled, float3 surfacePoint,
            float3 rayDirection, float occlusion, float stepsRatio)
        {
            int mode = (int)Flags.x;
            if (mode == 0) return Surface.rgb;
            if (mode == 1) return abs(normal);

            float value;
            if (mode == 2) value = trap.x;
            else if (mode == 3) value = travelled / DepthSpan() * 6.0;
            else if (mode == 4) value = trap.y * 3.0;
            else if (mode == 5) value = trap.z / max(March.w - 1.0, 1.0);
            else if (mode == 6) value = log(1.0 + trap.w) * 0.5;
        #if FRACTAL_KIND == 10
            else if (mode == 7) value = surfacePoint.y / max(ShapeA.y, 1e-6);
        #else
            else if (mode == 7) value = surfacePoint.y;
        #endif
            else if (mode == 8) value = 1.0 - occlusion;
            else if (mode == 9) value = 1.0 - saturate(dot(normal, -rayDirection));
            else value = stepsRatio;

            return SamplePalette(value * ShapeC.y + ShapeC.z);
        }

        // Зонд возвращает число, а не цвет: байты float укладываются в цель B8G8R8A8_UNorm
        // без потерь (значение n/255 квантуется ровно в n), поэтому ЦП читает их как float.
        float4 PackFloat(float value)
        {
            uint bits = asuint(value);
            return float4((bits >> 16) & 255, (bits >> 8) & 255, bits & 255, (bits >> 24) & 255) / 255.0;
        }

        float3 LinearToSrgb(float3 color)
        {
            color = saturate(color);
            float3 low = color * 12.92;
            float3 high = 1.055 * pow(max(color, 1e-8), 1.0 / 2.4) - 0.055;
            return lerp(low, high, step(0.0031308, color));
        }

        #if FRACTAL_KIND == 0 || FRACTAL_KIND == 11
        float MarkerDistance(float3 origin, float3 direction)
        {
            if (PickerMarker.w <= 0.0) return -1.0;
            float3 relative = origin - PickerMarker.xyz;
            float along = dot(relative, direction);
            float discriminant = along * along - dot(relative, relative) + PickerMarker.w * PickerMarker.w;
            if (discriminant < 0.0) return -1.0;
            float nearSide = -along - sqrt(discriminant);
            return nearSide > 0.0 ? nearSide : -1.0;
        }

        float4 ShadeMarker(float3 origin, float3 direction, float distance)
        {
            float3 normal = normalize(origin + direction * distance - PickerMarker.xyz);
            float3 key = normalize(CameraUp.xyz - CameraRight.xyz * 0.55 - direction * 0.8);
            float diffuse = saturate(dot(normal, key));
            float glint = pow(saturate(dot(normal, normalize(key - direction))), 28.0);
            float rim = pow(1.0 - saturate(dot(normal, -direction)), 3.0);
            float3 green = float3(0.025, 0.55, 0.07) * (0.28 + diffuse * 1.15) +
                           glint * float3(0.7, 1.0, 0.55) * 0.65 + rim * float3(0.03, 0.20, 0.02);
            return float4(LinearToSrgb(green), 1.0);
        }
        #endif

        float4 PSMain(PSInput input) : SV_TARGET
        {
            float2 pixel = input.position.xy + Resolution.zw;
            // По вертикали кадр занимает ровно ±1, поэтому масштаб поля зрения 1/tg(FOV/2)
            // задаёт именно заданный угол обзора.
            float2 plane = (pixel - 0.5 * Resolution.xy) / (0.5 * Resolution.y);
            plane.y = -plane.y;

            float3 rayDirection = normalize(
                CameraForward.xyz * CameraPosition.w + CameraRight.xyz * plane.x + CameraUp.xyz * plane.y);
            float3 rayOrigin = CameraPosition.xyz;

        #if FRACTAL_KIND == 0 || FRACTAL_KIND == 11
            float markerDistance = Probe.x < 0.5 ? MarkerDistance(rayOrigin, rayDirection) : -1.0;
        #endif

            float pixelRadius = 2.0 * March.y / max(Resolution.y * CameraPosition.w, 1.0);
            int maxSteps = (int)March.x;
            float maxDistance = March.z;
            float entryDistance = 0.0;
            float shadeOrigin = 0.0;

            // Начинаем трассировку у области фрактала: оценка расстояния далеко от него
            // может перескочить переднюю поверхность, а фиксированная дальность — обрезать её.
        #if FRACTAL_KIND == 0 || FRACTAL_KIND == 11
            float radius = ShapeA.w;
        #elif FRACTAL_KIND == 1
            float radius = max(ShapeA.w, length(ShapeB.xyz) + 2.0);
        #elif FRACTAL_KIND == 2
            // После кубической свёртки p превращается в -p + delta, |delta| <= 2*sqrt(3)*limit.
            // При scale != 1 за этой сферой первая итерация превышает радиус вылета.
            float foldReach = 1.7320508 * ShapeA.z;
            float radius = max(2.0 * foldReach + 1.0,
                (2.0 * abs(ShapeA.x) * foldReach + sqrt(ShapeA.w)) /
                max(abs(1.0 - ShapeA.x), 0.05));
        #elif FRACTAL_KIND == 3 || FRACTAL_KIND == 4 || FRACTAL_KIND == 8 || FRACTAL_KIND == 9
            float radius = 1.7320508; // Куб [-1, 1]^3 и его вписанный тетраэдр.
        #elif FRACTAL_KIND == 11
            float radius = 2.5; // Для степеней n >= 2 при |c| > 2 орбита уходит.
        #elif FRACTAL_KIND == 12
            float radius = max(ShapeA.w, length(ShapeB.xyz) + 2.0);
        #elif FRACTAL_KIND == 10
            float radius = length(float2(ShapeA.x * 0.707107, ShapeA.y));
        #elif FRACTAL_KIND == 6
            float radius = 1.112373;
        #else
            float radius = max(sqrt(ShapeA.w), length(ShapeB) + 2.0);
        #endif

        #if FRACTAL_KIND == 2
            // При scale ~= 1 множество может не иметь конечной границы.
            if (abs(1.0 - ShapeA.x) >= 0.05)
            {
        #endif
            // Сфера описана вплотную: вершины губки и тетраэдра лежат ровно на ней. Луч же
            // засчитывает попадание, не дойдя до поверхности порога в долю пикселя, да и float
            // округляет — без запаса лучи к вершинам уходили в фон и срезали уголки.
            float bound = radius * 1.001 + 2.0 * pixelRadius * (length(rayOrigin) + radius);
            float closest = -dot(rayOrigin, rayDirection);
            float3 closestPoint = rayOrigin + rayDirection * closest;
            float distanceSquared = dot(closestPoint, closestPoint);
            if (distanceSquared > bound * bound)
            {
            #if FRACTAL_KIND == 0 || FRACTAL_KIND == 11
                if (markerDistance > 0.0) return ShadeMarker(rayOrigin, rayDirection, markerDistance);
            #endif
                return Probe.x > 0.5 ? PackFloat(-1.0) : float4(LinearToSrgb(SkyAt(rayDirection)), 1.0);
            }

            float halfChord = sqrt(max(bound * bound - distanceSquared, 0.0));
            if (closest + halfChord < 0.0)
            {
            #if FRACTAL_KIND == 0 || FRACTAL_KIND == 11
                if (markerDistance > 0.0) return ShadeMarker(rayOrigin, rayDirection, markerDistance);
            #endif
                return Probe.x > 0.5 ? PackFloat(-1.0) : float4(LinearToSrgb(SkyAt(rayDirection)), 1.0);
            }
            entryDistance = max(0.0, closest - halfChord);
            // Туман и окраска по глубине меряются от камеры, пока она не дальше трёх радиусов
            // сферы: это привычный вид. Дальше точка отсчёта едет вслед за камерой, и отъезд
            // не красит фигуру в конец палитры и не топит её в тумане. Сдвиг непрерывен по
            // положению камеры и одинаков для всех лучей кадра, поэтому цвет не скачет.
            shadeOrigin = max(0.0, length(rayOrigin) - 3.0 * radius);
            // Пользовательская дальность остаётся минимумом, но не обрезает фигуру
            // только из-за того, что камера отъехала от неё.
            maxDistance = max(maxDistance, closest + halfChord);
        #if FRACTAL_KIND == 2
            }
        #endif

            int style = (int)Style.x;
            float strength = max(Style.y, 0.0);
            bool volumetric = style == 3 || style == 4;
            // Плотность рисует фигуру насквозь, но зонду по-прежнему нужно первое попадание:
            // от него считается шаг движения камеры.
            bool pierce = style == 4 && Probe.x < 0.5;

            float travelled = entryDistance;
            float4 trap = 0.0;
            float epsilon = 1e-6;
            float proximity = 0.0;
            int usedSteps = 0;
            bool hit = false;
            // Ближайший подход луча к поверхности в долях пикселя: если шаги кончились раньше,
            // чем луч сошёлся или ушёл за дальность, берём эту точку, а не рисуем фон.
            float bestRatio = 1e20;
            float bestTravelled = travelled;
            float4 bestTrap = 0.0;
            bool exhausted = true;

        #if FRACTAL_KIND == 10
            hit = TraceTerrain(rayOrigin, rayDirection, maxDistance, travelled, usedSteps);
            exhausted = false;
            epsilon = max(pixelRadius * travelled * 0.1, 1e-6);
        #else
            [loop]
            for (int i = 0; i < maxSteps; i++)
            {
                float4 stepTrap;
                float3 samplePoint = rayOrigin + rayDirection * travelled;
                float stepDistance = Map(samplePoint, stepTrap);
                epsilon = max(pixelRadius * travelled, 1e-7);
                usedSteps = i + 1;
                float ratio = stepDistance / epsilon;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    bestTravelled = travelled;
                    bestTrap = stepTrap;
                }
                // Во второй половине лимита запас плавно снимается: иначе луч, ползущий вдоль
                // поверхности у полюса, тратил бы все шаги и оставлял на её месте чёрную щель.
                float relax = saturate(2.0 * (float)i / max((float)maxSteps, 1.0) - 1.0);
                float safeStep = max(stepDistance * lerp(StepScale, 1.0, relax), epsilon);
                float advance = stepDistance < epsilon ? max(epsilon * 2.0, stepDistance) : safeStep;
                // Близость копится с весом пройденного пути, а не по числу шагов: иначе луч,
                // застрявший у поверхности, за пару кадров насыщал бы яркость до белого.
                if (volumetric) proximity += exp(-abs(stepDistance) * 22.0) * advance;
                if (stepDistance < epsilon && !pierce)
                {
                    trap = stepTrap;
                    hit = true;
                    break;
                }
                travelled += advance;
                if (travelled > maxDistance)
                {
                    exhausted = false;
                    break;
                }
            }

        #endif

            // Касательный луч и луч в узкой щели тратят все шаги у самой поверхности.
            // Фон на их месте выглядел бы чёрной дырой, поэтому, если луч подходил к поверхности
            // ближе нескольких пикселей, считаем попаданием ближайшую точку (Enhanced Sphere Tracing).
            if (!hit && !pierce && exhausted && bestRatio < 16.0)
            {
                hit = true;
                travelled = bestTravelled;
                trap = bestTrap;
            }

            if (Probe.x > 0.5) return PackFloat(hit ? travelled : -1.0);

        #if FRACTAL_KIND == 0 || FRACTAL_KIND == 11
            if (markerDistance > 0.0 && (!hit || markerDistance < travelled))
                return ShadeMarker(rayOrigin, rayDirection, markerDistance);
        #endif

            float3 sky = SkyAt(rayDirection);
        #if FRACTAL_KIND == 10
            float stepsRatio = saturate((float)usedSteps / max(2.0 * (ShapeA.z - 1.0), 1.0));
        #else
            float stepsRatio = saturate((float)usedSteps / max((float)maxSteps, 1.0));
        #endif
            // Туман, глубина и накопленная близость нормируются на заданную дальность, а не на
            // рабочий предел луча: тот растёт при отъезде камеры, и фигура меняла бы цвет от
            // одного лишь расстояния до неё.
            float traceSpan = max(March.z, 1e-3);
            float shadeDepth = travelled - shadeOrigin;

            if (style == 4)
            {
                // Накопленный путь соотносится с заданной дальностью, иначе крупная фигура
                // (Мандельбокс стоит в сотне единиц) насыщала бы плотность до сплошного пятна.
                float density = saturate(proximity / traceSpan * 12.0 * max(strength, 1e-3));
                float3 tint = SamplePalette(density * ShapeC.y + ShapeC.z, 0);
                return float4(LinearToSrgb(lerp(sky, tint, density)), 1.0);
            }

            float glow = style == 3
                ? saturate(proximity / traceSpan * 10.0 * strength)
                : 0.0;
            float3 glowTint = style == 3 ? SamplePalette(glow * ShapeC.y + ShapeC.z, 0) : 0.0;

            if (!hit) return float4(LinearToSrgb(sky + glowTint * glow), 1.0);

            float3 surfacePoint = rayOrigin + rayDirection * travelled;
        #if FRACTAL_KIND == 10
            float2 terrainSlope;
            TerrainHeight(surfacePoint.xz, terrainSlope);
            float3 normal = normalize(float3(-terrainSlope.x, 1.0, -terrainSlope.y));
            if (dot(normal, rayDirection) > 0) normal = -normal;
        #else
            float3 normal = EstimateNormal(surfacePoint, max(epsilon, 1e-6));
        #endif
            float3 lightDirection = Light.xyz;
            float3 lightTint = LightColor.rgb;

            float diffuse = saturate(dot(normal, lightDirection));
            float shadow = 1.0;
            if (Flags.y > 0.0 && diffuse > 0.0)
            {
                shadow = SoftShadow(surfacePoint + normal * epsilon * 4.0, lightDirection,
                    max(epsilon * 8.0, 1e-5), maxDistance * 0.5, Flags.y);
            }

            float occlusion = 1.0;
            if (Flags.z > 0.5) occlusion = lerp(1.0, Occlusion(surfacePoint, normal), saturate(Surface.a));

            // Фоновый свет либо остаётся нейтральным, либо забирает цвет неба над точкой.
            float3 ambientTint = lerp(float3(1.0, 1.0, 1.0), SkyAt(normal) * 3.0, saturate(Style.z));
            float3 ambient = Flags.w * ambientTint;
            float3 albedo = SurfaceAlbedo(
                normal, trap, shadeDepth, surfacePoint, rayDirection, occlusion, stepsRatio);
            float3 halfVector = normalize(lightDirection - rayDirection);
            float specular = Light.w * pow(saturate(dot(normal, halfVector)), 32.0) * shadow;

            float3 color;
            if (style == 1)
            {
                // Глина: свет обёрнут вокруг фигуры, блика нет, складки затенены сильнее.
                float wrapped = saturate(dot(normal, lightDirection) * 0.5 + 0.5);
                color = albedo * (ambient * 0.8 + wrapped * shadow * lightTint) * occlusion * occlusion;
            }
            else if (style == 2)
            {
                // Металл: в поверхности отражается небо, поверх него — жёсткий блик от лампы.
                float3 reflected = reflect(rayDirection, normal);
                float fresnel = pow(1.0 - saturate(dot(normal, -rayDirection)), 5.0);
                float sheen = saturate(dot(reflected, lightDirection));
                float3 environment = SkyAt(reflected) * 2.0 +
                    lightTint * (pow(sheen, 128.0) * 5.0 + pow(sheen, 6.0) * 0.45);
                color = albedo * (ambient + diffuse * shadow * lightTint * 0.25) * occlusion * 0.6 +
                    albedo * environment * lerp(0.35, 1.0, fresnel) * shadow * strength;
            }
            else if (style == 5)
            {
                // Студийный свет: освещение берётся от нормали в осях камеры, поэтому фигура
                // одинаково читается с любой стороны и не зависит от положения лампы.
                float2 screenNormal = float2(dot(normal, CameraRight.xyz), dot(normal, CameraUp.xyz));
                float key = saturate(dot(screenNormal, normalize(float2(-0.55, 0.62))) * 0.6 + 0.55);
                float fill = saturate(dot(screenNormal, normalize(float2(0.7, -0.3))) * 0.5 + 0.5) * 0.25;
                float rim = pow(saturate(length(screenNormal)), 6.0) * 0.55 * strength;
                color = albedo * (key + fill + Flags.w * 0.3) * occlusion + rim * lightTint;
            }
            else if (style == 6)
            {
                // Контурный: свет квантуется ступенями, силуэт обводится тёмной каймой.
                float levels = max(2.0, floor(2.0 + strength * 2.0));
                float lit = floor(saturate(diffuse * shadow) * levels) / max(levels - 1.0, 1.0);
                float edge = 1.0 - smoothstep(0.12, 0.42, saturate(dot(normal, -rayDirection)));
                color = albedo * (ambient + lit * lightTint) * occlusion * (1.0 - edge);
            }
            else if (style == 7)
            {
                // Просвечивание: свет, пришедший с изнанки, тем заметнее, чем тоньше место.
                float thickness = pow(saturate(occlusion), 3.0);
                float back = pow(saturate(dot(-normal, lightDirection)) * 0.6 + 0.4, 2.0);
                color = albedo * (ambient + diffuse * shadow * lightTint * 0.6) * occlusion +
                    albedo * back * thickness * strength * lightTint * 2.5 + specular * lightTint;
            }
            else
            {
                color = albedo * (ambient + diffuse * shadow * lightTint) * occlusion + specular * lightTint;
            }

            if (style == 3) color += glowTint * glow * 1.2;
            color = lerp(color, sky, saturate(shadeDepth / DepthSpan()));
            return float4(LinearToSrgb(color), 1.0);
        }
        """;
}
