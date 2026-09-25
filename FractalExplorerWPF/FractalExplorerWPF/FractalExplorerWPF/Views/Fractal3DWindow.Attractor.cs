using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private Attractor3DSettings CaptureAttractor()
    {
        if (Kind != Fractal3DKind.StrangeAttractor)
            return Attractor3DSystems.Default(Attractor3DSystem.Lorenz);
        return new Attractor3DSettings
        {
            System = (Attractor3DSystem)Math.Clamp(AttractorSystemBox.SelectedIndex, 0, 6),
            A = ReadDouble(AttractorABox, "Параметр a", -100, 100),
            B = ReadDouble(AttractorBBox, "Параметр b", -100, 100),
            C = ReadDouble(AttractorCBox, "Параметр c", -100, 100),
            D = ReadDouble(AttractorDBox, "Параметр d", -100, 100),
            E = ReadDouble(AttractorEBox, "Параметр e", -100, 100),
            F = ReadDouble(AttractorFBox, "Параметр f", -100, 100),
            TimeStep = ReadDouble(AttractorStepBox, "Шаг времени", .0001, .05),
            StartX = ReadDouble(AttractorStartXBox, "Начальная координата X", -100, 100),
            StartY = ReadDouble(AttractorStartYBox, "Начальная координата Y", -100, 100),
            StartZ = ReadDouble(AttractorStartZBox, "Начальная координата Z", -100, 100)
        };
    }

    private void LoadAttractor(Attractor3DSettings? source)
    {
        Attractor3DSettings s = source?.Clone() ?? Attractor3DSystems.Default(Attractor3DSystem.Lorenz);
        AttractorSystemBox.SelectedIndex = (int)s.System;
        AttractorABox.Text = Format(s.A);
        AttractorBBox.Text = Format(s.B);
        AttractorCBox.Text = Format(s.C);
        AttractorDBox.Text = Format(s.D);
        AttractorEBox.Text = Format(s.E);
        AttractorFBox.Text = Format(s.F);
        AttractorStepBox.Text = Format(s.TimeStep);
        AttractorStartXBox.Text = Format(s.StartX);
        AttractorStartYBox.Text = Format(s.StartY);
        AttractorStartZBox.Text = Format(s.StartZ);
        UpdateAttractorLabels();
    }

    private void AttractorSystem_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.StrangeAttractor) return;
        _updatingUi = true;
        LoadAttractor(Attractor3DSystems.Default((Attractor3DSystem)Math.Clamp(AttractorSystemBox.SelectedIndex, 0, 6)));
        _updatingUi = false;
        ScheduleRender();
    }

    private void UpdateAttractorLabels()
    {
        Attractor3DSystem system = (Attractor3DSystem)Math.Clamp(AttractorSystemBox.SelectedIndex, 0, 6);
        AttractorDescriptionText.Text = Attractor3DSystems.Description(system);
        string[] names = Attractor3DSystems.ParameterNames(system);
        StackPanel[] fields = [AttractorAField, AttractorBField, AttractorCField,
            AttractorDField, AttractorEField, AttractorFField];
        TextBlock[] labels = [AttractorALabel, AttractorBLabel, AttractorCLabel,
            AttractorDLabel, AttractorELabel, AttractorFLabel];
        for (int i = 0; i < fields.Length; i++)
        {
            fields[i].Visibility = i < names.Length ? Visibility.Visible : Visibility.Collapsed;
            labels[i].Text = i < names.Length ? names[i] : string.Empty;
        }
    }
}
