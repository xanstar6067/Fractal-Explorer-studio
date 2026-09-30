using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Models;

// Append only: these values are persisted in saves.
public enum Flame3DVariation { Linear, Sinusoidal, Spherical, Bubble, Twist, Curl, Julia, Waves }

public sealed class Flame3DTransform
{
    public Ifs3DTransform Map { get; set; } = Ifs3DTransform.Contract(.65, 0, 0, 0);
    public double Weight { get; set; } = 1;
    public Flame3DVariation Variation { get; set; }
    public double Amount { get; set; } = 1;
    public double ColorSpeed { get; set; } = .5;
    public Color Color { get; set; } = Colors.DeepSkyBlue;
    [System.Text.Json.Serialization.JsonIgnore]
    public SolidColorBrush ColorBrush => new(Color);
    [System.Text.Json.Serialization.JsonIgnore]
    public string VariationName => Flame3DPresets.Name(Variation);
    public Flame3DTransform Clone() => new()
    {
        Map = Map.Clone(), Weight = Weight, Variation = Variation,
        Amount = Amount, ColorSpeed = ColorSpeed, Color = Color
    };
}

public sealed class Flame3DSettings
{
    public int Seed { get; set; } = 271828;
    public int Warmup { get; set; } = 40;
    public double Exposure { get; set; } = 1.6;
    public double Gamma { get; set; } = 2.2;
    public double Density { get; set; } = 1;
    public double Vibrancy { get; set; } = 1.15;
    public List<Flame3DTransform> Transforms { get; set; } = [];
    public Flame3DSettings Clone() => new()
    {
        Seed = Seed, Warmup = Warmup, Exposure = Exposure, Gamma = Gamma,
        Density = Density, Vibrancy = Vibrancy, Transforms = Transforms.Select(t => t.Clone()).ToList()
    };

    public void Validate()
    {
        if (Transforms is null || Transforms.Count is < 1 or > 25)
            throw new InvalidOperationException("Добавьте от 1 до 25 преобразований Flame.");
        foreach (var t in Transforms)
        {
            if (t is null || t.Map is null || !Enum.IsDefined(t.Variation) ||
                !double.IsFinite(t.Weight) || t.Weight < 0 || t.Weight > 1000 ||
                !double.IsFinite(t.Amount) || t.Amount is < 0 or > 1 ||
                !double.IsFinite(t.ColorSpeed) || t.ColorSpeed is < 0 or > 1 ||
                Values(t.Map).Any(v => !double.IsFinite(v) || Math.Abs(v) > 1000))
                throw new InvalidOperationException("Проверьте матрицы, веса, силу вариации и смешивание цвета Flame.");
        }
        if (Transforms.Sum(t => t.Weight) <= 0)
            throw new InvalidOperationException("Сумма весов Flame должна быть положительной.");
        if (Warmup is < 10 or > 1000 || !double.IsFinite(Exposure) || Exposure is < .05 or > 20 ||
            !double.IsFinite(Gamma) || Gamma is < .5 or > 4 ||
            !double.IsFinite(Density) || Density is < .05 or > 10 ||
            !double.IsFinite(Vibrancy) || Vibrancy is < 0 or > 2)
            throw new InvalidOperationException("Проверьте параметры накопления и тональной коррекции Flame.");
    }

    public static double[] Values(Ifs3DTransform t) =>
        [t.M11, t.M12, t.M13, t.Tx, t.M21, t.M22, t.M23, t.Ty, t.M31, t.M32, t.M33, t.Tz];
}

public static class Flame3DPresets
{
    public static string Name(Flame3DVariation v) => v switch
    {
        Flame3DVariation.Linear => "Линейная", Flame3DVariation.Sinusoidal => "Синусоида XYZ",
        Flame3DVariation.Spherical => "Сферическая инверсия", Flame3DVariation.Bubble => "Пузырь",
        Flame3DVariation.Twist => "Вихрь вокруг Y", Flame3DVariation.Curl => "Пространственный curl",
        Flame3DVariation.Julia => "Сферическое ветвление", _ => "Волны XYZ"
    };

