using System.Windows.Media;
using System.Windows.Media.Imaging;
using FractalExplorerWPF.Core.Rendering.Laboratories;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Core.PercolationMath;

namespace FractalExplorerWPF.Core.Rendering;

/// <summary>
/// Кадр математической лаборатории. Каждая лаборатория — свой класс в
/// <c>Core/Rendering/Laboratories/</c> и рисует на общем <see cref="LaboratoryCanvas"/>.
/// </summary>
public static class MathematicalLaboratoryRenderer
{
    public static async Task<BitmapSource> RenderBitmapAsync(
        MathematicalLaboratoryState state,
        int width,
        int height,
        CancellationToken token,
        IProgress<int>? progress = null,
        IProgress<PercolationStatistics>? percolationStatistics = null)
    {
        width = Math.Clamp(width, 64, 8_192);
        height = Math.Clamp(height, 64, 8_192);
        MathematicalLaboratoryState snapshot = state.Clone();
        progress?.Report(2);
        byte[] pixels = await Task.Run(() => RenderPixels(snapshot, width, height, token, progress, percolationStatistics), token);
        token.ThrowIfCancellationRequested();
        BitmapSource bitmap = BitmapSource.Create(
            width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        progress?.Report(100);
        return bitmap;
    }

    private static byte[] RenderPixels(
        MathematicalLaboratoryState state,
        int width,
        int height,
        CancellationToken token,
        IProgress<int>? progress,
        IProgress<PercolationStatistics>? percolationStatistics)
    {
        LineSampling lines = state.Kind is MathematicalLaboratoryKind.VoronoiLloyd or
            MathematicalLaboratoryKind.RecamanSequence or MathematicalLaboratoryKind.KnotStudio or
            MathematicalLaboratoryKind.StochasticMotion or MathematicalLaboratoryKind.KleinianSchottky
            ? LineSampling.Fine
            : LineSampling.Standard;
        var canvas = new LaboratoryCanvas(width, height, state, lines);
        canvas.Clear(state.BackgroundColor);
        progress?.Report(6);
        switch (state.Kind)
        {
            case MathematicalLaboratoryKind.ModularArithmetic:
                ModularArithmeticLaboratory.Render(canvas, state, token);
                break;
            case MathematicalLaboratoryKind.PascalModulo:
                PascalModuloLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.RationalNumbers:
                RationalNumbersLaboratory.Render(canvas, state, token);
                break;
            case MathematicalLaboratoryKind.PrimeGeometry:
                PrimeGeometryLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.Phyllotaxis:
                PhyllotaxisLaboratory.Render(canvas, state, token);
                break;
            case MathematicalLaboratoryKind.CircleInversion:
                CircleInversionLaboratory.Render(canvas, state, token);
                break;
            case MathematicalLaboratoryKind.AperiodicTilings:
                AperiodicTilingsLaboratory.Render(canvas, state, token);
                break;
            case MathematicalLaboratoryKind.HyperbolicGeometry:
                HyperbolicGeometryLaboratory.Render(canvas, state, token);
                break;
            case MathematicalLaboratoryKind.FourierEpicycles:
                FourierEpicyclesLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.ChladniWaveInterference:
                ChladniWaveInterferenceLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.VoronoiLloyd:
                VoronoiLloydLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.RecamanSequence:
                RecamanSequenceLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.KnotStudio:
                KnotStudioLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.StochasticMotion:
                StochasticMotionLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.KleinianSchottky:
                KleinianSchottkyLaboratory.Render(canvas, state, token, progress);
                break;
            case MathematicalLaboratoryKind.Percolation:
                PercolationLaboratory.Render(canvas, state, token, progress, percolationStatistics);
                break;
            case MathematicalLaboratoryKind.Rauzy:
                RauzyLaboratory.Render(canvas, state, token, progress);
                break;
        }
        progress?.Report(98);
        return canvas.Pixels;
    }
}
