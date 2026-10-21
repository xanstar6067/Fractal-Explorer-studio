using System.Globalization;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private static string QuaternionNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private double? _quaternionDisplayedSlice;
    private bool _quaternionWasPlaying;
    private bool QuaternionSlicePlaying => Kind == Fractal3DKind.QuaternionJulia && QuaternionEditor.IsPlaying;

    private void AttachQuaternionEditor()
    {
        QuaternionEditor.SliceRequested += value =>
        {
            if (_isClosing || _suspended || !QuaternionEditor.IsPlaying) return;
            _updatingUi = true;
            SliceBox.Text = QuaternionNumber(value);
            _updatingUi = false;
            ScheduleRender(immediate: true);
        };
        QuaternionEditor.AtlasRequested += (_, _) =>
        {
            NumericInput.CommitAll(this);
            SuspendLive(() =>
            {
                var atlas = CreateQuaternionAtlas();
                atlas.ParametersApplied += (_, _) => { ApplyQuaternionConstant(atlas.SelectedConstant); atlas.Close(); };
                atlas.ShowDialog();
            });
        };
    }

    private void QuaternionEditor_OnChanged(object sender, EventArgs e)
    {
        if (_updatingUi || Kind != Fractal3DKind.QuaternionJulia) return;
        if (_quaternionWasPlaying && !QuaternionEditor.IsPlaying) RestoreQuaternionSlice();
        _quaternionWasPlaying = QuaternionEditor.IsPlaying;
        _renderCts?.Cancel();
        ScheduleRender(immediate: QuaternionEditor.IsPlaying);
    }

    private void StopQuaternionAnimation()
    {
        bool wasPlaying = QuaternionEditor.IsPlaying || _quaternionWasPlaying;
        QuaternionEditor.Stop(); _quaternionWasPlaying = false;
        if (wasPlaying) RestoreQuaternionSlice();
    }

    private void RestoreQuaternionSlice()
    {
        if (_quaternionDisplayedSlice is not double slice) return;
        bool updating = _updatingUi; _updatingUi = true;
        SliceBox.Text = QuaternionNumber(slice);
        _updatingUi = updating;
    }

    private QuaternionJuliaAtlasWindow CreateQuaternionAtlas() => new(CaptureViewState("atlas")) { Owner = this };

    private void ApplyQuaternionConstant(decimal[] point)
    {
        var state = CaptureViewState("constant");
        QuaternionJuliaAtlasWindow.SetConstant(state, point);
        _updatingUi = true;
        WriteQuaternionConstant(state);
        _updatingUi = false;
        _renderCts?.Cancel(); ScheduleRender(immediate: true);
    }

    private void WriteQuaternionConstant(Fractal3DState state)
    {
        JuliaCXBox.Text = QuaternionNumber(state.JuliaCX); JuliaCYBox.Text = QuaternionNumber(state.JuliaCY);
        JuliaCZBox.Text = QuaternionNumber(state.JuliaCZ); JuliaCWBox.Text = QuaternionNumber(state.JuliaCW);
    }
}
