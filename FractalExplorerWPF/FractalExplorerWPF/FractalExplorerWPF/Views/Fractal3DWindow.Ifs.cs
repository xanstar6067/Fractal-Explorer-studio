using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private readonly List<Ifs3DTransform> _ifsTransforms = [];
    private bool _ifsEditing;

    private List<Ifs3DTransform> CaptureIfsTransforms()
    {
        if (Kind != Fractal3DKind.Ifs3D) return [];
        if (_ifsTransforms.Count == 0) throw new InvalidOperationException("Добавьте хотя бы одно преобразование IFS.");
        if (IfsTransformList.SelectedIndex >= 0 &&
            (IfsMatrixBoxes().Any(box => !TryReadDouble(box.Text, out double number) || !double.IsFinite(number)) ||
             !TryReadDouble(IfsProbabilityBox.Text, out double probability) || probability < 0))
            throw new InvalidOperationException("Закончите ввод матрицы и неотрицательного веса.");
        foreach (Ifs3DTransform transform in _ifsTransforms)
        {
            double[] values = IfsValues(transform);
            if (values.Any(value => !double.IsFinite(value) || Math.Abs(value) > 1000) || transform.Probability < 0)
                throw new InvalidOperationException("Коэффициенты IFS должны быть конечными и не больше 1000 по модулю.");
        }
        if (_ifsTransforms.Sum(transform => transform.Probability) <= 0)
            throw new InvalidOperationException("Сумма весов IFS должна быть положительной.");
        return _ifsTransforms.Select(transform => transform.Clone()).ToList();
    }

    private void LoadIfsTransforms(IReadOnlyList<Ifs3DTransform> transforms)
    {
        _ifsTransforms.Clear();
        _ifsTransforms.AddRange(transforms.Select(transform => transform.Clone()));
        RebindIfsTransforms(0);
    }

    private void RebindIfsTransforms(int selected)
    {
        _ifsEditing = true;
        IfsTransformList.ItemsSource = null;
        IfsTransformList.ItemsSource = _ifsTransforms;
        IfsTransformList.SelectedIndex = _ifsTransforms.Count == 0 ? -1 : Math.Clamp(selected, 0, _ifsTransforms.Count - 1);
        _ifsEditing = false;
        ShowIfsTransform();
    }

    private void IfsTransformList_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ifsEditing) ShowIfsTransform();
    }

    private void ShowIfsTransform()
    {
        _ifsEditing = true;
        try
        {
            int selected = IfsTransformList.SelectedIndex;
            Ifs3DTransform? transform = selected >= 0 && selected < _ifsTransforms.Count ? _ifsTransforms[selected] : null;
            TextBox[] boxes = IfsMatrixBoxes();
            double[] values = transform is null ? new double[13] : IfsValues(transform);
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].IsEnabled = transform is not null;
                boxes[i].Text = transform is null ? string.Empty : Format(values[i]);
            }
        }
        finally { _ifsEditing = false; }
    }

    private void IfsMatrix_OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_ifsEditing || _updatingUi || Kind != Fractal3DKind.Ifs3D || IfsTransformList.SelectedIndex < 0) return;
        TextBox[] boxes = IfsMatrixBoxes();
        double[] values = new double[13];
        for (int i = 0; i < boxes.Length; i++)
            if (!TryReadDouble(boxes[i].Text, out values[i]) || !double.IsFinite(values[i])) return;
        if (values[12] < 0) return;
        Ifs3DTransform t = _ifsTransforms[IfsTransformList.SelectedIndex];
        (t.M11, t.M12, t.M13, t.Tx, t.M21, t.M22, t.M23, t.Ty,
            t.M31, t.M32, t.M33, t.Tz, t.Probability) =
            (values[0], values[1], values[2], values[3], values[4], values[5], values[6],
                values[7], values[8], values[9], values[10], values[11], values[12]);
        int selected = IfsTransformList.SelectedIndex;
        ClearIfsPreset();
        _ifsEditing = true;
        IfsTransformList.Items.Refresh();
        IfsTransformList.SelectedIndex = selected;
        _ifsEditing = false;
        ScheduleRender();
    }

    private void IfsAdd_OnClick(object sender, RoutedEventArgs e)
    {
        _ifsTransforms.Add(Ifs3DTransform.Contract(.5, .25, .25, .25));
        ClearIfsPreset();
        RebindIfsTransforms(_ifsTransforms.Count - 1);
        ScheduleRender();
    }

    private void IfsDuplicate_OnClick(object sender, RoutedEventArgs e)
    {
        int selected = IfsTransformList.SelectedIndex;
        if (selected < 0) return;
        _ifsTransforms.Insert(selected + 1, _ifsTransforms[selected].Clone());
        ClearIfsPreset();
        RebindIfsTransforms(selected + 1);
        ScheduleRender();
    }

    private void IfsDelete_OnClick(object sender, RoutedEventArgs e)
    {
        int selected = IfsTransformList.SelectedIndex;
        if (selected < 0) return;
        _ifsTransforms.RemoveAt(selected);
        ClearIfsPreset();
        RebindIfsTransforms(selected);
        ScheduleRender();
    }

    private void ClearIfsPreset()
    {
        _updatingUi = true;
        PresetBox.SelectedIndex = -1;
        _updatingUi = false;
    }

    private TextBox[] IfsMatrixBoxes() =>
        [IfsM11Box, IfsM12Box, IfsM13Box, IfsTxBox,
            IfsM21Box, IfsM22Box, IfsM23Box, IfsTyBox,
            IfsM31Box, IfsM32Box, IfsM33Box, IfsTzBox, IfsProbabilityBox];

    private static double[] IfsValues(Ifs3DTransform t) =>
        [t.M11, t.M12, t.M13, t.Tx, t.M21, t.M22, t.M23, t.Ty,
            t.M31, t.M32, t.M33, t.Tz, t.Probability];
}
