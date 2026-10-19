using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.NewtonMath;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class MandelbrotWindow
{
    private void InitializeNumericInput()
    {
        NumericInput.Decimal(this, PowerBox, "степень", _definition.HasInversion ? -12m : 0.1m, 12m, minimumMagnitude: 0.1m);
        NumericInput.Integer(this, IterationsBox, "итерации", 50, MaxIterations);
        NumericInput.Decimal(this, ThresholdBox, "порог выхода", 0.1m, 1_000m);
        NumericInput.Decimal(this, JuliaRealBox, "действительная часть C", -10m, 10m);
        NumericInput.Decimal(this, JuliaImaginaryBox, "мнимая часть C", -10m, 10m);
        NumericInput.Real(this, HistogramContrastBox, "контраст", 0.1, 4);
        NumericInput.Real(this, SmoothBlendPowerBox, "степень смешивания", 0.1, 5);
        NumericInput.Real(this, SmoothIterationOffsetBox, "сдвиг итерации", -100, 100);
        NumericInput.Real(this, PalettePhaseBox, "фаза палитры", -2, 2);
        NumericInput.Real(this, PaletteScaleBox, "масштаб палитры", -100, 100);
        NumericInput.Real(this, OrbitStrengthBox, "сила ловушки", 0, 5);
        NumericInput.Real(this, OrbitBiasBox, "смещение ловушки", -1, 1);
        NumericInput.Real(this, StripeFrequencyBox, "частота полос", 0.1, 20);
        NumericInput.Real(this, StripeStrengthBox, "сила полос", 0, 1);
        NumericInput.Real(this, StripeBiasBox, "смещение полос", -1, 1);
        NumericInput.Real(this, PolyABox, "коэффициент A", 0, 30);
        NumericInput.Real(this, PolyBBox, "коэффициент B", 0, 30);
        NumericInput.Real(this, PolyCBox, "коэффициент C", 0, 30);
        NumericInput.Real(this, PolyGammaBox, "гамма полинома", 0.1, 5);
        NumericInput.Real(this, PolyBlendBox, "смешивание полинома", 0, 1);
        NumericInput.Real(this, PolyBiasBox, "смещение полинома", -1, 1);
        NumericInput.Real(this, DistanceReliefBox, "сила рельефа", 0, 20);
        NumericInput.Real(this, DistanceLightAzimuthBox, "азимут света", -360, 360);
        NumericInput.Real(this, DistanceLightElevationBox, "высота света", 0, 90);
        NumericInput.Real(this, DistanceAmbientBox, "ambient", 0, 1);
        NumericInput.Real(this, DistanceDiffuseBox, "diffuse", 0, 2);
        NumericInput.Real(this, DistanceSpecularBox, "specular", 0, 2);
        NumericInput.Real(this, DistanceShininessBox, "блеск", 1, 256);
        NumericInput.Real(this, DistanceContourSpacingBox, "шаг контуров", 2, 100);
        NumericInput.Real(this, DistanceContourStrengthBox, "сила контуров", 0, 1);
        NumericInput.Zoom(this, ZoomBox, MinZoom, EffectiveMaxZoom, () => _viewport.Zoom);
    }
}
