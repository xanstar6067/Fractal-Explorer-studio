using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Орбитальная камера: положение задаётся азимутом, наклоном и расстоянием до цели.</summary>
public readonly record struct Fractal3DCameraBasis(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Right,
    Vector3 Up,
    float FieldOfViewScale);

/// <summary>Положение орбитальной камеры, которое меняет навигация окна.</summary>
public readonly record struct Fractal3DOrbit(double Yaw, double Pitch, double Distance, Vector3 Target);

/// <summary>
/// Недоеханный остаток шага колеса: во сколько раз ещё должно измениться расстояние и куда ещё
/// доехать точке наблюдения. Щелчок колеса не двигает камеру сам, а задаёт цель, к которой она
/// идёт несколько кадров — поэтому зум плавный, а не ступеньками. Остаток хранится приращением,
/// поэтому одновременные вращение и перетаскивание ему не мешают, а новые щелчки складываются с
/// недоеханными.
/// </summary>
public sealed class Fractal3DZoomGlide
{
    /// <summary>Скорость догона цели, 1/с: за 0.2 с остаётся около 6 % пути.</summary>
    public const double Rate = 14;

    /// <summary>Остаток мельче этого (в долях расстояния) считается доеханным.</summary>
    public const double Epsilon = 1e-4;

    /// <summary>Расстояние, к которому идёт камера; <c>null</c> — колесо его не трогало.</summary>
    public double? Aimed { get; private set; }

    /// <summary>Сколько ещё проехать точке наблюдения. Хранится приращением, а не целью, чтобы
    /// одновременные вращение и перетаскивание не спорили с колесом.</summary>
    public Vector3 Shift { get; private set; }

    /// <summary>Осталось ли что-то доехать вообще — пусть даже последний незаметный волосок.</summary>
    public bool HasRemainder => Aimed.HasValue || Shift != Vector3.Zero;

    /// <summary>Стоит ли ради остатка продолжать считать кадры.</summary>
    public bool IsActive(double distance)
    {
        double current = Math.Max(distance, Fractal3DCamera.MinDistance);
        return (Aimed is { } aim && Math.Abs(Math.Log(aim / current)) > Epsilon) ||
               Shift.Length() > current * Epsilon;
    }

    public void Clear()
    {
        Aimed = null;
        Shift = Vector3.Zero;
    }

    /// <summary>Куда приедет камера, если больше ничего не крутить.</summary>
    public double PlannedDistance(double distance) =>
        Aimed ?? Math.Clamp(distance, Fractal3DCamera.MinDistance, Fractal3DCamera.MaxDistance);

    public Vector3 PlannedTarget(Vector3 target) => target + Shift;

    /// <summary>Назначить новую цель: расстояние абсолютно, сдвиг точки наблюдения — приращением.</summary>
    public void Aim(double distance, Vector3 shift)
    {
        Aimed = Math.Clamp(distance, Fractal3DCamera.MinDistance, Fractal3DCamera.MaxDistance);
        Shift = shift;
    }

    /// <summary>Добавить к остатку ещё один сдвиг точки наблюдения.</summary>
    public void Push(Vector3 shift) => Shift += shift;

    /// <summary>
    /// Доехать часть оставшегося пути за прошедшее время: расстояние — геометрически (на глаз
    /// приближение идёт равномерно), точка наблюдения — по прямой.
    /// </summary>
    public Fractal3DOrbit Advance(Fractal3DOrbit orbit, double seconds)
    {
        double fraction = Math.Clamp(1 - Math.Exp(-Rate * seconds), 0, 1);
        double distance = orbit.Distance;
        if (Aimed is { } aim)
        {
            double current = Math.Max(distance, Fractal3DCamera.MinDistance);
            distance = current * Math.Pow(aim / current, fraction);
        }

        Vector3 moved = Shift * (float)fraction;
        Shift -= moved;
        return orbit with { Distance = distance, Target = orbit.Target + moved };
    }

    /// <summary>
    /// Доехать остаток разом. Геометрическое приближение приходит в цель только в пределе;
    /// последний незаметный волосок отдаётся целиком, чтобы камера встала ровно там, куда её
    /// послало колесо, и расстояние в поле не оставалось «почти круглым».
    /// </summary>
    public Fractal3DOrbit Finish(Fractal3DOrbit orbit)
    {
        Fractal3DOrbit arrived = orbit with
        {
            Distance = PlannedDistance(orbit.Distance),
            Target = PlannedTarget(orbit.Target)
        };
        Clear();
        return arrived;
    }
}

public static class Fractal3DCamera
{
    public const double MinPitch = -89.9;
    public const double MaxPitch = 89.9;
    public const double MinDistance = 1e-4;
    public const double MaxDistance = 1e5;

