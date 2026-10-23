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
        OpenShapeAtlas(CreateJuliabulbAtlas);
    }

    private void BurningShipJuliaAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind != Fractal3DKind.BurningShipJulia) return;
        OpenShapeAtlas(CreateBurningShipJuliaAtlas);
    }

    private void OpenShapeAtlas(Func<Julia3DShapeAtlasWindow> createAtlas)
    {
        NumericInput.CommitAll(this);
        _juliabulbMapTimer?.Stop();
        _juliabulbMapCts?.Cancel();
        try { SuspendLive(() => createAtlas().ShowDialog()); }
        finally { ScheduleJuliabulbMapRender(); }
    }

    private JuliabulbAtlasWindow CreateJuliabulbAtlas() =>
        ConnectShapeAtlas(new JuliabulbAtlasWindow(CaptureViewState("atlas")));

    private BurningShipJuliaAtlasWindow CreateBurningShipJuliaAtlas() =>
        ConnectShapeAtlas(new BurningShipJuliaAtlasWindow(CaptureViewState("atlas")));

    private T ConnectShapeAtlas<T>(T atlas) where T : Julia3DShapeAtlasWindow
    {
        atlas.Owner = this;
        atlas.ParametersApplied += (_, _) =>
        {
            var state = CaptureViewState("atlas");
            atlas.ApplySelectionTo(state);
            bool updating = _updatingUi; _updatingUi = true;
            try { WriteJulia3DShapeParameters(state); }
            finally { _updatingUi = updating; }
            _renderCts?.Cancel();
            ScheduleJuliabulbMapRender();
            ScheduleRender(immediate: true);
            atlas.Close();
        };
        return atlas;
    }

    private void WriteJulia3DShapeParameters(Fractal3DState state)
    {
        JuliaCXBox.Text = state.JuliaCX.ToString("R", CultureInfo.InvariantCulture);
        JuliaCYBox.Text = state.JuliaCY.ToString("R", CultureInfo.InvariantCulture);
        JuliaCZBox.Text = state.JuliaCZ.ToString("R", CultureInfo.InvariantCulture);
        PowerBox.Text = state.Power.ToString("R", CultureInfo.InvariantCulture);
    }
}
