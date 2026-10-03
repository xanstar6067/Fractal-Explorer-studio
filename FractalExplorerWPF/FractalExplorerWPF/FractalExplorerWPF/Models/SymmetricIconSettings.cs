namespace FractalExplorerWPF.Models;

public sealed class SymmetricIconSettings
{
    public int Degree { get; set; } = 3;
    public double Lambda { get; set; } = 1.56;
    public double Alpha { get; set; } = -1;
    public double Beta { get; set; } = .1;
    public double Gamma { get; set; } = -.82;
    public double Omega { get; set; } = .1;
    public bool Mirror { get; set; } = true;
    public double Rotation { get; set; }
    public double Span { get; set; } = 3.2;
    public int Seed { get; set; } = 1;

    public SymmetricIconSettings Clone() => (SymmetricIconSettings)MemberwiseClone();

    public void Validate()
    {
        if (Degree is < 3 or > 16)
            throw new InvalidOperationException("Число лучей должно быть от 3 до 16.");
        if (new[] { Lambda, Alpha, Beta, Gamma, Omega, Rotation, Span }.Any(v => !double.IsFinite(v)) || Span <= 0)
            throw new InvalidOperationException("Параметры орнамента должны быть конечными, размер кадра — положительным.");
    }
}

public sealed record SymmetricIconPreset(string Name, SymmetricIconSettings Settings, string PaletteName)
{
    public override string ToString() => Name;
}

public static class SymmetricIconPresets
{
    // Field–Golubitsky polynomial icons. Published parameter sets:
    // https://github.com/holoviz-topics/examples/blob/main/attractors/data/attractors.yml
    public static IReadOnlyList<SymmetricIconPreset> All { get; } = Array.AsReadOnly(new[]
    {
        P("Трилистник", 3, 1.56, -1, .1, -.82, 0, "Иконы — бирюза"),
        P("Пятилистник", 5, -1.806, 1.806, 0, 1, 0, "Иконы — аметист", 2),
        P("Снежинка", 6, -2.7, 5, 1.5, 1, 0, "Иконы — лёд", 1.9),
        P("Девятилучевая звезда", 9, -2.05, 3, -16.79, 1, 0, "Иконы — золото", 1.7),
        P("Золотой вихрь", 5, -2.5, 5, -1, 1, .188, "Иконы — золото", 1.75),
        P("Шестнадцать лепестков", 16, 2.39, -2.5, -.1, .9, -.15, "Иконы — аметист", 2.2)
    });

    private static SymmetricIconPreset P(string name, int degree, double lambda, double alpha,
        double beta, double gamma, double omega, string palette, double span = 3.2) => new(name,
        new() { Degree = degree, Lambda = lambda, Alpha = alpha, Beta = beta, Gamma = gamma,
            Omega = omega == 0 ? .1 : omega, Mirror = omega == 0, Span = span }, palette);

    public static void Apply(DynamicSystemState state, int index)
    {
        SymmetricIconPreset preset = All[index];
        state.Attractor2DMode = nameof(Attractor2DKind.SymmetricIcon);
        state.SymmetricIcon = preset.Settings.Clone();
        state.PaletteName = preset.PaletteName;
        state.X0 = .01; state.Y0 = .01;
        state.CenterX = 0; state.CenterY = 0; state.Zoom = 1;
    }
}