    /// <summary>Единичный вектор от точки наблюдения к камере по азимуту и наклону.</summary>
    public static Vector3 Direction(double yaw, double pitch)
    {
        double yawRadians = yaw * Math.PI / 180;
        double pitchRadians = Math.Clamp(pitch, MinPitch, MaxPitch) * Math.PI / 180;
        return new Vector3(
            (float)(Math.Cos(pitchRadians) * Math.Sin(yawRadians)),
            (float)Math.Sin(pitchRadians),
            (float)(Math.Cos(pitchRadians) * Math.Cos(yawRadians)));
    }

    /// <summary>Азимут и наклон, дающие это направление: обратная к <see cref="Direction"/>.</summary>
    public static (double Yaw, double Pitch) Angles(Vector3 direction)
    {
        Vector3 unit = direction.LengthSquared() > 1e-24f ? Vector3.Normalize(direction) : Vector3.UnitZ;
        return (Math.Atan2(unit.X, unit.Z) * 180 / Math.PI,
            Math.Clamp(Math.Asin(Math.Clamp(unit.Y, -1, 1)) * 180 / Math.PI, MinPitch, MaxPitch));
    }

    /// <summary>Положение камеры для состояния.</summary>
    public static Vector3 Position(Fractal3DState state) =>
        Target(state) + Direction(state.CameraYaw, state.CameraPitch) *
        (float)Math.Max(state.CameraDistance, MinDistance);

    public static Vector3 Target(Fractal3DState state) =>
        new((float)state.TargetX, (float)state.TargetY, (float)state.TargetZ);

    public static Vector3 Position(Fractal3DOrbit orbit) =>
        orbit.Target + Direction(orbit.Yaw, orbit.Pitch) * (float)Math.Max(orbit.Distance, MinDistance);

    /// <summary>
    /// Поворот камеры на углы в мере перетаскивания мыши. С якорем в точке наблюдения камера
    /// облетает фрактал, а игровая камера остаётся на месте и поворачивает взгляд — поэтому вперёд
    /// по новому направлению переносится сама точка наблюдения.
    /// </summary>
    public static Fractal3DOrbit Rotate(
        Fractal3DOrbit orbit, Fractal3DRotationAnchor anchor, double dragYaw, double dragPitch)
    {
        if (anchor != Fractal3DRotationAnchor.FreeLook)
        {
            return orbit with
            {
                Yaw = orbit.Yaw - dragYaw,
                Pitch = Math.Clamp(orbit.Pitch + dragPitch, MinPitch, MaxPitch)
            };
        }

        Vector3 position = Position(orbit);
        double yaw = orbit.Yaw + dragYaw;
        double pitch = Math.Clamp(orbit.Pitch - dragPitch, MinPitch, MaxPitch);
        return orbit with
        {
            Yaw = yaw,
            Pitch = pitch,
            Target = position - Direction(yaw, pitch) * (float)Math.Max(orbit.Distance, MinDistance)
        };
    }

    /// <summary>
    /// Направление луча через пиксель кадра — та же формула, что в пиксельном шейдере, поэтому
    /// расстояние, измеренное зондом, откладывается ровно по этому лучу.
    /// </summary>
    public static Vector3 PixelRay(
        Fractal3DState state, double pixelX, double pixelY, double width, double height)
    {
        Fractal3DCameraBasis basis = Build(state);
        double half = 0.5 * Math.Max(height, 1);
        double planeX = (pixelX - 0.5 * width) / half;
        double planeY = -(pixelY - 0.5 * height) / half;
        return Vector3.Normalize(basis.Forward * basis.FieldOfViewScale +
            basis.Right * (float)planeX + basis.Up * (float)planeY);
    }

    public static Fractal3DCameraBasis Build(Fractal3DState state)
    {
        Vector3 target = Target(state);
        Vector3 direction = Direction(state.CameraYaw, state.CameraPitch);
        Vector3 position = target + direction * (float)Math.Max(state.CameraDistance, MinDistance);
        Vector3 forward = Vector3.Normalize(target - position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Cross(right, forward);
        float fieldOfViewScale = (float)(1 / Math.Tan(Math.Clamp(state.FieldOfView, 5, 160) * Math.PI / 360));

        return new Fractal3DCameraBasis(position, forward, right, up, fieldOfViewScale);
    }

    /// <summary>Направление на источник света по его азимуту и высоте.</summary>
    public static Vector3 LightDirection(Fractal3DState state)
    {
        double yaw = state.LightYaw * Math.PI / 180;
        double pitch = Math.Clamp(state.LightPitch, MinPitch, MaxPitch) * Math.PI / 180;
        return Vector3.Normalize(new Vector3(
            (float)(Math.Cos(pitch) * Math.Sin(yaw)),
            (float)Math.Sin(pitch),
            (float)(Math.Cos(pitch) * Math.Cos(yaw))));
    }

    /// <summary>Сколько мировых единиц приходится на пиксель в плоскости цели — шаг панорамирования.</summary>
    public static double WorldUnitsPerPixel(Fractal3DState state, double viewportHeight) =>
        2 * Math.Max(state.CameraDistance, MinDistance) *
        Math.Tan(Math.Clamp(state.FieldOfView, 5, 160) * Math.PI / 360) / Math.Max(viewportHeight, 1);
}
