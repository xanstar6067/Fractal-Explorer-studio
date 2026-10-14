using System.Windows.Media;
using FractalExplorerWPF.Models;
using static FractalExplorerWPF.Core.Rendering.Laboratories.LaboratoryHelpers;
using Color = System.Windows.Media.Color;

namespace FractalExplorerWPF.Core.Rendering.Laboratories;

/// <summary>Подстановочные и апериодические мозаики.</summary>
internal static class AperiodicTilingsLaboratory
{
    public static void Render(LaboratoryCanvas canvas, MathematicalLaboratoryState state, CancellationToken token)
    {
        int depth = Math.Clamp(state.PrimaryValue, 1, 9);
        switch (state.Mode)
        {
            case 0:
                RenderRadialTriangles(canvas, state, depth, Math.Clamp(state.SecondaryValue, 5, 20), golden: true, token);
                break;
            case 1:
                RenderAmmannBeenker(canvas, state, depth, token);
                break;
            case 2:
                RenderChair(canvas, state, depth, -0.9, -0.9, 1.8, 0, token);
                break;
            case 3:
                RenderRadialTriangles(canvas, state, depth, 5, golden: false, token);
                break;
            case 4:
                RenderSphinx(canvas, state, depth, new(-0.9, 0.68), new(0.9, 0.68), new(0, -0.88), 0, token);
                break;
            default:
                RenderFibonacciTriangles(canvas, state, depth);
                break;
        }
    }

    private static void RenderRadialTriangles(
        LaboratoryCanvas canvas, MathematicalLaboratoryState state, int depth, int sectors, bool golden,
        CancellationToken token)
    {
        for (int sector = 0; sector < sectors; sector++)
        {
            double a0 = Math.Tau * sector / sectors - Math.PI / 2;
            double a1 = Math.Tau * (sector + 1) / sectors - Math.PI / 2;
            var stack = new Stack<(LaboratoryPoint a, LaboratoryPoint b, LaboratoryPoint c, int depth, int branch)>();
            stack.Push((new(0, 0), new(0.94 * Math.Cos(a0), 0.94 * Math.Sin(a0)),
                new(0.94 * Math.Cos(a1), 0.94 * Math.Sin(a1)), depth, sector));
            while (stack.Count > 0)
            {
                var tile = stack.Pop();
                if (tile.depth == 0)
                {
                    DrawTile(canvas, [tile.a, tile.b, tile.c], state,
                        (tile.branch * 0.137 + sector / (double)sectors) % 1);
                    continue;
                }
                double ratio = golden ? 1 / ((1 + Math.Sqrt(5)) / 2) : 0.5;
                LaboratoryPoint split = Lerp(tile.b, tile.c, ratio);
                stack.Push((tile.a, tile.b, split, tile.depth - 1, tile.branch * 2));
                stack.Push((tile.a, split, tile.c, tile.depth - 1, tile.branch * 2 + 1));
                if ((stack.Count & 2047) == 0) token.ThrowIfCancellationRequested();
            }
        }
    }

    private static void RenderAmmannBeenker(
        LaboratoryCanvas canvas, MathematicalLaboratoryState state, int depth, CancellationToken token)
    {
        int rings = Math.Clamp(depth * 3, 3, 24);
        for (int ring = rings; ring >= 1; ring--)
        {
            double inner = 0.9 * (ring - 1d) / rings;
            double outer = 0.9 * ring / rings;
            int sectors = 8 * Math.Max(1, ring);
            for (int i = 0; i < sectors; i++)
            {
                double a0 = Math.Tau * i / sectors;
                double a1 = Math.Tau * (i + 1) / sectors;
                LaboratoryPoint[] tile =
                [
                    new(inner * Math.Cos(a0), inner * Math.Sin(a0)),
                    new(outer * Math.Cos(a0), outer * Math.Sin(a0)),
                    new(outer * Math.Cos(a1), outer * Math.Sin(a1)),
                    new(inner * Math.Cos(a1), inner * Math.Sin(a1))
                ];
                DrawTile(canvas, tile, state, (ring + i % 8) / (double)(rings + 8));
            }
            token.ThrowIfCancellationRequested();
        }
    }

