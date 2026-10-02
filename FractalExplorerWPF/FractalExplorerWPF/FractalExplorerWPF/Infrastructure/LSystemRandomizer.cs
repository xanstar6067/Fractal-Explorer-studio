using System.Text;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Constructs balanced grammars from meaningful motifs, rather than arbitrary command strings.</summary>
public static class LSystemRandomizer
{
    public static LSystemRandomizationResult Create2D(LSystemRandomizationSettings options,
        LSystemDefinition current, bool nearby, CancellationToken token)
    {
        options.Validate(); token.ThrowIfCancellationRequested();
        var random = new Random(options.Seed);
        LSystemDefinition d = current.Clone();
        if (nearby)
        {
            d.RulesText = MutateRules(d.RulesText, d.DrawSymbols, options, random, false);
            d.AngleDegrees = Jitter(d.AngleDegrees, 25 * options.Variation, random);
        }
        else
        {
            d.DrawSymbols = "F"; d.InitialAngleDegrees = 90; d.Depth = options.Detail + 2;
            d.StyleMode = options.Family is LSystemShapeFamily.Curves ? LSystemStyleMode.DrawingOrder : LSystemStyleMode.BranchDepth;
            int run = random.Next(1, 4);
            string stem = new('F', run);
            d.Axiom = "A";
            switch (options.Family)
            {
                case LSystemShapeFamily.Plants:
                    d.AngleDegrees = Jitter(28, 18 * options.Variation, random);
                    d.RulesText = $"A → {stem}{Branches2D(options, random, "A")}F\nF → FF";
                    break;
                case LSystemShapeFamily.Radial:
                    int arms = options.Branching + 2;
                    d.AngleDegrees = 360d / arms;
                    d.Axiom = string.Concat(Enumerable.Range(0, arms).Select(_ => "[A]+"));
                    d.RulesText = $"A → {stem}{Branches2D(options, random, "A")}F";
                    break;
                case LSystemShapeFamily.Curves:
                    d.Axiom = "F"; d.InitialAngleDegrees = 0; d.Depth = options.Detail + 1;
                    d.AngleDegrees = options.Symmetric ? 60 : Jitter(65, 20 * options.Variation, random);
                    string left = options.Symmetric || random.Next(2) == 0 ? "+" : "-";
                    d.RulesText = $"F → {stem}{left}F{Opposite(left)}{Opposite(left)}F{left}{stem}";
                    break;
                case LSystemShapeFamily.Geometric:
                    d.AngleDegrees = random.Next(2) == 0 ? 45 : 90;
                    d.RulesText = $"A → {stem}{Branches2D(options, random, "B")}A\nB → F[+B][-B]";
                    break;
            }
        }
        // Keep interactive results useful even with a previously very deep custom grammar.
        for (;;)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var scene = LSystemEngine.BuildScene(d, token);
                if (scene.Segments.Count > 50_000 && d.Depth > 1) { d.Depth--; continue; }
                if (scene.Segments.Count < 2 || scene.Bounds.Width < 1e-4 || scene.Bounds.Height < 1e-4)
                    throw new InvalidOperationException("Вариация получилась слишком простой. Попробуйте другое случайное число или семейство.");
                return new(d, null, options.Seed, Description(options, nearby), scene.Segments.Count);
            }
            catch (InvalidOperationException ex) when (d.Depth > 1 && IsLimit(ex))
            { d.Depth--; }
        }
    }

    public static LSystemRandomizationResult Create3D(LSystemRandomizationSettings options,
        LSystem3DSettings current, bool nearby, CancellationToken token)
    {
        options.Validate(); token.ThrowIfCancellationRequested();
        var random = new Random(options.Seed);
        LSystem3DSettings d;
        if (nearby)
        {
            d = current with
            {
                RulesText = MutateRules(current.RulesText, current.DrawSymbols, options, random, true),
                Yaw = Math.Clamp(Jitter(current.Yaw, 25 * options.Variation, random), -180, 180),
                Pitch = Math.Clamp(Jitter(current.Pitch, 25 * options.Variation, random), -180, 180),
                Roll = Math.Clamp(Jitter(current.Roll, 35 * options.Variation, random), -180, 180),
                Growth = 1
            };
        }
        else
        {
            int branches = options.Branching;
            var rule = new StringBuilder(new string('F', random.Next(1, 3)));
            string child = random.Next(3) == 0 ? "B" : "A";
            string branchStem = random.Next(3) == 0 ? new string('F', random.Next(1, 3)) : "";
            for (int i = 0; i < branches; i++)
            {
                int tilt = options.Symmetric ? 1 : random.Next(1, 3);
                rule.Append('[').Append('&', tilt).Append('>').Append(branchStem).Append(child).Append(']');
                rule.Append(options.Symmetric || random.Next(2) == 0 ? '\\' : '/');
            }
            d = new()
            {
                RulesText = "A → " + rule + (child == "B" ? "\nB → F[+>A][->A]" : ""), Generations = options.Detail + 2,
                Yaw = Jitter(28, 16 * options.Variation, random),
                Pitch = Jitter(38, 22 * options.Variation, random),
                Roll = options.Symmetric ? 360d / branches : Jitter(137.5, 40 * options.Variation, random),
                Radius = .1 + random.NextDouble() * .1,
                BranchTaper = .66 + random.NextDouble() * .17,
                StepDecay = .68 + random.NextDouble() * .2
            };
            switch (options.Family)
            {
                case LSystemShapeFamily.Plants:
                    if (random.Next(2) == 0) d = d with
                    { RulesText = d.RulesText.Replace("\n", "FA\n") + (child == "A" ? "FA" : "") };
                    break;
                case LSystemShapeFamily.Radial:
                    d = d with { Axiom = "[&A]" + string.Concat(Enumerable.Range(1, branches + 1).Select(_ => "\\[&A]")),
                        Pitch = 90, Roll = 360d / (branches + 1), StepDecay = .65 };
                    break;
                case LSystemShapeFamily.Curves:
                    d = d with { Axiom = "F", RulesText = random.Next(2) == 0 ? "F → F&\\F" : "F → F+\\F",
                        Generations = options.Detail + 3, Yaw = Jitter(32, 14 * options.Variation, random),
                        Pitch = Jitter(32, 14 * options.Variation, random), Roll = Jitter(4, 3 * options.Variation, random),
                        Radius = .16, BranchTaper = 1, ColorSource = LSystem3DColorSource.DrawingOrder };
                    break;
                case LSystemShapeFamily.Geometric:
                    string[] directions = ["+", "-", "&", "^", "|"];
                    d = d with { RulesText = "A → F" + string.Concat(directions.Take(branches).Select(c => $"[{c}>A]")) + ">A",
                        Yaw = options.Symmetric ? 90 : 45, Pitch = 90, Roll = 90, StepDecay = .5, BranchTaper = .62 };
                    break;
            }
        }
        for (;;)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var geometry = LSystem3DGeometry.Build(d, token);
                if (geometry.Segments.Length > 20_000 && d.Generations > 1) { d = d with { Generations = d.Generations - 1 }; continue; }
                if (geometry.Segments.Length < 2) throw new InvalidOperationException("Недостаточно сегментов для вариации. Выберите другую форму.");
                return new(null, d, options.Seed, Description(options, nearby), geometry.Segments.Length);
            }
            catch (InvalidOperationException ex) when (d.Generations > 1 && IsLimit(ex))
            { d = d with { Generations = d.Generations - 1 }; }
        }
    }

    private static bool IsLimit(Exception ex) => ex.Message.Contains("превыс") || ex.Message.Contains("превыш") ||
        ex.Message.Contains("Более") || ex.Message.Contains("более");
    private static string Description(LSystemRandomizationSettings o, bool nearby) =>
        (nearby ? "Вариация текущей формы" : o.Family switch
        { LSystemShapeFamily.Plants => "Ветвящееся растение", LSystemShapeFamily.Radial => "Радиальная конструкция",
          LSystemShapeFamily.Curves => "Трубчатая / фрактальная кривая", _ => "Геометрическое ветвление" }) +
        $" · число {o.Seed}";
    private static double Jitter(double value, double amount, Random r) => Math.Round(value + (r.NextDouble() * 2 - 1) * amount, 3);
    private static string Opposite(string s) => s == "+" ? "-" : "+";

    private static string Branches2D(LSystemRandomizationSettings o, Random r, string symbol)
    {
        var s = new StringBuilder();
        for (int i = 0; i < o.Branching; i++)
        {
            if (o.Symmetric && o.Branching % 2 == 1 && i == o.Branching - 1)
            { s.Append("[F").Append(symbol).Append(']'); continue; }
            int turns = o.Symmetric ? i / 2 + 1 : r.Next(1, 4);
            char sign = o.Symmetric ? (i % 2 == 0 ? '+' : '-') : (r.Next(2) == 0 ? '+' : '-');
            s.Append('[').Append(sign, turns).Append(symbol).Append(']');
        }
        return s.ToString();
    }

    private static string MutateRules(string text, string drawSymbols, LSystemRandomizationSettings o, Random r, bool spatial)
    {
        var rules = LSystemEngine.ParseRules(text, spatial);
        if (o.Variation == 0) return text;
        var drawing = LSystemEngine.NormalizeSymbols(drawSymbols).ToHashSet();
        bool changed = false;
        var output = new List<string>();
        foreach (var (symbol, rule) in rules)
        {
            var next = new StringBuilder();
            foreach (char c in rule)
            {
                next.Append(c);
                if (drawing.Contains(c) && r.NextDouble() < o.Variation * .4)
                { next.Append(c); changed = true; }
                // A symmetric mutation alters paired turn commands together via the global angle.
                if (!o.Symmetric && "+-&^\\/".Contains(c) && r.NextDouble() < o.Variation * .25)
                { next.Append(c); changed = true; }
            }
            output.Add($"{symbol} → {next}");
        }
        if (!changed && output.Count > 0)
        {
            int index = r.Next(output.Count);
            string line = output[index];
            int start = line.IndexOf('→') + 1;
            int position = Enumerable.Range(start, line.Length - start).FirstOrDefault(i => drawing.Contains(line[i]), -1);
            if (position >= 0) output[index] = line.Insert(position, line[position].ToString());
        }
        return string.Join("\n", output);
    }
}
