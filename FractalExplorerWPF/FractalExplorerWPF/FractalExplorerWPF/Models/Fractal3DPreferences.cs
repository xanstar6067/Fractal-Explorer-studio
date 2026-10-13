namespace FractalExplorerWPF.Models;

/// <summary>
/// Общие предпочтения 3D-окон. Отсутствующее значение оставляет настройку готового вида:
/// до первого изменения пользователем стартовые параметры каждого режима сохраняются.
/// </summary>
public sealed class Fractal3DPreferences
{
    public Fractal3DNavigationMode? NavigationMode { get; set; }
    public double? GameMovementSpeed { get; set; }
    public double? GameMouseSensitivity { get; set; }
    public Fractal3DMotionQuality? MotionQuality { get; set; }
    public Fractal3DMotionResolution? MotionResolution { get; set; }
    public bool? RotationInertia { get; set; }
    public bool? AutoRotate { get; set; }
    public double? AutoRotateSpeed { get; set; }
    public int? MaxSteps { get; set; }
    public double? Detail { get; set; }
    public double? MaxDistance { get; set; }
    public int? Ssaa { get; set; }
    public bool? SoftShadows { get; set; }
    public double? ShadowSharpness { get; set; }
    public bool? AmbientOcclusion { get; set; }
    public double? AoStrength { get; set; }

    /// <summary>Накладывает допустимые предпочтения на копию, не меняя библиотеку готовых видов.</summary>
    public Fractal3DState ApplyTo(Fractal3DState source)
    {
        Fractal3DState state = source.Clone();
        if (NavigationMode is { } navigation && Enum.IsDefined(navigation)) state.NavigationMode = navigation;
        if (GameMovementSpeed is >= 0.1 and <= 5) state.GameMovementSpeed = GameMovementSpeed.Value;
        if (GameMouseSensitivity is >= 0.1 and <= 3) state.GameMouseSensitivity = GameMouseSensitivity.Value;
        if (MotionQuality is { } quality && Enum.IsDefined(quality)) state.MotionQuality = quality;
        if (MotionResolution is { } resolution && Enum.IsDefined(resolution)) state.MotionResolution = resolution;
        if (RotationInertia is { } inertia) state.RotationInertia = inertia;
        if (AutoRotate is { } autoRotate) state.AutoRotate = autoRotate;
        if (AutoRotateSpeed is >= -720 and <= 720) state.AutoRotateSpeed = AutoRotateSpeed.Value;
        if (MaxSteps is >= 16 and <= 1024) state.MaxSteps = MaxSteps.Value;
        if (Detail is >= 0.05 and <= 8) state.Detail = Detail.Value;
        if (MaxDistance is >= 1 and <= 1000) state.MaxDistance = MaxDistance.Value;
        if (Ssaa is 1 or 2 or 4) state.Ssaa = Ssaa.Value;
        if (SoftShadows is { } shadows) state.SoftShadows = shadows;
        if (ShadowSharpness is >= 1 and <= 128) state.ShadowSharpness = ShadowSharpness.Value;
        if (AmbientOcclusion is { } ao) state.AmbientOcclusion = ao;
        if (AoStrength is >= 0 and <= 1) state.AoStrength = AoStrength.Value;
        return state;
    }
}
