using System.Numerics;
using System.Windows;
using System.Windows.Input;
using FractalExplorerWPF.Core.Rendering3D;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Controls;

/// <summary>Что окно трёхмерных фракталов даёт режиму с живым моделированием на ГП.</summary>
internal interface IFractal3DSimulationHost
{
    Direct3DDeviceHost DeviceHost { get; }
    bool IsClosing { get; }
    bool IsSuspended { get; }
    /// <summary>Экран подготовки поверх холста, общий для всех моделирований.</summary>
    PreparationOverlay SimulationPreparation { get; }
    void BeginSimulationPreparation(string message);
    /// <summary>Строка времени и кнопки «Продолжить» / «Один кадр» поверх холста.</summary>
    void ShowTransport(string time, string playText, bool playEnabled, bool stepEnabled);
    void RequestDraftFrame();
    void ScheduleRender();
    /// <summary>Отменяет начатый кадр и сразу рисует новый — после перезапуска моделирования.</summary>
    void RestartRender();
    void ShowStatus(string text);
    void ShowPreparationError(string message);
    void UpdateCancelAvailability();
    void SetControlsEnabled(bool enabled);
    void SetColoringModeName(Fractal3DColoringMode mode, string name);
    /// <summary>Точка выбранной плоскости среза под курсором в долях ребра куба, [0; 1]³.</summary>
    bool TryPickCutPlane(MouseButtonEventArgs e, int axis, double position, out Vector3 point);
    Fractal3DPose Pose { get; }
    void MoveCameraTo(Fractal3DPose pose);
    /// <summary>Вписывает в кадр шар с центром и радиусом в координатах куба [−1; 1]³.</summary>
    void FitCameraTo(Vector3 center, double radius);
}

/// <summary>Режим окна с живым моделированием — всё, что окну нужно о нём знать.</summary>
internal interface IFractal3DSimulationMode
{
    FrameworkElement Editor { get; }
    bool IsPreparing { get; }
    /// <summary>Моделирование развивается: кадры идут как при движении камеры, без доводки.</summary>
    bool IsRunning { get; }
    /// <summary>Порция шагов считается на ГП.</summary>
    bool IsBusy { get; }
    /// <summary>Подготовка, на время которой панель и камера недоступны.</summary>
    bool IsInputLocked { get; }
    /// <summary>Идут шаги, расчёт или поиск — кнопке «Отмена» есть что останавливать.</summary>
    bool IsActive { get; }
    void Load(Fractal3DState state);
    /// <summary>Вид для кадра: показанный кадр без чтения поля с ГП.</summary>
    void CaptureInto(Fractal3DState state);
    /// <summary>Точное поле показанного кадра для файла сохранения.</summary>
    void CheckpointInto(Fractal3DState state);
    /// <summary>Подставляет кадр в пути; false — поле ещё готовится и рисовать нечего.</summary>
    bool PrepareFrame(Fractal3DState state);
    bool IsCurrentFrame(Fractal3DState state);
    void OnFrameDisplayed(Fractal3DState state);
    void OnFrameFailed(Fractal3DState state);
    void TogglePlay();
    void StepOnce();
    void StopPreparation();
    void Cancel();
    /// <summary>Модальное окно поверх: подача шагов останавливается, поданные публикуются и покажутся после.</summary>
    void Suspend();
    void Close();
    /// <summary>Shift+щелчок по холсту — кисть на плоскости среза; true, если событие обработано.</summary>
    bool TryBrush(MouseButtonEventArgs e);
}

/// <summary>Панель параметров конкретного моделирования.</summary>
internal interface IFractal3DSimulationEditor<TSettings>
{
    FrameworkElement View { get; }
    int StepsPerFrame { get; }
    bool IsSearching { get; }
    /// <summary>Заполняет поля применёнными параметрами.</summary>
    void Show(TSettings settings);
    /// <summary>Применённые параметры с полями вида из панели (уровень, срез, скорость).</summary>
    TSettings CaptureView(TSettings applied);
    /// <summary>Подписи и доступность кнопок панели после изменения состояния.</summary>
    void Refresh();
    bool TryBrush(MouseButtonEventArgs e);
    void CancelSearch();
}

