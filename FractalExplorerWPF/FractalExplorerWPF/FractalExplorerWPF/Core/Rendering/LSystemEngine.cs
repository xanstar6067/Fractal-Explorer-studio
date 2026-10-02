using System.Windows.Media;
using FractalExplorerWPF.Models;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering;

public readonly record struct LSystemPoint(double X, double Y);

public readonly record struct LSystemSegment(
    LSystemPoint Start,
    LSystemPoint End,
    int CreatedGeneration,
    int BranchDepth);

public readonly record struct LSystemBounds(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public double CenterX => (Left + Right) / 2.0;
    public double CenterY => (Top + Bottom) / 2.0;
}

public sealed class LSystemScene
{
    public required IReadOnlyList<LSystemSegment> Segments { get; init; }
    public required LSystemBounds Bounds { get; init; }
    public required int ExpandedSymbolCount { get; init; }
    public required int MaximumGeneration { get; init; }
    public required int MaximumBranchDepth { get; init; }
}

public static class LSystemEngine
{
    public const int MaximumExpandedSymbols = 2_000_000;
    public const int MaximumSegments = 750_000;

    public static LSystemScene BuildScene(
        LSystemDefinition definition,
        CancellationToken cancellationToken,
        Action<int>? reportProgress = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Depth is < 0 or > 20)
        {
            throw new InvalidOperationException("Глубина должна быть от 0 до 20.");
        }

        string axiom = NormalizeSymbols(definition.Axiom);
        if (axiom.Length == 0)
        {
            throw new InvalidOperationException("Аксиома не может быть пустой.");
        }

        HashSet<char> drawSymbols = NormalizeSymbols(definition.DrawSymbols).ToHashSet();
        if (drawSymbols.Count == 0)
        {
            throw new InvalidOperationException("Укажите хотя бы один рисующий символ.");
        }

        Dictionary<char, string> rules = ParseRules(definition.RulesText);
        List<LSystemToken> tokens = axiom.Select(symbol => new LSystemToken(symbol, 0)).ToList();

        for (int generation = 1; generation <= definition.Depth; generation++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long nextLength = 0;
            foreach (LSystemToken token in tokens)
            {
                nextLength += rules.TryGetValue(token.Symbol, out string? replacement)
                    ? replacement.Length
                    : 1;
                if (nextLength > MaximumExpandedSymbols)
                {
                    throw new InvalidOperationException(
                        $"После поколения {generation} строка превысит {MaximumExpandedSymbols:N0} символов. " +
                        "Уменьшите глубину или упростите правила.");
                }
            }

            var next = new List<LSystemToken>((int)nextLength);
            foreach (LSystemToken token in tokens)
            {
                if (!rules.TryGetValue(token.Symbol, out string? replacement))
                {
                    next.Add(token);
                    continue;
                }

                foreach (char replacementSymbol in replacement)
                {
                    next.Add(new LSystemToken(replacementSymbol, generation));
                }
            }

            tokens = next;
            reportProgress?.Invoke(definition.Depth == 0 ? 35 : generation * 35 / definition.Depth);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var segments = new List<LSystemSegment>(Math.Min(tokens.Count, MaximumSegments));
        var stack = new Stack<TurtleState>();
        var position = new LSystemPoint(0, 0);
        double heading = definition.InitialAngleDegrees * Math.PI / 180.0;
        double turn = definition.AngleDegrees * Math.PI / 180.0;
        int branchDepth = 0;
        int maximumBranchDepth = 0;
        int maximumGeneration = 0;
        double left = 0;
        double right = 0;
        double top = 0;
        double bottom = 0;

        for (int index = 0; index < tokens.Count; index++)
        {
            if ((index & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                reportProgress?.Invoke(35 + (int)(60L * index / Math.Max(1, tokens.Count)));
            }

            LSystemToken token = tokens[index];
            char symbol = token.Symbol;
            if (drawSymbols.Contains(symbol) || symbol == 'f')
            {
                var next = new LSystemPoint(position.X + Math.Cos(heading), position.Y + Math.Sin(heading));
                Include(next, ref left, ref top, ref right, ref bottom);
                if (symbol != 'f')
                {
                    if (segments.Count >= MaximumSegments)
                    {
                        throw new InvalidOperationException(
                            $"Фрактал содержит более {MaximumSegments:N0} отрезков. Уменьшите глубину.");
                    }
                    segments.Add(new LSystemSegment(position, next, token.Generation, branchDepth));
                    maximumGeneration = Math.Max(maximumGeneration, token.Generation);
                }
                position = next;
                continue;
            }

            switch (symbol)
            {
                case '+':
                    heading += turn;
                    break;
                case '-':
                    heading -= turn;
                    break;
                case '|':
                    heading += Math.PI;
                    break;
                case '[':
                    stack.Push(new TurtleState(position, heading, branchDepth));
                    branchDepth++;
                    maximumBranchDepth = Math.Max(maximumBranchDepth, branchDepth);
                    break;
                case ']':
                    if (stack.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"Лишня закрывающая скобка в позиции {index + 1}.");
                    }
                    TurtleState state = stack.Pop();
                    position = state.Position;
                    heading = state.Heading;
                    branchDepth = state.BranchDepth;
                    break;
            }
        }

        if (stack.Count != 0)
        {
            throw new InvalidOperationException("В результате развёртки остались незакрытые скобки.");
        }
        if (segments.Count == 0)
        {
            throw new InvalidOperationException(
                "Ни один отрезок не построен. Проверьте аксиому, правила и список рисующих символов.");
        }

        reportProgress?.Invoke(100);
        return new LSystemScene
        {
            Segments = segments,
            Bounds = new LSystemBounds(left, top, right, bottom),
            ExpandedSymbolCount = tokens.Count,
            MaximumGeneration = maximumGeneration,
            MaximumBranchDepth = maximumBranchDepth
        };
    }

