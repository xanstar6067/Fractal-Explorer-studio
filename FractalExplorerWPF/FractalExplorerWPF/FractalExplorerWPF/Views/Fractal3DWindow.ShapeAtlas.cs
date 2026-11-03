using System.Globalization;
using System.Windows;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private void CahnHilliardAtlas_OnClick(object? sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.CahnHilliard3D) OpenShapeAtlas(CreateCahnHilliardAtlas);
    }

    private CahnHilliard3DAtlasWindow CreateCahnHilliardAtlas()
    {
        var editor = (CahnHilliard3DEditor)_simulation!.Editor;
        editor.Simulation.Cancel();
        var source = CaptureState("atlas");
        var atlas = new CahnHilliard3DAtlasWindow(source) { Owner = this };
        atlas.ParametersApplied += async (_, _) =>
        {
            try
            {
                var result = await atlas.PrepareSelectionAsync(CaptureViewState("atlas"));
                if (_isClosing) return;
                editor.ApplyAtlasResult(result.CahnHilliard, source.CahnHilliard);
                atlas.Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusText.Text = $"Не удалось применить вариант Кана–Хиллиарда: {ex.Message}"; }
        };
        return atlas;
    }

    private void TuringAtlas_OnClick(object? sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.Turing3D) OpenShapeAtlas(CreateTuringAtlas);
    }

    private Turing3DAtlasWindow CreateTuringAtlas()
    {
        var editor = (Turing3DEditor)_simulation!.Editor;
        editor.Simulation.Cancel();
        var source = CaptureState("atlas");
        var atlas = new Turing3DAtlasWindow(source) { Owner = this };
        atlas.ParametersApplied += async (_, _) =>
        {
            try
            {
                var result = await atlas.PrepareSelectionAsync(CaptureViewState("atlas"));
                if (_isClosing) return;
                editor.ApplyAtlasResult(result.Turing, source.Turing);
                atlas.Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusText.Text = $"Не удалось применить вариант Тьюринга: {ex.Message}"; }
        };
        return atlas;
    }

    private void GrayScottAtlas_OnClick(object? sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.GrayScott3D) OpenShapeAtlas(CreateGrayScottAtlas);
    }

    private GrayScott3DAtlasWindow CreateGrayScottAtlas()
    {
        var editor = (GrayScott3DEditor)_simulation!.Editor;
        editor.Simulation.Cancel();
        var source = CaptureState("atlas");
        var atlas = new GrayScott3DAtlasWindow(source) { Owner = this };
        atlas.ParametersApplied += async (_, _) =>
        {
            try
            {
                var result = await atlas.PrepareSelectionAsync(CaptureViewState("atlas"));
                if (_isClosing) return;
                editor.ApplyAtlasResult(result.GrayScott, source.GrayScott);
                atlas.Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusText.Text = $"Не удалось применить вариант Gray–Scott: {ex.Message}"; }
        };
        return atlas;
    }

    private void LichtenbergAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.Lichtenberg3D) OpenShapeAtlas(CreateLichtenbergAtlas);
    }

    private Lichtenberg3DAtlasWindow CreateLichtenbergAtlas()
    {
        _lichtenbergRunning = false; _lichtenbergRequested = _lichtenbergCount;
        _renderCts?.Cancel(); UpdateLichtenbergLabels();
        return ConnectShapeAtlas(new Lichtenberg3DAtlasWindow(CaptureViewState("atlas")), state =>
        {
            bool boundary = _lichtenbergBoundary && state.Lichtenberg.SameGrowth(_lichtenbergSettings);
            LoadLichtenberg(state.Lichtenberg);
            _lichtenbergBoundary = boundary; UpdateLichtenbergLabels();
        });
    }

    private void BuddhabrotAtlas_OnClick(object? sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.Buddhabrot4D) OpenShapeAtlas(CreateBuddhabrotAtlas);
    }

    private Buddhabrot4DAtlasWindow CreateBuddhabrotAtlas() =>
        ConnectShapeAtlas(new Buddhabrot4DAtlasWindow(CaptureViewState("atlas")), state => BuddhabrotEditor.Load(state.Buddhabrot));

    private void LSystemAtlas_OnClick(object? sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.LSystem3D) OpenShapeAtlas(CreateLSystemAtlas);
    }

    private LSystem3DAtlasWindow CreateLSystemAtlas()
    {
        LSystemEditor.PrepareAtlas();
        return ConnectShapeAtlas(new LSystem3DAtlasWindow(CaptureViewState("atlas")), state => LSystemEditor.ApplyAtlasParameters(state.LSystem));
    }

    private void DlaAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.Dla3D) OpenShapeAtlas(CreateDlaAtlas);
    }

    private Dla3DAtlasWindow CreateDlaAtlas()
    {
        // A pending batch may be ahead of the last complete frame. Freeze the displayed count.
        _dlaRunning = false; _dlaRequestedCount = _dlaCount;
        _renderCts?.Cancel(); UpdateDlaLabels();
        return ConnectShapeAtlas(new Dla3DAtlasWindow(CaptureViewState("atlas")), state => LoadDla(state.Dla));
    }

    private void AttractorAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.StrangeAttractor) OpenShapeAtlas(CreateAttractorAtlas);
    }

    private Attractor3DAtlasWindow CreateAttractorAtlas() =>
        ConnectShapeAtlas(new Attractor3DAtlasWindow(CaptureViewState("atlas")), state => LoadAttractor(state.Attractor));

    private void ApollonianPackingAtlas_OnClick(object sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.ApollonianPacking) OpenShapeAtlas(CreateApollonianPackingAtlas);
    }

    private ApollonianPackingAtlasWindow CreateApollonianPackingAtlas() =>
        ConnectShapeAtlas(new ApollonianPackingAtlasWindow(CaptureViewState("atlas")), state =>
        {
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
            ColoringModeBox.SelectedIndex = (int)state.ColoringMode;
        });

    private void CubeRuleAtlas_OnClick(object sender, EventArgs e)
    {
        if (_mode.HasCubeRule) OpenShapeAtlas(CreateCubeRuleAtlas);
    }

    private CubeRuleAtlasWindow CreateCubeRuleAtlas() =>
        ConnectShapeAtlas(new CubeRuleAtlasWindow(CaptureViewState("atlas")), state =>
        {
            CubeEditor.ApplyAtlasRule(state.CubeRuleMask);
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
            CubeThicknessPanel.IsEnabled = !state.CubeRuleMask.HasValue;
        });

    private void BurningShipAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.BurningShip) OpenShapeAtlas(CreateBurningShipAtlas);
    }

    private BurningShipAtlasWindow CreateBurningShipAtlas() =>
        ConnectShapeAtlas(new BurningShipAtlasWindow(CaptureViewState("atlas")), state =>
        {
            PowerBox.Text = state.Power.ToString("R", CultureInfo.InvariantCulture);
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        });

    private void TerrainAtlas_OnClick(object sender, EventArgs e)
    {
        if (Kind == Fractal3DKind.Terrain) OpenShapeAtlas(CreateTerrainAtlas);
    }

    private TerrainAtlasWindow CreateTerrainAtlas() =>
        ConnectShapeAtlas(new TerrainAtlasWindow(CaptureViewState("atlas")), state => LoadTerrain(state.Terrain));

    private void AntoineAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.AntoineNecklace) OpenShapeAtlas(CreateAntoineAtlas);
    }

    private AntoineAtlasWindow CreateAntoineAtlas() =>
        ConnectShapeAtlas(new AntoineAtlasWindow(CaptureViewState("atlas")), state =>
        {
            AntoineEditor.Load(state.Antoine);
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        });

    private void KleinianAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.Kleinian) OpenShapeAtlas(CreateKleinianAtlas);
    }

    private KleinianAtlasWindow CreateKleinianAtlas() =>
        ConnectShapeAtlas(new KleinianAtlasWindow(CaptureViewState("atlas")), state =>
        {
            KleinianEditor.Load(state.Kleinian);
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        });

    private void Lyapunov3DAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.Lyapunov3D) OpenShapeAtlas(CreateLyapunov3DAtlas);
    }

    private Lyapunov3DAtlasWindow CreateLyapunov3DAtlas() =>
        ConnectShapeAtlas(new Lyapunov3DAtlasWindow(CaptureViewState("atlas")), state =>
            WriteLyapunovAtlasParameters(state.Lyapunov));

    private void Newton3DAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.Newton3D) OpenShapeAtlas(CreateNewton3DAtlas);
    }

    private Newton3DAtlasWindow CreateNewton3DAtlas() =>
        ConnectShapeAtlas(new Newton3DAtlasWindow(CaptureViewState("atlas")), state =>
            NewtonEditor.ApplyAtlasParameters(state.Newton));

    private void ImplicitSurfaceAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.ImplicitSurface) OpenShapeAtlas(CreateImplicitSurfaceAtlas);
    }

    private ImplicitSurfaceAtlasWindow CreateImplicitSurfaceAtlas() =>
        ConnectShapeAtlas(new ImplicitSurfaceAtlasWindow(CaptureViewState("atlas")), state =>
            LoadImplicitSurfaceParameters(state.ImplicitSurface));

    private void SierpinskiAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.SierpinskiTetrahedron) OpenShapeAtlas(CreateSierpinskiAtlas);
    }

    private SierpinskiAtlasWindow CreateSierpinskiAtlas() =>
        ConnectShapeAtlas(new SierpinskiAtlasWindow(CaptureViewState("atlas")), state =>
        {
            SierpinskiScaleBox.Text = state.SierpinskiScale.ToString("R", CultureInfo.InvariantCulture);
            IterationsBox.Text = state.Iterations.ToString(CultureInfo.InvariantCulture);
        });

    private void PeriodicSurfaceAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind == Fractal3DKind.PeriodicSurfaces) OpenShapeAtlas(CreatePeriodicSurfaceAtlas);
    }

    private PeriodicSurfaceAtlasWindow CreatePeriodicSurfaceAtlas() =>
        ConnectShapeAtlas(new PeriodicSurfaceAtlasWindow(CaptureViewState("atlas")), state =>
            LoadPeriodicSurface(state.PeriodicSurface));

    private void KifsAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind != Fractal3DKind.Kifs) return;
        OpenShapeAtlas(CreateKifsAtlas);
    }

    private KifsAtlasWindow CreateKifsAtlas() =>
        ConnectShapeAtlas(new KifsAtlasWindow(CaptureViewState("atlas")), state =>
        {
            _kifsHistory.Push(CaptureKifs());
            LoadKifs(state.Kifs);
        });

    private void BulbBoxHybridAtlas_OnClick(object sender, RoutedEventArgs e)
    {
        if (Kind != Fractal3DKind.BulbBoxHybrid) return;
        OpenShapeAtlas(CreateBulbBoxHybridAtlas);
    }

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

    private BulbBoxHybridAtlasWindow CreateBulbBoxHybridAtlas() =>
        ConnectShapeAtlas(new BulbBoxHybridAtlasWindow(CaptureViewState("atlas"), HybridEditor.SelectedStageIndex), state =>
        {
            if (state.HybridSequence is { } sequence)
                HybridEditor.ApplyAtlasStage(HybridEditor.SelectedStageIndex, sequence.Stages[HybridEditor.SelectedStageIndex]);
            else
            {
                PowerBox.Text = state.Power.ToString("R", CultureInfo.InvariantCulture);
                BoxScaleBox.Text = state.BoxScale.ToString("R", CultureInfo.InvariantCulture);
                BoxMinRadiusBox.Text = state.BoxMinRadius.ToString("R", CultureInfo.InvariantCulture);
                BoxFoldingBox.Text = state.BoxFoldingLimit.ToString("R", CultureInfo.InvariantCulture);
                HybridMixBox.Text = state.HybridMix.ToString("R", CultureInfo.InvariantCulture);
                HybridBulbStepsBox.Text = state.HybridBulbSteps.ToString(CultureInfo.InvariantCulture);
                HybridBoxStepsBox.Text = state.HybridBoxSteps.ToString(CultureInfo.InvariantCulture);
            }
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