/// <summary>
/// Живое моделирование окна: моделирование и рендер делят устройство Direct3D окна. Порция шагов
/// публикует на ГП новый кадр, рендер рисует его без копий через ЦП, и только показанный кадр
/// запускает следующую порцию. В оперативную память поле попадает лишь при сохранении
/// (<see cref="Checkpoint"/>). Работает на потоке интерфейса; шаги идут в пуле потоков.
/// </summary>
internal sealed class Fractal3DLiveSimulation<TSimulation, TSettings, TField, TVolume> : IFractal3DSimulationMode
    where TSimulation : GpuVolumeSimulation<TSettings, TField, TVolume>
    where TSettings : class, IGpuSimulationSettings, new()
    where TField : class
    where TVolume : class, IGpuVolumeTicket<object>
{
    /// <summary>Итог работы над полем перед шагами: заменено ли поле и что сделать на потоке интерфейса после публикации.</summary>
    public readonly record struct WorkResult(bool ReplacedField, Action? Completed);

    public sealed class Options
    {
        /// <summary>Название в сообщении об ошибке расчёта («Ошибка Lenia 3D: …»).</summary>
        public required string Title { get; init; }
        public required Func<Direct3DDeviceHost, TSettings, TSimulation> Create { get; init; }
        public required Func<Fractal3DState, TSettings> Get { get; init; }
        public required Action<Fractal3DState, TSettings> Set { get; init; }
        public required Func<TSettings, TVolume?> Live { get; init; }
        public required Func<TSettings, TField?> Field { get; init; }
        /// <summary>Параметры с другим живым кадром и контрольной точкой.</summary>
        public required Func<TSettings, TVolume?, TField?, TSettings> With { get; init; }
        /// <summary>Шагов подготовки после загрузки.</summary>
        public required Func<TSettings, int> InitialSteps { get; init; }
        public required Func<TSettings, string> PreparationMessage { get; init; }
        /// <summary>Строка времени поверх кадра без состояния («Шаг 1 200 · 64³»).</summary>
        public required Func<TVolume?, TSettings, string> Time { get; init; }
        /// <summary>Сообщение экрана подготовки по ходу шагов; null — оставить исходное.</summary>
        public Func<int, int, string>? ProgressMessage { get; init; }
        /// <summary>Сообщение, пока опубликованное поле рисуется первый раз.</summary>
        public string? RenderingMessage { get; init; }
        public bool DisableControlsWhilePreparing { get; init; }
        /// <summary>Новое правило для текущего поля перед следующей порцией, без сброса времени.</summary>
        public Action<TSimulation, TSettings>? Configure { get; init; }
    }

    private readonly IFractal3DSimulationHost _host;
    private readonly IFractal3DSimulationEditor<TSettings> _editor;
    private readonly Options _options;
    private readonly List<(string? Key, Func<TSimulation, TSettings, WorkResult> Run)> _work = [];
    /// <summary>С чего перезапустить моделирование перед следующей порцией (загрузка, «начать заново»).</summary>
    private TSettings? _resetTo;
    private TSimulation? _simulation;
    private CancellationTokenSource? _cts;
    private TVolume? _preparationFrame;
    private string? _workPreparation;
    private bool _configure, _preparationStopped;
    private int _queuedSteps;

    public Fractal3DLiveSimulation(IFractal3DSimulationHost host, IFractal3DSimulationEditor<TSettings> editor, Options options)
    {
        _host = host;
        _editor = editor;
        _options = options;
    }

    /// <summary>Применённое уравнение и вид; <c>Field</c> — контрольная точка, с которой начато моделирование.</summary>
    public TSettings Settings { get; private set; } = new();
    public TVolume? Shown { get; private set; }
    public TVolume? Pending { get; private set; }
    public bool Running { get; private set; }
    public bool Busy { get; private set; }
    public bool Preparing { get; private set; }
    /// <summary>Растёт при каждой загрузке: результаты прежнего поля отбрасываются.</summary>
    public int Epoch { get; private set; }
    public string Device { get; private set; } = "";
    public bool ResetPending => _resetTo is not null;
    /// <summary>Показанный кадр и никакой работы в пути.</summary>
    public bool Ready => !Busy && Pending is null && Shown is not null;
    public bool CanBrush => Ready && !_editor.IsSearching && !_host.IsClosing && !_host.IsSuspended;

    public FrameworkElement Editor => _editor.View;
    public bool IsPreparing => Preparing;
    public bool IsRunning => Running;
    public bool IsBusy => Busy;
    public bool IsInputLocked => Preparing && _options.DisableControlsWhilePreparing;
    public bool IsActive => Busy || Running || _editor.IsSearching;

    public void Load(Fractal3DState state) => Load(_options.Get(state));

    public void Load(TSettings? settings)
    {
        settings ??= new();
        var s = _options.With(settings, null, _options.Field(settings));
        s.Validate();
        _editor.CancelSearch();
        Epoch++; _cts?.Cancel(); Running = false;
        _work.Clear(); _workPreparation = null; _configure = false;
        Shown = Pending = null;
        Settings = s; _resetTo = s;
        Device = _simulation?.DeviceName ?? "ГП · подготовка устройства";
        _editor.Show(s);
        _queuedSteps = _options.InitialSteps(s);
        BeginPreparation(_options.PreparationMessage(s));
        NotifyChanged();
        _ = RunAsync();
    }

    /// <summary>«Применить и начать заново», находка поиска или возврат формы.</summary>
    public void Restart(TSettings settings)
    {
        Load(settings);
        _host.RestartRender();
    }

    public TSettings Capture() =>
        _options.With(_editor.CaptureView(Settings), Shown, Shown is null ? _options.Field(Settings) : null);

    public void CaptureInto(Fractal3DState state) => _options.Set(state, Capture());

    /// <summary>Показанный кадр как точное поле для файла сохранения: единственное чтение поля с ГП.</summary>
    public TSettings Checkpoint(TSettings settings) => _options.Live(settings) is { } live
        ? _options.With(settings, null, ((TSimulation)live.Source).ReadCheckpoint(live))
        : settings;

    public void CheckpointInto(Fractal3DState state) => _options.Set(state, Checkpoint(_options.Get(state)));

    public bool PrepareFrame(Fractal3DState state)
    {
        var settings = _options.Get(state);
        if (Pending is not null)
            _options.Set(state, settings = _options.With(settings, Pending, _options.Field(settings)));
        if (_options.Live(settings) is not null) return true;
        if (Busy) _host.ShowStatus(_options.PreparationMessage(Settings));
        return false;
    }

    public bool IsCurrentFrame(Fractal3DState state) => Equals(_options.Live(_options.Get(state)), Pending ?? Shown);

    public void TogglePlay()
    {
        if (Preparing || _editor.IsSearching) return;
        if (Running) { Pause(); return; }
        Running = true; NotifyChanged();
        if (Pending is null && !Busy)
        { _queuedSteps = _editor.StepsPerFrame; _ = RunAsync(); }
    }

    /// <summary>Отмена не теряет шагов: уже поданные целиком публикуются и показываются.</summary>
    public void Pause()
    {
        if (Preparing) { StopPreparation(); return; }
        Running = false; _queuedSteps = 0; _cts?.Cancel();
        NotifyChanged(); _host.RequestDraftFrame();
    }

    /// <summary>Подготовка останавливается на целом шаге, и достигнутое поле публикуется.</summary>
    public void StopPreparation()
    {
        if (!Preparing) return;
        _preparationStopped = true; Running = false; _queuedSteps = 0;
        _cts?.Cancel();
        _host.SimulationPreparation.Message = "Завершаем текущий шаг…";
        _host.SimulationPreparation.IsActionEnabled = false;
        NotifyChanged();
    }

    public void StepOnce()
    {
        if (Preparing || Busy || Pending is not null || _editor.IsSearching) return;
        Running = false; _queuedSteps = _editor.StepsPerFrame; _ = RunAsync();
    }

    /// <summary>Кисть по показанному кадру: поле меняется без шагов времени.</summary>
    public void Brush(Action<TSimulation> paint)
    {
        if (!CanBrush) return;
        _work.Add((null, (simulation, _) => { paint(simulation); return default; }));
        _queuedSteps = 0; _ = RunAsync();
    }

    /// <summary>
    /// Работа над полем перед следующей порцией шагов (перенос на другую сетку). Работа с тем же
    /// ключом заменяет ещё не начатую.
    /// </summary>
    public void Enqueue(string key, Func<TSimulation, TSettings, WorkResult> work, string? preparationMessage = null)
    {
        _work.RemoveAll(item => item.Key == key);
        _work.Add((key, work));
        if (preparationMessage is not null) _workPreparation = preparationMessage;
        if (!Busy && Pending is null) _ = RunAsync();
    }

    /// <summary>Новое правило для текущего поля: действует со следующей порции, время не сбрасывается.</summary>
    public void Reconfigure(TSettings rule)
    {
        Settings = rule;
        _configure = true;
    }

    /// <summary>Применённые параметры, уже совпадающие с полем на ГП (после переноса на другую сетку).</summary>
    public void Replace(TSettings settings) => Settings = settings;

    public void Cancel()
    {
        _editor.CancelSearch();
        Pause();
    }

    public void Suspend()
    {
        _editor.CancelSearch();
        _cts?.Cancel(); _queuedSteps = 0;
    }

    public void Close()
    {
        _editor.CancelSearch();
        Running = false; Epoch++; _cts?.Cancel();
        if (!Busy) DisposeSimulation();
    }

    public bool TryBrush(MouseButtonEventArgs e) => _editor.TryBrush(e);

    public void OnFrameDisplayed(Fractal3DState state)
    {
        if (_host.IsClosing || _host.IsSuspended) return;
        var live = _options.Live(_options.Get(state));
        if (Pending is not null && Equals(live, Pending))
        {
            Shown = Pending; Pending = null;
            if (Preparing && Equals(live, _preparationFrame)) EndPreparation();
        }
        NotifyChanged();
        if (Busy || Pending is not null) return;
        if (Running) { _queuedSteps = _editor.StepsPerFrame; _ = RunAsync(); }
        else if (_work.Count > 0) _ = RunAsync();
    }

    public void OnFrameFailed(Fractal3DState state)
    {
        if (!IsCurrentFrame(state)) return;
        Running = false;
        if (Preparing) EndPreparation();
        NotifyChanged();
    }

    /// <summary>Подписи поверх кадра, панель и кнопка «Отмена».</summary>
    public void NotifyChanged()
    {
        bool searching = _editor.IsSearching;
        _host.ShowTransport(_options.Time(Shown, Settings) + " · " + (Running ? "развивается" : Busy ? "расчёт…" : "пауза"),
            Running ? "Ⅱ Пауза" : "▶ Продолжить", !Preparing && !searching, Ready && !Running && !searching);
        _editor.Refresh();
        _host.UpdateCancelAvailability();
    }

    private void BeginPreparation(string message)
    {
        Preparing = true; _preparationStopped = false; _preparationFrame = null;
        _host.BeginSimulationPreparation(message);
        if (_options.DisableControlsWhilePreparing) _host.SetControlsEnabled(false);
    }

    private void EndPreparation()
    {
        Preparing = false; _preparationFrame = null;
        _host.SimulationPreparation.IsIndeterminate = false;
        _host.SimulationPreparation.Visibility = Visibility.Collapsed;
        if (_options.DisableControlsWhilePreparing) _host.SetControlsEnabled(true);
    }

    /// <summary>
    /// Одна порция работы на ГП: перезапуск или новое правило, работа над полем (кисть, перенос),
    /// шаги и публикация кадра. Новая порция начинается только после показа опубликованного кадра.
    /// </summary>
    private async Task RunAsync()
    {
        if (Busy || _host.IsClosing || _host.IsSuspended) return;
        int steps = _queuedSteps, epoch = Epoch;
        var reset = _resetTo ?? (_simulation is null ? Settings : null);
        var rule = Settings;
        bool configure = _configure && reset is null;
        var work = _work.Select(item => item.Run).ToArray();
        if (_workPreparation is { } workMessage) BeginPreparation(workMessage);
        bool preparing = Preparing;
        var keep = Shown;
        _queuedSteps = 0; _work.Clear(); _workPreparation = null; _resetTo = null; _configure = false;
        Busy = true;
        var cts = new CancellationTokenSource(); _cts = cts;
        if (preparing && _preparationStopped) cts.Cancel();
        var overlay = _host.SimulationPreparation;
        IProgress<int>? progress = preparing ? new Progress<int>(done =>
        {
            if (_host.IsClosing || epoch != Epoch || !Preparing || _preparationFrame is not null) return;
            overlay.IsIndeterminate = false;
            overlay.Progress = steps > 0 ? 90.0 * done / steps : 90;
            if (_options.ProgressMessage is { } message && !_preparationStopped) overlay.Message = message(done, steps);
        }) : null;
        var host = _host.DeviceHost;
        var simulation = _simulation;
        var completions = new List<Action>();
        NotifyChanged();
        try
        {
            var volume = await Task.Run(() =>
            {
                if (reset is not null)
                {
                    if (simulation is null) simulation = _options.Create(host, reset);
                    else simulation.Reset(reset);
                    keep = null;
                }
                else if (configure) _options.Configure?.Invoke(simulation!, rule);
                foreach (var item in work)
                {
                    var result = item(simulation!, rule);
                    if (result.ReplacedField) keep = null;
                    if (result.Completed is { } completed) completions.Add(completed);
                }
                progress?.Report(0);
                simulation!.Advance(steps, cts.Token, progress);
                return simulation.Publish(keep);
            });
            Device = simulation!.DeviceName;
            if (_host.IsClosing || epoch != Epoch) return;
            foreach (var completed in completions) completed();
            Pending = volume;
            if (preparing)
            {
                _preparationFrame = volume;
                overlay.IsIndeterminate = false;
                overlay.Progress = 90;
                if (!_preparationStopped && _options.RenderingMessage is { } message) overlay.Message = message;
            }
            _host.RequestDraftFrame();
        }
        catch (Exception exception)
        {
            if (epoch == Epoch && !_host.IsClosing)
            {
                Running = false;
                Device = "Сбой расчёта на ГП; показан последний кадр. " + exception.Message;
                _host.ShowStatus($"Ошибка {_options.Title}: " + exception.Message);
                EndPreparation();
                _host.ShowPreparationError(exception.Message);
            }
        }
        finally
        {
            _simulation = simulation;
            Busy = false; if (ReferenceEquals(_cts, cts)) _cts = null; cts.Dispose();
            if (_host.IsClosing) DisposeSimulation();
            else
            {
                NotifyChanged();
                if (Running && !_host.IsSuspended && Pending is null && _queuedSteps == 0 && _resetTo is null)
                    _queuedSteps = _editor.StepsPerFrame;
                if (Pending is null && (_queuedSteps > 0 || _resetTo is not null || _work.Count > 0))
                    _ = RunAsync();
            }
        }
    }

    // Освобождение ждёт очереди устройства (полоса кадра), поэтому уходит с UI-потока.
    private void DisposeSimulation()
    {
        var simulation = _simulation; _simulation = null;
        if (simulation is not null) Task.Run(simulation.Dispose);
    }
}
