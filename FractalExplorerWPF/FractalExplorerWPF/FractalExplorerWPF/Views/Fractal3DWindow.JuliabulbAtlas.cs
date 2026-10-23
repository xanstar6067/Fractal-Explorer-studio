using System.Globalization;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void JuliabulbAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind != Fractal3DKind.Juliabulb) return;
        NumericInput.CommitAll(this);
        _juliabulbMapTimer?.Stop();
        _juliabulbMapCts?.Cancel();
        try { SuspendLive(() => CreateJuliabulbAtlas().ShowDialog()); }
        finally { ScheduleJuliabulbMapRender(); }
    }

    private JuliabulbAtlasWindow CreateJuliabulbAtlas()
    {
        var atlas = new JuliabulbAtlasWindow(CaptureViewState("atlas")) { Owner = this };
        atlas.ParametersApplied += (_, _) =>
        {
            var state = CaptureViewState("atlas");
            atlas.ApplySelectionTo(state);
            bool updating = _updatingUi; _updatingUi = true;
            try { WriteJuliabulbParameters(state); }
            finally { _updatingUi = updating; }
            _renderCts?.Cancel();
            ScheduleJuliabulbMapRender();
            ScheduleRender(immediate: true);
            atlas.Close();
        };
        return atlas;
    }

    private void WriteJuliabulbParameters(Fractal3DState state)
    {
        JuliaCXBox.Text = state.JuliaCX.ToString("R", CultureInfo.InvariantCulture);
        JuliaCYBox.Text = state.JuliaCY.ToString("R", CultureInfo.InvariantCulture);
        JuliaCZBox.Text = state.JuliaCZ.ToString("R", CultureInfo.InvariantCulture);
        PowerBox.Text = state.Power.ToString("R", CultureInfo.InvariantCulture);
    }
}