    public static string Hint(Flame3DVariation v) => v switch
    {
        Flame3DVariation.Linear => "Только матрица 3×4: поворот, сжатие и перенос.",
        Flame3DVariation.Sinusoidal => "Синус каждой из трёх координат мягко складывает пространство.",
        Flame3DVariation.Spherical => "Инверсия p / |p|². Создаёт оболочки; точки около нуля могут улетать.",
        Flame3DVariation.Bubble => "Радиальное сжатие 4p / (4 + |p|²): округлые органические формы.",
        Flame3DVariation.Twist => "Поворот XZ зависит от высоты Y и радиуса: объёмные дымчатые ленты.",
        Flame3DVariation.Curl => "Связанные синусоидальные изгибы по трём осям: завитки и складки.",
        Flame3DVariation.Julia => "Квадратный корень радиуса и случайные ветви обоих сферических углов.",
        _ => "Каждая координата изгибается волнами двух остальных: пространственные переплетения."
    };

    public static List<Flame3DTransform> Transforms(int preset)
    {
        if (preset == 0)
        {
            // A persistent rotating/lifting orbit with rare reinjection makes actual ribbons,
            // rather than filling a ball with unrelated randomly rotated contractions.
            double a = .32, scale = .975;
            return
            [
                new()
                {
                    Map = new() { M11 = scale * Math.Cos(a), M13 = -scale * Math.Sin(a),
                        M22 = .985, M31 = scale * Math.Sin(a), M33 = scale * Math.Cos(a), Ty = .018 },
                    Weight = 14, Variation = Flame3DVariation.Twist, Amount = .22,
                    Color = Colors.DeepSkyBlue, ColorSpeed = .08
                },
                new()
                {
                    Map = Ifs3DTransform.Contract(.28, .68, -.55, .08),
                    Weight = 1, Variation = Flame3DVariation.Bubble, Amount = .5,
                    Color = Colors.HotPink, ColorSpeed = .8
                },
                new()
                {
                    Map = Ifs3DTransform.Contract(.32, -.48, -.35, .42),
                    Weight = .35, Variation = Flame3DVariation.Sinusoidal, Amount = .4,
                    Color = Colors.Gold, ColorSpeed = .7
                }
            ];
        }
        Color[] colors = preset switch
        {
            1 => [Colors.OrangeRed, Colors.Gold, Colors.DeepPink, Colors.MediumPurple],
            2 => [Colors.Turquoise, Colors.DodgerBlue, Colors.Orchid, Colors.LightCyan],
            _ => [Colors.DeepSkyBlue, Colors.MediumPurple, Colors.HotPink, Colors.Gold]
        };
        var result = new List<Flame3DTransform>();
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 2 + .35;
            double scale = preset == 2 ? .72 : .78;
            var map = Ifs3DTransform.Contract(scale, .38 * Math.Cos(a), (i - 1.5) * .22, .38 * Math.Sin(a));
            map.M11 = scale * Math.Cos(a); map.M13 = -scale * Math.Sin(a);
            map.M31 = scale * Math.Sin(a); map.M33 = scale * Math.Cos(a);
            map.M12 = .12; map.M23 = -.16; map.M32 += .12;
            result.Add(new()
            {
                Map = map, Color = colors[i], Weight = i == 3 ? .5 : 1,
                Variation = preset switch
                {
                    1 => i == 3 ? Flame3DVariation.Bubble : Flame3DVariation.Waves,
                    2 => i == 3 ? Flame3DVariation.Julia : Flame3DVariation.Curl,
                    _ => i == 3 ? Flame3DVariation.Sinusoidal : Flame3DVariation.Twist
                },
                Amount = preset == 2 ? .55 : .75
            });
        }
        return result;
    }
}