    public static Dictionary<char, string> ParseRules(string rulesText, bool spatialCommands = false)
    {
        var result = new Dictionary<char, string>();
        string normalizedText = (rulesText ?? string.Empty).Replace('\r', '\n');
        foreach (string rawLine in normalizedText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine;
            // In a spatial grammar // means two rolls, so comments use # instead.
            int commentStart = spatialCommands ? line.IndexOf('#') : line.IndexOf("//", StringComparison.Ordinal);
            if (commentStart >= 0)
            {
                line = line[..commentStart];
            }
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int arrow = line.IndexOf('→');
            int arrowLength = 1;
            if (arrow < 0)
            {
                arrow = spatialCommands ? line.IndexOf('=') : -1;
                arrowLength = 1;
            }
            if (arrow < 0)
            {
                arrow = line.IndexOf("->", StringComparison.Ordinal);
                arrowLength = 2;
            }
            if (arrow < 0)
            {
                throw new InvalidOperationException(
                    $"В правиле «{rawLine}» нет стрелки → или ->" + (spatialCommands ? " или =." : "."));
            }

            string source = NormalizeSymbols(line[..arrow]);
            string replacement = NormalizeSymbols(line[(arrow + arrowLength)..]);
            if (source.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Левая часть правила «{rawLine}» должна состоять из одного символа.");
            }
            if (replacement.Length == 0 && !spatialCommands)
            {
                throw new InvalidOperationException(
                    $"Правая часть правила «{rawLine}» не может быть пустой.");
            }
            if (!result.TryAdd(source[0], replacement))
            {
                throw new InvalidOperationException($"Для символа «{source}» указано несколько правил.");
            }
        }
        return result;
    }

    public static string NormalizeSymbols(string? value) =>
        new((value ?? string.Empty)
            .Select(symbol => symbol == '−' ? '-' : symbol)
            .Where(symbol => !char.IsWhiteSpace(symbol))
            .ToArray());

    private static void Include(
        LSystemPoint point,
        ref double left,
        ref double top,
        ref double right,
        ref double bottom)
    {
        left = Math.Min(left, point.X);
        right = Math.Max(right, point.X);
        top = Math.Min(top, point.Y);
        bottom = Math.Max(bottom, point.Y);
    }

    private readonly record struct LSystemToken(char Symbol, int Generation);
    private readonly record struct TurtleState(LSystemPoint Position, double Heading, int BranchDepth);
}

public static class LSystemRasterizer
{
    public static byte[]? Render(
        LSystemScene scene,
        LSystemDefinition definition,
        int width,
        int height,
        int visibleSegmentCount,
        double viewZoom,
        double panX,
        double panY,
        CancellationToken cancellationToken,
        Action<int>? reportProgress = null)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        Fill(pixels, definition.BackgroundColor);

        double boundsWidth = Math.Max(scene.Bounds.Width, 1e-9);
        double boundsHeight = Math.Max(scene.Bounds.Height, 1e-9);
        double margin = Math.Max(12, Math.Min(width, height) * 0.055);
        double fitScale = Math.Min(
            Math.Max(1, width - margin * 2) / boundsWidth,
            Math.Max(1, height - margin * 2) / boundsHeight);
        double scale = fitScale * Math.Clamp(viewZoom, 0.02, 1_000);
        double centerX = width * (0.5 + panX);
        double centerY = height * (0.5 + panY);
        double thicknessScale = Math.Min(width, height) / 700.0 * Math.Sqrt(Math.Clamp(viewZoom, 0.05, 100));
        int count = Math.Clamp(visibleSegmentCount, 0, scene.Segments.Count);

