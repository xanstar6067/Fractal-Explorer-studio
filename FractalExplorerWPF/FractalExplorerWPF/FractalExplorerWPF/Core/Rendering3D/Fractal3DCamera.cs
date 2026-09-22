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

public static class Fractal3DCamera
{
    public const double MinPitch = -89.9;
    public const double MaxPitch = 89.9;
    public const double MinDistance = 1e-4;

    public static Fractal3DCameraBasis Build(Fractal3DState state)
    {
        double yaw = state.CameraYaw * Math.PI / 180;
        double pitch = Math.Clamp(state.CameraPitch, MinPitch, MaxPitch) * Math.PI / 180;

        var target = new Vector3((float)state.TargetX, (float)state.TargetY, (float)state.TargetZ);
        var direction = new Vector3(
            (float)(Math.Cos(pitch) * Math.Sin(yaw)),
            (float)Math.Sin(pitch),
            (float)(Math.Cos(pitch) * Math.Cos(yaw)));

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
