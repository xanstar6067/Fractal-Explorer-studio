using System.Globalization;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void MandelbulbAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind != Fractal3DKind.Mandelbulb) return;
        OpenShapeAtlas(CreateMandelbulbAtlas);
    }

    private void MandelboxAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind != Fractal3DKind.Mandelbox) return;
        OpenShapeAtlas(CreateMandelboxAtlas);
    }

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

    private void OpenShapeAtlas(Func<Fractal3DParameterAtlasWindow> createAtlas)
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

    private MandelbulbAtlasWindow CreateMandelbulbAtlas() =>
        ConnectShapeAtlas(new MandelbulbAtlasWindow(CaptureViewState("atlas")), state =>
        {
            PowerBox.Text = state.Power.ToString("R", CultureInfo.InvariantCulture);
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        });

    private MandelboxAtlasWindow CreateMandelboxAtlas() =>
        ConnectShapeAtlas(new MandelboxAtlasWindow(CaptureViewState("atlas")), state =>
        {
            BoxScaleBox.Text = state.BoxScale.ToString("R", CultureInfo.InvariantCulture);
            BoxMinRadiusBox.Text = state.BoxMinRadius.ToString("R", CultureInfo.InvariantCulture);
            BoxFoldingBox.Text = state.BoxFoldingLimit.ToString("R", CultureInfo.InvariantCulture);
        });

    private T ConnectShapeAtlas<T>(T atlas, Action<Fractal3DState>? writeParameters = null) where T : Fractal3DParameterAtlasWindow
    {
        atlas.Owner = this;
        atlas.ParametersApplied += (_, _) =>
        {
            var state = CaptureViewState("atlas");
            atlas.ApplySelectionTo(state);
            bool updating = _updatingUi; _updatingUi = true;
            try { (writeParameters ?? WriteJulia3DShapeParameters)(state); }
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
