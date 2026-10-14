using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Models;

// Хеши кадров всех математических лабораторий: побитная сверка до и после правки рендерера.
internal static partial class Program
{
    // Каждая лаборатория в каждом режиме: стартовый вид и пять отклонений от него (заливка и
    // направляющие, анимация, вид, точки пользователя, числовые параметры).
    //
    //   laboratory [--record файл] [--compare файл]
    //
    // Без ключей группа только проверяет, что каждый кадр не однотонный.
    private static async Task VerifyLaboratoriesAsync(string[] args)
    {
        string? record = ReadOption(args, "--record");
        string? compare = ReadOption(args, "--compare");
        const int w = 160, h = 120;
        var lines = new List<string>();
        foreach ((string name, MathematicalLaboratoryState state) in LaboratoryCases())
        {
            BitmapSource bitmap = await MathematicalLaboratoryRenderer.RenderBitmapAsync(state, w, h, CancellationToken.None);
            var pixels = new byte[w * h * 4];
            bitmap.CopyPixels(pixels, w * 4, 0);
            if (name.EndsWith("/base"))
                Check(Enumerable.Range(0, w * h).Select(pixel => BitConverter.ToInt32(pixels, pixel * 4)).Distinct().Count() > 1,
                    $"laboratory {name}: кадр однотонный.");
            lines.Add($"{name} {w}x{h}:{Convert.ToHexString(SHA256.HashData(pixels))}");
        }

        if (record is not null)
        {
            File.WriteAllLines(record, lines);
            Console.WriteLine($"Записано {lines.Count} кадров лабораторий в {record}.");
        }
        if (compare is not null)
        {
            string[] expected = File.ReadAllLines(compare).Where(line => line.Length > 0).ToArray();
            var differing = lines.Where(line => !expected.Contains(line)).ToList();
            foreach (string line in differing)
                Console.WriteLine($"Кадр отличается от эталона: {line}");
            Check(expected.Length == lines.Count, $"В эталоне {expected.Length} кадров, посчитано {lines.Count}.");
            Check(differing.Count == 0, $"{differing.Count} кадров лабораторий отличаются от эталона {compare}.");
            Console.WriteLine($"Все {lines.Count} кадров лабораторий совпадают с эталоном {compare}.");
        }
        Console.WriteLine($"PASS (laboratory): {lines.Count} кадров всех лабораторий во всех режимах.");
    }

    private static IEnumerable<(string Name, MathematicalLaboratoryState State)> LaboratoryCases()
    {
        foreach (MathematicalLaboratoryKind kind in Enum.GetValues<MathematicalLaboratoryKind>())
        {
            MathematicalLaboratoryDefinition definition = MathematicalLaboratoryCatalog.GetDefinition(kind);
            for (int mode = 0; mode < definition.Modes.Length; mode++)
            {
                MathematicalLaboratoryState Base()
                {
                    MathematicalLaboratoryState state = MathematicalLaboratoryCatalog.CreateDefaultState(kind);
                    state.Mode = mode;
                    return state;
                }
                string prefix = $"{kind}/{mode}";
                yield return ($"{prefix}/base", Base());

                MathematicalLaboratoryState outline = Base();
                outline.Filled = !outline.Filled;
                outline.ShowGuides = !outline.ShowGuides;
                yield return ($"{prefix}/outline", outline);

                MathematicalLaboratoryState animated = Base();
                animated.Animate = true;
                animated.Phase = 0.37;
                yield return ($"{prefix}/animated", animated);

                MathematicalLaboratoryState view = Base();
                view.Zoom *= 1.7;
                view.ViewCenterX = 0.13;
                view.ViewCenterY = -0.21;
                view.Rotation += 23;
                view.AnchorX = 0.2;
                view.AnchorY = -0.1;
                yield return ($"{prefix}/view", view);

                MathematicalLaboratoryState points = Base();
                points.InputPoints = [new(-0.6, -0.2), new(-0.3, 0.5), new(0.1, 0.65), new(0.55, 0.3), new(0.4, -0.45), new(-0.1, -0.6)];
                yield return ($"{prefix}/points", points);

                MathematicalLaboratoryState values = Base();
                values.PrimaryValue = Math.Clamp(values.PrimaryValue + 3, definition.PrimaryMinimum, definition.PrimaryMaximum);
                values.SecondaryValue = Math.Clamp(values.SecondaryValue + 1, definition.SecondaryMinimum, definition.SecondaryMaximum);
                values.TertiaryValue = Math.Clamp(values.TertiaryValue + 1, definition.TertiaryMinimum, definition.TertiaryMaximum);
                values.Parameter = Math.Clamp(values.Parameter * 1.3, definition.ParameterMinimum, definition.ParameterMaximum);
                yield return ($"{prefix}/values", values);
            }
        }
    }
}
