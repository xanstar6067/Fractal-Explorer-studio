using System.Text;
using FractalExplorerWPF.Core.Rendering;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Builds balanced recursive grammars; each seed selects topology as well as angles.</summary>
public static class LSystemRandomizer
{
    public static LSystemRandomizationResult Create2D(LSystemRandomizationSettings options,
        LSystemDefinition current, bool nearby, CancellationToken token)
    {
        options.Validate(); token.ThrowIfCancellationRequested();
        var random = new Random(options.Seed);
        var d = current.Clone();
        string motif = "";
        if (nearby)
        {
            d.RulesText = MutateRules(d.RulesText, d.DrawSymbols, options, random, false);
            d.AngleDegrees = Math.Clamp(Jitter(d.AngleDegrees, 25 * options.Variation, random), -180, 180);
        }
        else
        {
            d.DrawSymbols = "F"; d.InitialAngleDegrees = 90; d.Depth = options.Detail + 2;
            d.StyleMode = options.Family == LSystemShapeFamily.Curves ? LSystemStyleMode.DrawingOrder : LSystemStyleMode.BranchDepth;
            d.AngleDegrees = Sample(options.AngleMinimum, options.AngleMaximum, random);
            d.Axiom = "A";
            if (options.Family == LSystemShapeFamily.Curves)
            {
                var curve = CreateCurve(options, random, false);
                d.Axiom = curve.Axiom; d.RulesText = curve.Rules; d.DrawSymbols = curve.Drawing;
                d.AngleDegrees = CurveAngle(curve.Angle, options, random);
                d.InitialAngleDegrees = 0; d.Depth = options.Detail + curve.ExtraDepth;
                motif = curve.Name;
            }
            else
            {
                d.RulesText = CreateBranches(options, random, false);
                if (options.Family == LSystemShapeFamily.Radial)
                {
                    int arms = options.Branching + random.Next(2, 5);
                    // Repeated roots use the same grammar; angle also controls the branches.
                    d.AngleDegrees = 360d / arms;
                    d.Axiom = string.Concat(Enumerable.Repeat("[A]+", arms));
                }
                if (options.Family == LSystemShapeFamily.Geometric)
                    d.AngleDegrees = CurveAngle(random.Next(2) == 0 ? 45 : 90, options, random);
            }
        }
        for (;;)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var scene = LSystemEngine.BuildScene(d, token);
                if (scene.Segments.Count > 50_000 && d.Depth > 1) { d.Depth--; continue; }
                if (scene.Segments.Count < 2 || scene.Bounds.Width < 1e-4 || scene.Bounds.Height < 1e-4)
                    throw new InvalidOperationException("Вариация получилась слишком простой. Попробуйте другое случайное число или семейство.");
                return new(d, null, options.Seed, Description(options, nearby, motif), scene.Segments.Count);
            }
            catch (InvalidOperationException ex) when (d.Depth > 1 && IsLimit(ex)) { d.Depth--; }
        }
    }

    public static LSystemRandomizationResult Create3D(LSystemRandomizationSettings options,
        LSystem3DSettings current, bool nearby, CancellationToken token)
    {
        options.Validate(); token.ThrowIfCancellationRequested();
        var random = new Random(options.Seed);
        LSystem3DSettings d;
        string motif = "";
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
            d = new()
            {
                RulesText = CreateBranches(options, random, true), Generations = options.Detail + 2,
                Yaw = Sample(options.AngleMinimum, options.AngleMaximum, random),
                Pitch = Sample(options.PitchMinimum, options.PitchMaximum, random),
                Roll = Sample(options.RollMinimum, options.RollMaximum, random),
                Radius = options.Radius, BranchTaper = options.BranchTaper, StepDecay = options.StepDecay
            };
            if (options.Family == LSystemShapeFamily.Curves)
            {
                var curve = CreateCurve(options, random, true);
                d = d with
                {
                    Axiom = curve.Axiom, RulesText = curve.Rules, DrawSymbols = curve.Drawing,
                    Generations = options.Detail + curve.ExtraDepth,
                    Yaw = CurveAngle(curve.Angle, options, random),
                    Pitch = Math.Clamp(curve.Angle, options.PitchMinimum, options.PitchMaximum),
                    ColorSource = LSystem3DColorSource.DrawingOrder
                };
                // Hilbert's grid needs quarter turns on all three local axes.
                if (curve.Name == "Гильберт") d = d with
                { Roll = Math.Clamp(90, options.RollMinimum, options.RollMaximum) };
                if (curve.Name == "Спираль") d = d with
                { Roll = Math.Clamp(Jitter(6, 4 * options.Variation, random), options.RollMinimum, options.RollMaximum) };
                motif = curve.Name;
            }
            else if (options.Family == LSystemShapeFamily.Radial)
            {
                int arms = options.Branching + random.Next(1, 4);
                d = d with
                {
                    Axiom = "[&A]" + string.Concat(Enumerable.Repeat("\\[&A]", arms - 1)),
                    Roll = 360d / arms
                };
            }
            else if (options.Family == LSystemShapeFamily.Geometric)
                d = d with
                {
                    Yaw = CurveAngle(90, options, random),
                    Pitch = Math.Clamp(90, options.PitchMinimum, options.PitchMaximum)
                };
        }
        for (;;)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var geometry = LSystem3DGeometry.Build(d, token);
                if (geometry.Segments.Length > 20_000 && d.Generations > 1)
                { d = d with { Generations = d.Generations - 1 }; continue; }
                // Long recursive paths can fit into the scene as subpixel hairs even below the segment limit.
                // Keep generated tubes readable at preview size, while preserving the requested radius.
                if (!nearby && options.Family == LSystemShapeFamily.Curves && d.Generations > 1 &&
                    geometry.Segments.Max(s => s.Radius) < .01f)
                { d = d with { Generations = d.Generations - 1 }; continue; }
                if (geometry.Segments.Length < 2) throw new InvalidOperationException("Недостаточно сегментов для вариации. Выберите другую форму.");
                return new(null, d, options.Seed, Description(options, nearby, motif), geometry.Segments.Length);
            }
            catch (InvalidOperationException ex) when (d.Generations > 1 && IsLimit(ex))
            { d = d with { Generations = d.Generations - 1 }; }
        }
    }

    private sealed record Curve(string Axiom, string Rules, string Drawing, double Angle, int ExtraDepth, string Name);

    private static Curve CreateCurve(LSystemRandomizationSettings o, Random r, bool spatial)
    {
        var kind = o.CurveKind == LSystemCurveKind.Mixed ? (LSystemCurveKind)r.Next(1, 6) : o.CurveKind;
        string run = new('F', r.Next(1, o.StemLength + 1));
        string sign = o.Symmetric || r.Next(2) == 0 ? "+" : "-";
        string inverse = sign == "+" ? "-" : "+";
        // Spatial folds use alternating local planes instead of a constant screw motion.
        string pitch = spatial ? (r.Next(2) == 0 ? "&" : "^") : sign;
        string unpitch = pitch == "&" ? "^" : pitch == "^" ? "&" : inverse;
        string bend = spatial ? "\\" : "";
        if (o.RuleComplexity >= 3)
        {
            string straight = run;
            run += sign + new string('F', r.Next(1, o.StemLength + 1)) + inverse + straight;
            if (o.RuleComplexity == 4) run += pitch + straight + unpitch + straight;
        }
        string motif = run + sign + "F" + pitch + run + unpitch + inverse + "F";
        switch (kind)
        {
            case LSystemCurveKind.Folding:
                return r.Next(2) == 0
                    ? new("F", $"F → {sign}{run}{pitch}{pitch}{run}{inverse}", "F", 45, 4, "Складчатая")
                    : new("F", $"F → {run}{sign}{run}{unpitch}{unpitch}{run}{pitch}{run}", "F", 60, 2, "Складчатая");
            case LSystemCurveKind.Dragon:
                return new("FA", $"A → A{sign}B{run}{bend}{sign}\nB → {inverse}{run}A{inverse}{bend}B",
                    "F", 90, 5, "Дракон");
            case LSystemCurveKind.Hilbert:
                if (spatial)
                {
                    var hilbert = LSystem3DPresets.All.First(p => p.Name.StartsWith("Пространственный Гильберт")).Settings;
                    return new(hilbert.Axiom, hilbert.RulesText.Replace("F", run), "F", 90, 0, "Гильберт");
                }
                return new("A", $"A → +B{run}-A{run}A-{run}B+\nB → -A{run}+B{run}B+{run}A-", "F", 90, 1, "Гильберт");
            case LSystemCurveKind.Meander:
                string woven = string.Concat(Enumerable.Repeat(motif, o.RuleComplexity));
                return new("A", $"A → {woven}{bend}B\nB → {run}{inverse}A{pitch}{run}{unpitch}A{sign}{run}",
                    "F", 90, 2, "Меандр");
            default:
                // Alternating drawing symbols vary spacing along a screw without reducing it to a straight path.
                string second = run.Replace('F', 'G');
                if (!o.Symmetric) second += new string('G', r.Next(1, o.StemLength + 1));
                return new("F", $"F → {run}{pitch}{bend}G\nG → {second}{pitch}{bend}F", "FG", 30, 3, "Спираль");
        }
    }

    private static string CreateBranches(LSystemRandomizationSettings o, Random r, bool spatial)
    {
        int symbols = o.RuleComplexity;
        var rules = new List<string>();
        string[] planes = spatial ? ["+", "-", "&", "^", "|", "+&", "-^"] : ["+", "-", "++", "--"];
        for (int s = 0; s < symbols; s++)
        {
            char self = (char)('A' + s);
            var rule = new StringBuilder(new string('F', r.Next(1, o.StemLength + 1)));
            bool tiered = o.Family == LSystemShapeFamily.Plants && r.Next(2) == 0;
            string pairedTurn = planes[r.Next(spatial ? 4 : 2)];
            int directionOffset = r.Next(planes.Length);
            string regularStem = new('F', r.Next(o.StemLength));
            bool nested = o.RuleComplexity >= 3 && r.Next(3) == 0;
            for (int i = 0; i < o.Branching; i++)
            {
                char child = (char)('A' + (o.Symmetric ? (s + 1) % symbols : r.Next(symbols)));
                string turn = o.Symmetric ? (i % 2 == 0 ? pairedTurn : ReverseTurn(pairedTurn)) : planes[r.Next(planes.Length)];
                if (o.Family == LSystemShapeFamily.Geometric)
                    turn = planes[(i + s + (o.Symmetric ? directionOffset : r.Next(planes.Length))) % planes.Length];
                string stem = o.Symmetric ? regularStem : new('F', r.Next(o.StemLength));
                rule.Append('[').Append(turn);
                if (spatial) rule.Append('>');
                rule.Append(stem);
                if (o.Symmetric ? nested : o.RuleComplexity >= 3 && r.Next(3) == 0)
                    rule.Append('[').Append(ReverseTurn(turn)).Append(spatial ? ">" : "").Append(child).Append(']');
                rule.Append(child).Append(']');
                if (spatial) rule.Append(o.Symmetric || r.Next(2) == 0 ? '\\' : '/');
                if (tiered) rule.Append('F');
            }
            if (o.Family == LSystemShapeFamily.Plants || r.Next(2) == 0)
                rule.Append(o.Symmetric ? "" : planes[r.Next(planes.Length)]).Append(spatial ? ">" : "").Append(self);
            rules.Add($"{self} → {rule}");
        }
        if (!spatial && o.Family == LSystemShapeFamily.Plants)
            rules.Add(r.Next(2) == 0 ? "F → FF" : "F → F");
        return string.Join("\n", rules);
    }

    private static string ReverseTurn(string turn) => string.Concat(turn.Select(c => c switch
    { '+' => '-', '-' => '+', '&' => '^', '^' => '&', '\\' => '/', '/' => '\\', _ => c }));
    private static double Sample(double min, double max, Random r) => Math.Round(min + r.NextDouble() * (max - min), 3);
    private static double CurveAngle(double angle, LSystemRandomizationSettings o, Random r) =>
        Math.Clamp(o.Symmetric ? angle : Jitter(angle, 20 * o.Variation, r), o.AngleMinimum, o.AngleMaximum);
    private static bool IsLimit(Exception ex) => ex.Message.Contains("превыс") || ex.Message.Contains("превыш") ||
        ex.Message.Contains("Более") || ex.Message.Contains("более");
    private static string Description(LSystemRandomizationSettings o, bool nearby, string motif) =>
        (nearby ? "Вариация текущей формы" : o.Family switch
        { LSystemShapeFamily.Plants => "Ветвящееся растение", LSystemShapeFamily.Radial => "Радиальная конструкция",
          LSystemShapeFamily.Curves => $"Кривая · {motif}", _ => "Геометрическое ветвление" }) + $" · число {o.Seed}";
    private static double Jitter(double value, double amount, Random r) => Math.Round(value + (r.NextDouble() * 2 - 1) * amount, 3);

    private static string MutateRules(string text, string drawSymbols, LSystemRandomizationSettings o, Random r, bool spatial)
    {
        var rules = LSystemEngine.ParseRules(text, spatial);
        if (o.Variation == 0) return text;
        var drawing = LSystemEngine.NormalizeSymbols(drawSymbols).ToHashSet();
        bool changed = false;
        bool branching = text.Contains('[');
        var output = new List<string>();
        foreach (var (symbol, rule) in rules)
        {
            var next = new StringBuilder();
            foreach (char c in rule)
            {
                next.Append(c);
                if (drawing.Contains(c) && r.NextDouble() < o.Variation * .4)
                {
                    if (o.Variation > .5 && r.Next(2) == 0)
                        next.Append(spatial ? "&" : "+").Append(c).Append(spatial ? "^" : "-");
                    else next.Append(c);
                    changed = true;
                }
                if (branching && rules.ContainsKey(c) && !drawing.Contains(c) && r.NextDouble() < o.Variation * .12)
                {
                    next.Append("[+").Append(spatial ? ">" : "").Append(c).Append("][-").Append(spatial ? ">" : "").Append(c).Append(']');
                    changed = true;
                }
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
