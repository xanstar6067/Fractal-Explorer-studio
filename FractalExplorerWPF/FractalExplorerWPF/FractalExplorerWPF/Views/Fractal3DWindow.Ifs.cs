using System.Numerics;
using System.Windows;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private readonly List<Ifs3DTransform> _ifsTransforms = [];

    private List<Ifs3DTransform> CaptureIfsTransforms()
    {
        if (Kind != Fractal3DKind.Ifs3D) return [];
        if (_ifsTransforms.Count == 0)
            throw new InvalidOperationException("Добавьте хотя бы одно преобразование IFS.");
        foreach (Ifs3DTransform transform in _ifsTransforms)
        {
            double[] values = Values(transform);
            if (values.Any(value => !double.IsFinite(value) || Math.Abs(value) > 1000) ||
                !double.IsFinite(transform.Probability) || transform.Probability < 0)
                throw new InvalidOperationException("Матрица IFS содержит недопустимое значение.");
        }
        if (_ifsTransforms.Sum(transform => transform.Probability) <= 0)
            throw new InvalidOperationException("Сумма весов IFS должна быть положительной.");
        return _ifsTransforms.Select(transform => transform.Clone()).ToList();
    }

    private void LoadIfsTransforms(IReadOnlyList<Ifs3DTransform> transforms)
    {
        _ifsTransforms.Clear();
        _ifsTransforms.AddRange(transforms.Select(transform => transform.Clone()));
    }

    private void IfsTransforms_OnClick(object sender, RoutedEventArgs e)
    {
        int originalPreset = PresetBox.SelectedIndex;
        List<Ifs3DTransform> original = CaptureIfsTransforms();
        var editor = new Ifs3DTransformEditorWindow(_ifsTransforms) { Owner = this };
        editor.TransformsPreviewed += transforms =>
        {
            LoadIfsTransforms(transforms);
            ClearIfsPreset();
            ScheduleRender();
        };
        editor.Randomized += () => BeginTransition(new Fractal3DPose(_orientation, 3, Vector3.Zero));
        editor.ShowDialog();
        if (originalPreset >= 0 && original.Count == _ifsTransforms.Count &&
            original.Zip(_ifsTransforms).All(pair =>
                pair.First.Probability == pair.Second.Probability &&
                Values(pair.First).SequenceEqual(Values(pair.Second))))
        {
            _updatingUi = true;
            PresetBox.SelectedIndex = originalPreset;
            _updatingUi = false;
        }
    }

    private void ClearIfsPreset()
    {
        _updatingUi = true;
        PresetBox.SelectedIndex = -1;
        _updatingUi = false;
    }

    private static double[] Values(Ifs3DTransform t) =>
        [t.M11, t.M12, t.M13, t.Tx, t.M21, t.M22, t.M23, t.Ty,
            t.M31, t.M32, t.M33, t.Tz];
}
