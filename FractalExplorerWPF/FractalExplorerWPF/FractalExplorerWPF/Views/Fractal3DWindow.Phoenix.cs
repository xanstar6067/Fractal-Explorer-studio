using System.Globalization;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

public partial class Fractal3DWindow
{
    private readonly List<Phoenix3DParameters> _phoenixHistory = [];

    private void AttachPhoenixExplorer()
    {
        PhoenixExplorer.AtlasRequested += (_, _) =>
        {
            NumericInput.CommitAll(this);
            SuspendLive(() => CreatePhoenixAtlas().ShowDialog());
        };
        PhoenixExplorer.VariationRequested += (_, _) =>
        {
            NumericInput.CommitAll(this);
            var (radius, target) = PhoenixExplorer.CaptureVariation();
            var current = Phoenix3DParameters.FromState(CaptureViewState("variation"));
            ApplyPhoenixParameters(current.Vary(Random.Shared, radius, target));
        };
        PhoenixExplorer.UndoRequested += (_, _) =>
        {
            NumericInput.CommitAll(this);
            if (_phoenixHistory.Count == 0) return;
            var previous = _phoenixHistory[^1];
            _phoenixHistory.RemoveAt(_phoenixHistory.Count - 1);
            WritePhoenixAndRender(previous);
        };
    }

    private Phoenix3DAtlasWindow CreatePhoenixAtlas()
    {
        var atlas = new Phoenix3DAtlasWindow(CaptureViewState("atlas")) { Owner = this };
        atlas.ParametersApplied += (_, _) =>
        {
            var current = Phoenix3DParameters.FromState(CaptureViewState("atlas"));
            ApplyPhoenixParameters(current.WithAtlasPoint(atlas.SelectedConstants));
            atlas.Close();
        };
        return atlas;
    }

    private void ApplyPhoenixParameters(Phoenix3DParameters next)
    {
        var state = CaptureViewState("parameters");
        var previous = Phoenix3DParameters.FromState(state);
        next.ApplyTo(state); // Проверить весь набор до изменения полей и истории.
        if (previous == next) return;
        if (_phoenixHistory.Count == 32) _phoenixHistory.RemoveAt(0);
        _phoenixHistory.Add(previous);
        WritePhoenixAndRender(next);
    }

    private void WritePhoenixAndRender(Phoenix3DParameters parameters)
    {
        bool updating = _updatingUi; _updatingUi = true;
        try { WritePhoenixParameters(parameters); }
        finally { _updatingUi = updating; }
        PhoenixExplorer.SetUndoAvailable(_phoenixHistory.Count > 0);
        _renderCts?.Cancel(); ScheduleRender(immediate: true);
    }

    private void WritePhoenixParameters(Phoenix3DParameters parameters)
    {
        var boxes = new[] { JuliaCXBox, JuliaCYBox, JuliaCZBox, PhoenixMemoryXBox, PhoenixMemoryYBox, PhoenixMemoryZBox };
        var values = parameters.Components;
        for (int i = 0; i < boxes.Length; i++) boxes[i].Text = values[i].ToString("R", CultureInfo.InvariantCulture);
    }
}