        for (int index = 0; index < count; index++)
        {
            if ((index & 255) == 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }

                reportProgress?.Invoke((int)(100L * index / Math.Max(1, count)));
            }

            LSystemSegment segment = scene.Segments[index];
            double style = GetStylePosition(scene, definition.StyleMode, segment, index);
            Color color = Interpolate(definition.StartColor, definition.EndColor, style);
            double thickness = Math.Max(0.15,
                Interpolate(definition.StartThickness, definition.EndThickness, style) * thicknessScale);
            double x1 = centerX + (segment.Start.X - scene.Bounds.CenterX) * scale;
            double y1 = centerY - (segment.Start.Y - scene.Bounds.CenterY) * scale;
            double x2 = centerX + (segment.End.X - scene.Bounds.CenterX) * scale;
            double y2 = centerY - (segment.End.Y - scene.Bounds.CenterY) * scale;
            DrawAntialiasedLine(pixels, width, height, stride, x1, y1, x2, y2, thickness, color);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        reportProgress?.Invoke(100);
        return pixels;
    }

    private static double GetStylePosition(
        LSystemScene scene,
        LSystemStyleMode mode,
        LSystemSegment segment,
        int index) => mode switch
    {
        LSystemStyleMode.Generation => segment.CreatedGeneration / (double)Math.Max(1, scene.MaximumGeneration),
        LSystemStyleMode.BranchDepth => segment.BranchDepth / (double)Math.Max(1, scene.MaximumBranchDepth),
        LSystemStyleMode.DrawingOrder => index / (double)Math.Max(1, scene.Segments.Count - 1),
        _ => 0
    };

    private static void Fill(byte[] pixels, Color color)
    {
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;
        }
    }

    private static void DrawAntialiasedLine(
        byte[] pixels,
        int width,
        int height,
        int stride,
        double x1,
        double y1,
        double x2,
        double y2,
        double thickness,
        Color color)
    {
        double radius = Math.Max(0.5, thickness / 2.0);
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x1, x2) - radius - 1));
        int maxX = Math.Min(width - 1, (int)Math.Ceiling(Math.Max(x1, x2) + radius + 1));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(y1, y2) - radius - 1));
        int maxY = Math.Min(height - 1, (int)Math.Ceiling(Math.Max(y1, y2) + radius + 1));
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        double dx = x2 - x1;
        double dy = y2 - y1;
        double lengthSquared = dx * dx + dy * dy;
        double subpixelOpacity = Math.Min(1.0, thickness);
        for (int y = minY; y <= maxY; y++)
        {
            double py = y + 0.5;
            for (int x = minX; x <= maxX; x++)
            {
                double px = x + 0.5;
                double t = lengthSquared <= 1e-12
                    ? 0
                    : Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lengthSquared, 0, 1);
                double nearestX = x1 + t * dx;
                double nearestY = y1 + t * dy;
                double distance = Math.Sqrt((px - nearestX) * (px - nearestX) + (py - nearestY) * (py - nearestY));
                double coverage = Math.Clamp(radius + 0.5 - distance, 0, 1) * subpixelOpacity;
                if (coverage <= 0)
                {
                    continue;
                }

                int offset = y * stride + x * 4;
                double alpha = coverage * color.A / 255.0;
                pixels[offset] = Blend(pixels[offset], color.B, alpha);
                pixels[offset + 1] = Blend(pixels[offset + 1], color.G, alpha);
                pixels[offset + 2] = Blend(pixels[offset + 2], color.R, alpha);
                pixels[offset + 3] = (byte)Math.Clamp(
                    pixels[offset + 3] + (255 - pixels[offset + 3]) * alpha, 0, 255);
            }
        }
    }

    private static byte Blend(byte background, byte foreground, double alpha) =>
        (byte)Math.Clamp(Math.Round(background + (foreground - background) * alpha), 0, 255);

    private static Color Interpolate(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            Interpolate(from.A, to.A, amount),
            Interpolate(from.R, to.R, amount),
            Interpolate(from.G, to.G, amount),
            Interpolate(from.B, to.B, amount));
    }

    private static byte Interpolate(byte from, byte to, double amount) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * amount), 0, 255);

    private static double Interpolate(double from, double to, double amount) =>
        from + (to - from) * Math.Clamp(amount, 0, 1);
}
