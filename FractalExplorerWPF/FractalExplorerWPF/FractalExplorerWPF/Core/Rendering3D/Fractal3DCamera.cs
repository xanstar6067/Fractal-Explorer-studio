using System.Numerics;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Базис камеры, по которому шейдер строит лучи.</summary>
public readonly record struct Fractal3DCameraBasis(
    Vector3 Position,
    Vector3 Forward,
    Vector3 Right,
    Vector3 Up,
    float FieldOfViewScale);

/// <summary>
/// Положение камеры, которое меняет навигация окна: поворот, точка наблюдения и расстояние до неё.
/// Сама камера стоит в <see cref="Position"/>, на <see cref="Distance"/> позади цели. Поворот
/// хранится кватернионом, поэтому у камеры нет выделенного «верха»: её можно перевернуть через
/// полюс и накренить.
/// </summary>
public readonly record struct Fractal3DPose(Quaternion Orientation, double Distance, Vector3 Target)
{
    /// <summary>Направление взгляда.</summary>
    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Orientation);

    /// <summary>Вправо по экрану.</summary>
    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Orientation);

    /// <summary>Вверх по экрану.</summary>
    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Orientation);

    public Vector3 Position => Target - Forward * (float)Math.Max(Distance, Fractal3DCamera.MinDistance);
}

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
    public Fractal3DPose Advance(Fractal3DPose orbit, double seconds)
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
    public Fractal3DPose Finish(Fractal3DPose orbit)
    {
        Fractal3DPose arrived = orbit with
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
    /// <summary>Пределы высоты источника света. Камера наклоном не ограничена.</summary>
    public const double MinPitch = -89.9;
    public const double MaxPitch = 89.9;
    public const double MinDistance = 1e-4;
    public const double MaxDistance = 1e5;

    private const double Degrees = 180 / Math.PI;

    /// <summary>
    /// Единичный вектор от точки наблюдения к камере по азимуту и наклону — задний вектор
    /// поворота <see cref="Orientation(double, double, double)"/> при любом крене.
    /// </summary>
    public static Vector3 Direction(double yaw, double pitch)
    {
        double yawRadians = yaw / Degrees;
        double pitchRadians = Math.Clamp(pitch, -90, 90) / Degrees;
        return new Vector3(
            (float)(Math.Cos(pitchRadians) * Math.Sin(yawRadians)),
            (float)Math.Sin(pitchRadians),
            (float)(Math.Cos(pitchRadians) * Math.Cos(yawRadians)));
    }

    /// <summary>
    /// Поворот камеры по азимуту, наклону и крену. Камера без поворота смотрит вдоль −Z, вправо у
    /// неё +X, вверх +Y. Сначала крен вокруг оси взгляда, затем наклон (положительный — камера над
    /// целью и смотрит вниз), затем азимут вокруг мировой вертикали. С нулевым креном это ровно
    /// прежняя орбитальная камера.
    /// </summary>
    public static Quaternion Orientation(double yaw, double pitch, double roll)
    {
        Quaternion rollRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(roll / Degrees));
        Quaternion pitchRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)(-pitch / Degrees));
        Quaternion yawRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)(yaw / Degrees));
        return Quaternion.Normalize(
            Quaternion.Concatenate(Quaternion.Concatenate(rollRotation, pitchRotation), yawRotation));
    }

    public static Quaternion Orientation(Fractal3DState state) =>
        Orientation(state.CameraYaw, state.CameraPitch, state.CameraRoll);

    /// <summary>
    /// Азимут, наклон и крен поворота — обратная к <see cref="Orientation(double, double, double)"/>.
    /// Наклон лежит в [−90°; 90°]: камера, перевёрнутая через полюс, описывается азимутом на 180°
    /// дальше и креном около 180°. Прямо над полюсом азимут неоднозначен, и тогда весь поворот
    /// вокруг оси взгляда отдаётся ему.
    /// </summary>
    public static (double Yaw, double Pitch, double Roll) Angles(Quaternion orientation)
    {
        Vector3 back = Vector3.Transform(Vector3.UnitZ, orientation);
        Vector3 up = Vector3.Transform(Vector3.UnitY, orientation);
        Vector3 right = Vector3.Transform(Vector3.UnitX, orientation);

        double pitch = Math.Asin(Math.Clamp((double)back.Y, -1, 1));
        double horizontal = Math.Sqrt((double)back.X * back.X + (double)back.Z * back.Z);
        if (horizontal < 1e-6)
            return (Math.Atan2(-right.Z, right.X) * Degrees, Math.Sign(back.Y) * 90.0, 0);

        double yaw = Math.Atan2(back.X, back.Z);
        double sinPitch = Math.Sin(pitch), cosPitch = Math.Cos(pitch);
        double sinYaw = Math.Sin(yaw), cosYaw = Math.Cos(yaw);
        // Право и верх той же камеры без крена; крен — угол между ними и настоящими.
        double upAlongRight = up.X * cosYaw - up.Z * sinYaw;
        double upAlongUp = -up.X * sinPitch * sinYaw + up.Y * cosPitch - up.Z * sinPitch * cosYaw;
        double roll = Math.Atan2(-upAlongRight, upAlongUp);
        return (yaw * Degrees, pitch * Degrees, roll * Degrees);
    }

    /// <summary>Азимут и наклон направления «от цели к камере»: обратная к <see cref="Direction"/>.</summary>
    public static (double Yaw, double Pitch) Angles(Vector3 direction)
    {
        Vector3 unit = direction.LengthSquared() > 1e-24f ? Vector3.Normalize(direction) : Vector3.UnitZ;
        return (Math.Atan2(unit.X, unit.Z) * Degrees, Math.Asin(Math.Clamp(unit.Y, -1, 1)) * Degrees);
    }

    public static Fractal3DPose Pose(Fractal3DState state) =>
        new(Orientation(state), Math.Max(state.CameraDistance, MinDistance), Target(state));

    /// <summary>Записать положение камеры в состояние.</summary>
    public static void Apply(Fractal3DPose pose, Fractal3DState state)
    {
        (state.CameraYaw, state.CameraPitch, state.CameraRoll) = Angles(pose.Orientation);
        state.CameraDistance = pose.Distance;
        state.TargetX = pose.Target.X;
        state.TargetY = pose.Target.Y;
        state.TargetZ = pose.Target.Z;
    }

    /// <summary>Положение камеры для состояния.</summary>
    public static Vector3 Position(Fractal3DState state) => Pose(state).Position;

    public static Vector3 Target(Fractal3DState state) =>
        new((float)state.TargetX, (float)state.TargetY, (float)state.TargetZ);

    /// <summary>
    /// Повернуть камеру вместе с точкой наблюдения вокруг неподвижной точки на поворот, заданный
    /// в мировых осях.
    /// </summary>
    public static Fractal3DPose RotateAround(Fractal3DPose pose, Vector3 pivot, Quaternion rotation) => pose with
    {
        Orientation = Quaternion.Normalize(Quaternion.Concatenate(pose.Orientation, rotation)),
        Target = pivot + Vector3.Transform(pose.Target - pivot, rotation)
    };

    /// <summary>
    /// Трекбол, как в CAD: фрактал поворачивается вокруг опорной точки вслед за мышью — тянете
    /// вправо, и ближняя сторона уходит вправо; тянете вниз — вниз. Оси берутся экранные, а не
    /// мировые, поэтому через полюс камера проходит без упора.
    /// </summary>
    public static Fractal3DPose Orbit(Fractal3DPose pose, Vector3 pivot, double dragX, double dragY)
    {
        Quaternion rotation = Quaternion.Concatenate(
            Quaternion.CreateFromAxisAngle(pose.Up, (float)(-dragX / Degrees)),
            Quaternion.CreateFromAxisAngle(pose.Right, (float)(-dragY / Degrees)));
        return RotateAround(pose, pivot, rotation);
    }

    /// <summary>
    /// Поворот взгляда на месте: камера стоит, а картинка идёт за мышью так же, как при
    /// <see cref="Orbit"/>, — тянете вправо, и взгляд уходит влево.
    /// </summary>
    public static Fractal3DPose Look(Fractal3DPose pose, double dragX, double dragY)
    {
        Quaternion rotation = Quaternion.Concatenate(
            Quaternion.CreateFromAxisAngle(pose.Up, (float)(dragX / Degrees)),
            Quaternion.CreateFromAxisAngle(pose.Right, (float)(dragY / Degrees)));
        return RotateAround(pose, pose.Position, rotation);
    }

    /// <summary>Крен вокруг оси взгляда; положительный угол поворачивает картинку по часовой стрелке.</summary>
    public static Fractal3DPose Roll(Fractal3DPose pose, double angle) =>
        RotateAround(pose, pose.Position, Quaternion.CreateFromAxisAngle(pose.Forward, (float)(-angle / Degrees)));

    /// <summary>
    /// Тот же взгляд без крена: верх экрана снова смотрит в сторону мирового верха. Перевёрнутую
    /// через полюс камеру это поворачивает на 180° вокруг оси взгляда.
    /// </summary>
    public static Quaternion Level(Quaternion orientation)
    {
        (double yaw, double pitch, _) = Angles(orientation);
        return Orientation(yaw, pitch, 0);
    }

    /// <summary>Кратчайший поворот камеры, после которого она смотрит в заданном направлении.</summary>
    public static Quaternion TurnToward(Quaternion orientation, Vector3 direction)
    {
        Vector3 forward = Vector3.Transform(-Vector3.UnitZ, orientation);
        Vector3 to = Vector3.Normalize(direction);
        float cos = Math.Clamp(Vector3.Dot(forward, to), -1f, 1f);
        Vector3 axis = Vector3.Cross(forward, to);
        if (axis.LengthSquared() < 1e-12f)
        {
            if (cos > 0) return orientation;
            axis = Vector3.Transform(Vector3.UnitY, orientation);
        }
        Quaternion rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(cos));
        return Quaternion.Normalize(Quaternion.Concatenate(orientation, rotation));
    }

    /// <summary>
    /// Направление луча через пиксель кадра — та же формула, что в пиксельном шейдере, поэтому
    /// расстояние, измеренное зондом, откладывается ровно по этому лучу.
    /// </summary>
    public static Vector3 PixelRay(
        Fractal3DState state, double pixelX, double pixelY, double width, double height) =>
        PixelRay(Pose(state), state.FieldOfView, pixelX, pixelY, width, height);

    public static Vector3 PixelRay(
        Fractal3DPose pose, double fieldOfView, double pixelX, double pixelY, double width, double height)
    {
        double half = 0.5 * Math.Max(height, 1);
        double planeX = (pixelX - 0.5 * width) / half;
        double planeY = -(pixelY - 0.5 * height) / half;
        return Vector3.Normalize(pose.Forward * FieldOfViewScale(fieldOfView) +
            pose.Right * (float)planeX + pose.Up * (float)planeY);
    }

    /// <summary>Где на кадре окажется точка перед камерой — обратная к <c>PixelRay</c>.</summary>
    public static (double X, double Y) Project(
        Fractal3DPose pose, double fieldOfView, Vector3 point, double width, double height)
    {
        Vector3 offset = point - pose.Position;
        double depth = Vector3.Dot(offset, pose.Forward) / FieldOfViewScale(fieldOfView);
        double half = 0.5 * Math.Max(height, 1);
        return (0.5 * width + Vector3.Dot(offset, pose.Right) / depth * half,
            0.5 * height - Vector3.Dot(offset, pose.Up) / depth * half);
    }

    public static Fractal3DCameraBasis Build(Fractal3DState state)
    {
        Fractal3DPose pose = Pose(state);
        return new Fractal3DCameraBasis(pose.Position, pose.Forward, pose.Right, pose.Up,
            FieldOfViewScale(state.FieldOfView));
    }

    private static float FieldOfViewScale(double fieldOfView) =>
        (float)(1 / Math.Tan(Math.Clamp(fieldOfView, 5, 160) * Math.PI / 360));

    /// <summary>Направление на источник света по его азимуту и высоте.</summary>
    public static Vector3 LightDirection(Fractal3DState state)
    {
        double yaw = state.LightYaw / Degrees;
        double pitch = Math.Clamp(state.LightPitch, MinPitch, MaxPitch) / Degrees;
        return Vector3.Normalize(new Vector3(
            (float)(Math.Cos(pitch) * Math.Sin(yaw)),
            (float)Math.Sin(pitch),
            (float)(Math.Cos(pitch) * Math.Cos(yaw))));
    }

    /// <summary>Сколько мировых единиц приходится на пиксель в плоскости цели.</summary>
    public static double WorldUnitsPerPixel(Fractal3DState state, double viewportHeight) =>
        WorldUnitsPerPixel(state.CameraDistance, state.FieldOfView, viewportHeight);

    /// <summary>Сколько мировых единиц приходится на пиксель на заданной глубине — шаг панорамирования.</summary>
    public static double WorldUnitsPerPixel(double depth, double fieldOfView, double viewportHeight) =>
        2 * Math.Max(depth, MinDistance) *
        Math.Tan(Math.Clamp(fieldOfView, 5, 160) * Math.PI / 360) / Math.Max(viewportHeight, 1);
}
