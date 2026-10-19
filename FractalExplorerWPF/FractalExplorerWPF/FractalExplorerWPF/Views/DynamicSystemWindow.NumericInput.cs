using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class DynamicSystemWindow
{
    private void InitializeNumericInput()
    {
        foreach (var (key, box) in _boxes)
        {
            var property = typeof(DynamicSystemState).GetProperty(key)!;
            string label = _fieldLabels[key].Text;
            if (property.PropertyType == typeof(string)) continue;
            if (property.PropertyType == typeof(int))
            {
                bool zeroAllowed = key is "TransientIterations" or "DiscardIterations" or "BifurcationTransient";
                NumericInput.Integer(this, box, label, zeroAllowed ? 0 : 1,
                    _kind == DynamicSystemKind.Hopalong && key == "Iterations" ? 100_000_000 : int.MaxValue);
            }
            else if (key == "Zoom") NumericInput.Real(this, box, label, .01, 1_000_000);
            else if (key == "DensityGamma") NumericInput.Real(this, box, label, .05, 8);
            else if (key == "Dt") NumericInput.Real(this, box, label, double.Epsilon, double.MaxValue);
            else NumericInput.Real(this, box, label, -double.MaxValue, double.MaxValue);
        }
        foreach (var (lower, upper) in new[] { ("AMin", "AMax"), ("BMin", "BMax"),
            ("RMin", "RMax"), ("XMin", "XMax"), ("RangeXMin", "RangeXMax"),
            ("RangeYMin", "RangeYMax"), ("BifurcationRMin", "BifurcationRMax") })
            if (_boxes.TryGetValue(lower, out var a) && _boxes.TryGetValue(upper, out var b))
                NumericInput.OrderedPair(this, a, b);

        foreach (TextBox box in _quadraticBoxes.Values)
            NumericInput.Real(this, box, "Коэффициент карты", -double.MaxValue, double.MaxValue);
        foreach (var (key, box) in _iconBoxes)
        {
            if (key == "Seed") NumericInput.Integer(this, box, "Seed орнамента", 0, int.MaxValue);
            else NumericInput.Real(this, box, key, key == "Span" ? double.Epsilon : -double.MaxValue, double.MaxValue);
        }
        foreach (var (key, box) in _hopalongBoxes)
        {
            (double min, double max) = key switch
            {
                "A" or "C" => (-10000, 10000), "B" => (-100, 100),
                "StartX" or "StartY" => (-1e6, 1e6), "Rotation" => (-360000, 360000),
                "Span" => (.0001, 1e8), _ => throw new InvalidOperationException($"Неизвестный параметр Hopalong: {key}")
            };
            NumericInput.Real(this, box, key, min, max);
        }
        foreach (var (key, box) in _popcornBoxes)
        {
            switch (key)
            {
                case "OrbitIterations": NumericInput.Integer(this, box, "Итерации орбиты", 1, 1000); break;
                case "GridSize": NumericInput.Integer(this, box, "Сетка затравок", 8, 512); break;
                case "HilbertOrder": NumericInput.Integer(this, box, "Порядок Гильберта", 2, 9); break;
                default:
                    (double min, double max) = key switch
                    {
                        "H" => (.0001, .5), "K" => (.1, 12), "SeedSpan" => (.1, 40), "Span" => (.1, 100),
                        _ => throw new InvalidOperationException($"Неизвестный параметр Popcorn: {key}")
                    };
                    NumericInput.Real(this, box, key, min, max); break;
            }
        }
        if (_popcornBoxes.TryGetValue("OrbitIterations", out var iterations))
        {
            NumericInput.Pair(this, _popcornBoxes["GridSize"], iterations, (size, count) =>
                (long)int.Parse(size) * int.Parse(size) * int.Parse(count) <= 100_000_000,
                "Предел — 100 млн шагов затравок.", () => _state.Popcorn.PlotMode == PopcornPlotMode.GridOrbits);
            NumericInput.Pair(this, _popcornBoxes["HilbertOrder"], iterations, (order, count) =>
                (1L << (2 * int.Parse(order))) * int.Parse(count) <= 100_000_000,
                "Предел — 100 млн шагов кривой.", () => _state.Popcorn.PlotMode == PopcornPlotMode.HilbertCurve);
        }
    }
}
