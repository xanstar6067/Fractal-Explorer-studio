using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public static class KifsRandomizer
{
    /// <summary>Вариации вокруг готовых форм; камера, свет и палитра остаются пользовательскими.</summary>
    public static KifsSettings Create(Random random, KifsSettings? nearby = null)
    {
        KifsSettings settings;
        if (nearby is not null) settings = nearby.Normalized();
        else
        {
            var presets = Fractal3DCatalog.GetPresets(Fractal3DKind.Kifs);
            settings = presets[random.Next(presets.Count)].Kifs.Clone();
        }
        double amount = nearby is null ? 1 : .35;
        // Tetrahedral copies separate quickly under large twists. Keep their random
        // deformation gentler so generated crystals still occupy a useful part of the view.
        double twist = settings.Symmetry == KifsSymmetry.Tetrahedral ? 4 : 12;
        double Jitter(double value, double range) => Math.Round(value + (random.NextDouble() * 2 - 1) * range * amount, 3);
        settings.Scale = Jitter(settings.Scale, .15);
        if (nearby is null && settings.Symmetry == KifsSymmetry.Tetrahedral)
            settings.Scale = Math.Min(settings.Scale, 2);
        settings.RotationX = Jitter(settings.RotationX, twist);
        settings.RotationY = Jitter(settings.RotationY, twist);
        settings.RotationZ = Jitter(settings.RotationZ, twist);
        settings.OffsetX = Jitter(settings.OffsetX, .08);
        settings.OffsetY = Jitter(settings.OffsetY, .08);
        settings.OffsetZ = Jitter(settings.OffsetZ, .08);
        return settings.Normalized();
    }
}
