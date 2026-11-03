using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FractalExplorerWPF.Controls;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Views;

/// <summary>Независимые GPU-прогоны от общего поля и перенос результата без хранения всех полей сетки.</summary>
public abstract class GpuSimulationParameterAtlasWindow<TSettings, TField, TVolume> : Fractal3DParameterAtlasWindow
    where TSettings : class, IGpuSimulationSettings, new()
    where TField : class
    where TVolume : class, IGpuVolumeTicket<object>
{
    protected sealed class SimulationOptions
    {
        public required Func<Fractal3DState, TSettings> Get { get; init; }
        public required Action<Fractal3DState, TSettings> Set { get; init; }
        public required Func<Direct3DDeviceHost, TSettings, GpuVolumeSimulation<TSettings, TField, TVolume>> Create { get; init; }
        public required Func<TSettings, TField?, TVolume?, TSettings> With { get; init; }
        public required Func<TSettings, TField?> Field { get; init; }
        // Некоторые модели создают исходный шум вокруг равновесия, зависящего от коэффициентов.
        public bool FreezeInitialField { get; init; }
    }
    private readonly SimulationOptions _options;
    private TField? _initial;
    private readonly TSettings _initialSettings;
    private GpuVolumeSimulation<TSettings, TField, TVolume>? _simulation;
    private TVolume? _volume;
    private TextBox _stepsBox = null!;

    protected GpuSimulationParameterAtlasWindow(Fractal3DState source, SimulationOptions options) : base(source)
    { _options = options; _initialSettings = options.With(options.Get(source), options.Field(options.Get(source)), null); _initial = options.Field(_initialSettings); }

    protected void AddTrialSteps(int steps)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Шагов от общего начального поля (1–10 000)", VerticalAlignment = VerticalAlignment.Center });
        _stepsBox = new TextBox { Text = steps.ToString(CultureInfo.InvariantCulture), Width = 90, Margin = new Thickness(10, 0, 0, 0) };
        RegisterName("TrialStepsBox", _stepsBox); panel.Children.Add(_stepsBox);
        ((StackPanel)FindName("AtlasSettingsPanel")).Children.Insert(2, panel);
        NumericSpinner.SetIsEnabled(_stepsBox, true);
        NumericInput.Integer(this, _stepsBox, "Шагов сравнения", 1, 10000);
        _stepsBox.TextChanged += (_, _) => InvalidateSelection("Число шагов изменено. Нажмите «Построить».");
    }
    private int TrialSteps => int.Parse(_stepsBox.Text, CultureInfo.InvariantCulture);
    protected override Func<Fractal3DState, CancellationToken, Task> CreatePreviewPreparation()
    {
        int steps = TrialSteps;
        return (state, token) => RunTrialAsync(state, steps, token);
    }
    private async Task RunTrialAsync(Fractal3DState state, int steps, CancellationToken token)
    {
        TSettings settings = _options.Get(state);
        var live = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_initial is null && _options.FreezeInitialField)
            {
                if (_simulation is null) _simulation = _options.Create(PreviewDevice, _initialSettings);
                else _simulation.Reset(_initialSettings);
                _initial = _simulation.ReadCheckpoint(_simulation.Publish());
                token.ThrowIfCancellationRequested();
            }
            settings = _options.With(settings, _initial, null);
            if (_simulation is null) _simulation = _options.Create(PreviewDevice, settings);
            else _simulation.Reset(settings);
            _simulation.Advance(steps, token);
            token.ThrowIfCancellationRequested();
            return _simulation.Publish();
        }, token);
        _volume = live; _options.Set(state, _options.With(settings, null, live));
    }
    public Task<Fractal3DState> PrepareSelectionAsync(Fractal3DState current)
    {
        NumericInput.CommitAll(this);
        var selected = current.Clone(); ApplySelectionTo(selected); int steps = TrialSteps;
        return RunExclusiveAsync("Повторяем выбранный прогон и переносим точное поле…", async token =>
        {
            await RunTrialAsync(selected, steps, token);
            var field = await Task.Run(() => _simulation!.ReadCheckpoint(_volume!), token);
            _options.Set(selected, _options.With(_options.Get(selected), field, null));
            return selected;
        });
    }
    protected override void ReleaseRenderResources()
    { _simulation?.Dispose(); _simulation = null; base.ReleaseRenderResources(); }
}
