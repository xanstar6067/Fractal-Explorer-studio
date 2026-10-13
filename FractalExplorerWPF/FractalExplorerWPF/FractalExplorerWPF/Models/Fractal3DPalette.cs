using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

/// <summary>
/// Палитра трёхмерного фрактала: цвета опорных точек градиента и, по желанию, окружение —
/// фон, цвет лампы и цвет материала. Окружение применяется только при
/// <see cref="OverridesEnvironment"/>, поэтому встроенные палитры не трогают свет и фон.
/// </summary>
public sealed class Fractal3DPalette
{
    /// <summary>Сколько опорных цветов шейдер умеет принять за один кадр.</summary>
    public const int MaxColors = 16;

    public string Name { get; set; } = "Новая палитра";

    public List<Color> Colors { get; set; } = [Color.FromRgb(26, 58, 122), Color.FromRgb(255, 186, 92)];

    /// <summary>Плавный переход между опорными цветами или резкие полосы.</summary>
    public bool IsGradient { get; set; } = true;

    /// <summary>Степень, в которую возводится позиция на градиенте: смещает насыщенность полос.</summary>
    public double Gamma { get; set; } = 1;

    public bool IsBuiltIn { get; set; }

    /// <summary>Задаёт ли палитра заодно фон, свет и материал.</summary>
    public bool OverridesEnvironment { get; set; }

    public Color BackgroundTop { get; set; } = Fractal3DEnvironment.BackgroundTop;
    public Color BackgroundBottom { get; set; } = Fractal3DEnvironment.BackgroundBottom;
    public Color LightColor { get; set; } = Fractal3DEnvironment.LightColor;
    public Color SurfaceColor { get; set; } = Fractal3DEnvironment.SurfaceColor;

    public static Fractal3DPalette FromPair(string name, Color first, Color second) => new()
    {
        Name = name,
        Colors = [first, second]
    };

    public Fractal3DPalette Clone(string? name = null) => new()
    {
        Name = name ?? Name,
        Colors = [.. Colors],
        IsGradient = IsGradient,
        Gamma = Gamma,
        OverridesEnvironment = OverridesEnvironment,
        BackgroundTop = BackgroundTop,
        BackgroundBottom = BackgroundBottom,
        LightColor = LightColor,
        SurfaceColor = SurfaceColor
    };

    public override string ToString() => Name;
}

/// <summary>Свет и фон по умолчанию: их же несут встроенные палитры.</summary>
public static class Fractal3DEnvironment
{
    public static Color BackgroundTop => Color.FromRgb(18, 22, 34);
    public static Color BackgroundBottom => Color.FromRgb(6, 7, 11);
    public static Color LightColor => Color.FromRgb(255, 255, 255);
    public static Color SurfaceColor => Color.FromRgb(228, 206, 180);
}

/// <summary>
/// Встроенные палитры трёхмерных фракталов. Окружение у них — то же, что было до появления
/// палитр, и <see cref="Fractal3DPalette.OverridesEnvironment"/> выключен: встроенная палитра
/// меняет цвет фигуры, но не трогает свет и фон.
/// </summary>
public static class Fractal3DPalettes
{
    /// <summary>Палитра по умолчанию: те же два цвета, что рисовались до появления палитр.</summary>
    public const string ClassicName = "Классическая";

    public static IReadOnlyList<Fractal3DPalette> All { get; } =
    [
        BuiltIn(ClassicName, [Rgb(26, 58, 122), Rgb(255, 186, 92)]),
        BuiltIn("Раскалённый металл",
            [Rgb(8, 4, 2), Rgb(92, 18, 10), Rgb(214, 74, 18), Rgb(255, 176, 48), Rgb(255, 246, 214)]),
        BuiltIn("Виридис",
            [Rgb(68, 1, 84), Rgb(59, 82, 139), Rgb(33, 145, 140), Rgb(94, 201, 98), Rgb(253, 231, 37)]),
        BuiltIn("Северное сияние",
            [Rgb(3, 7, 26), Rgb(10, 58, 80), Rgb(24, 158, 140), Rgb(126, 232, 151), Rgb(236, 255, 214)]),
        BuiltIn("Аметист",
            [Rgb(12, 4, 24), Rgb(70, 16, 110), Rgb(160, 40, 180), Rgb(236, 110, 200), Rgb(255, 225, 250)]),
        BuiltIn("Медь и патина",
            [Rgb(16, 28, 26), Rgb(22, 92, 86), Rgb(86, 190, 168), Rgb(224, 158, 86), Rgb(120, 44, 18)]),
        BuiltIn("Спектр",
            [Rgb(255, 64, 64), Rgb(255, 208, 48), Rgb(72, 220, 92), Rgb(52, 176, 255), Rgb(128, 88, 255)]),
        BuiltIn("Лёд",
            [Rgb(6, 16, 38), Rgb(26, 86, 150), Rgb(120, 200, 238), Rgb(214, 244, 255), Rgb(255, 255, 255)]),
        BuiltIn("Сепия",
            [Rgb(24, 16, 10), Rgb(86, 58, 34), Rgb(168, 128, 82), Rgb(228, 200, 158), Rgb(255, 246, 228)]),
        BuiltIn("Неон",
            [Rgb(4, 2, 10), Rgb(236, 32, 180), Rgb(60, 244, 236), Rgb(255, 255, 255)]),
        BuiltIn("Мрамор",
            [Rgb(22, 22, 26), Rgb(96, 98, 104), Rgb(186, 188, 194), Rgb(238, 238, 242), Rgb(255, 255, 255)]),
        BuiltIn("Закат",
            [Rgb(18, 10, 40), Rgb(104, 28, 92), Rgb(220, 74, 88), Rgb(255, 158, 74), Rgb(255, 232, 166)])
    ];

    /// <summary>Копия встроенной палитры: состояние владеет своей палитрой и правит её свободно.</summary>
    public static Fractal3DPalette Get(string name) =>
        (All.FirstOrDefault(palette => palette.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
         ?? All[0]).Clone();

    public static Fractal3DPalette Classic() => Get(ClassicName);

    private static Fractal3DPalette BuiltIn(string name, List<Color> colors) => new()
    {
        Name = name,
        Colors = colors,
        IsBuiltIn = true
    };

    private static Color Rgb(byte red, byte green, byte blue) => Color.FromRgb(red, green, blue);
}