    private static void RenderChair(
        LaboratoryCanvas canvas, MathematicalLaboratoryState state, int depth,
        double x, double y, double size, int rotation, CancellationToken token)
    {
        if (depth <= 0 || size < 0.008)
        {
            double third = size / 3;
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                {
                    int missingX = rotation is 0 or 3 ? 2 : 0;
                    int missingY = rotation is 0 or 1 ? 0 : 2;
                    if (column == missingX && row == missingY) continue;
                    LaboratoryPoint[] square =
                    [
                        new(x + column * third, y + row * third),
                        new(x + (column + 1) * third, y + row * third),
                        new(x + (column + 1) * third, y + (row + 1) * third),
                        new(x + column * third, y + (row + 1) * third)
                    ];
                    DrawTile(canvas, square, state, (rotation + row + column) / 7d);
                }
            return;
        }
        double half = size / 2;
        RenderChair(canvas, state, depth - 1, x, y, half, (rotation + 1) % 4, token);
        RenderChair(canvas, state, depth - 1, x + half, y, half, rotation, token);
        RenderChair(canvas, state, depth - 1, x, y + half, half, rotation, token);
        token.ThrowIfCancellationRequested();
    }

    private static void RenderSphinx(
        LaboratoryCanvas canvas, MathematicalLaboratoryState state, int depth,
        LaboratoryPoint a, LaboratoryPoint b, LaboratoryPoint c, int branch, CancellationToken token)
    {
        if (depth <= 0)
        {
            DrawTile(canvas, [a, b, c], state, branch * 0.173 % 1);
            return;
        }
        LaboratoryPoint ab = Lerp(a, b, 0.5), bc = Lerp(b, c, 0.5), ca = Lerp(c, a, 0.5);
        RenderSphinx(canvas, state, depth - 1, a, ab, ca, branch * 4, token);
        RenderSphinx(canvas, state, depth - 1, ab, b, bc, branch * 4 + 1, token);
        RenderSphinx(canvas, state, depth - 1, ca, bc, c, branch * 4 + 2, token);
        if ((branch & 255) == 0) token.ThrowIfCancellationRequested();
    }

    private static void RenderFibonacciTriangles(LaboratoryCanvas canvas, MathematicalLaboratoryState state, int depth)
    {
        var fibonacci = new List<int> { 1, 1 };
        for (int i = 2; i < depth + 5; i++) fibonacci.Add(fibonacci[^1] + fibonacci[^2]);
        int rows = Math.Clamp(fibonacci[^1], 5, 144);
        for (int row = 0; row < rows; row++)
        {
            int columns = rows - row;
            double y0 = -0.86 + 1.72 * row / rows;
            double y1 = -0.86 + 1.72 * (row + 1) / rows;
            for (int column = 0; column < columns; column++)
            {
                double x0 = -0.88 + 1.76 * (column + row * 0.5) / rows;
                double x1 = -0.88 + 1.76 * (column + 1 + row * 0.5) / rows;
                double xm = (x0 + x1) / 2;
                DrawTile(canvas, [new(x0, y0), new(x1, y0), new(xm, y1)], state,
                    ((column + fibonacci[row % fibonacci.Count]) % 13) / 13d);
            }
        }
    }

    private static void DrawTile(
        LaboratoryCanvas canvas, IReadOnlyList<LaboratoryPoint> points,
        MathematicalLaboratoryState state, double palettePosition)
    {
        Color color = Palette(state, palettePosition);
        if (state.Filled) canvas.Polygon(points, color, true, 0.76);
        canvas.Polygon(points, state.Filled ? Mix(color, state.BackgroundColor, 0.3) : color,
            false, Math.Clamp(state.Parameter, 0.25, 6));
    }
}
