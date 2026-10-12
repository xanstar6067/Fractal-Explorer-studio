using System.Diagnostics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Параметры моделирования, которое целиком идёт на ГП в кубической сетке.</summary>
public interface IGpuSimulationSettings
{
    int Size { get; }
    void Validate();
}

/// <summary>
/// Опубликованный кадр моделирования. Билет неизменяем; данные слота живут на ГП, пока в него
/// не опубликован следующий кадр.
/// </summary>
public interface IGpuVolumeTicket<out TSimulation>
{
    TSimulation Source { get; }
    long Version { get; }
    long Step { get; }
    int Size { get; }
    int Slot { get; }
}

/// <summary>
/// Общий каркас объёмных моделирований на ГП (Gray–Scott, Тьюринг, Кан–Хиллиард, Lenia, Physarum,
/// Кобаяси, возбудимые среды). Поле не покидает видеокарту: <see cref="Publish"/> пишет его в слот,
/// который рендер того же <see cref="Direct3DDeviceHost"/> читает напрямую, а в оперативную память
/// поле попадает только по запросу — для сохранения и проверок.
/// Слотов публикации два: новый кадр пишется мимо показанного, поэтому кадр, который ещё рисуется
/// или сохраняется, не меняется под ним. Шаги подаются только целиком, поэтому отмена между
/// порциями оставляет согласованное поле.
/// Методы без суффикса <c>Locked</c> сами встают в очередь устройства; с суффиксом — вызываются
/// под уже взятым <see cref="Direct3DDeviceHost.Gate"/> (подготовка превью внутри рендера).
/// Наследник выделяет свои буферы, описывает один шаг, публикацию в слот и чтение поля.
/// </summary>
public abstract class GpuVolumeSimulation<TSettings, TField, TVolume> : IDisposable
    where TSettings : class, IGpuSimulationSettings, new()
    where TVolume : class, IGpuVolumeTicket<object>
{
    private protected const int SlotCount = 2;

    private readonly IReadOnlyList<ShaderCacheEntry> _shaderEntries;
    private readonly string _sourceName;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private readonly ID3D11Query[] _fences = new ID3D11Query[2];
    private readonly bool[] _fencePending = new bool[2];
    private readonly long[] _slotVersions = new long[SlotCount];
    private ID3D11Buffer? _constants;
    private ID3D11Buffer? _staging;
    private int _fenceIndex, _lastSlot = 1;
    private long _step, _version;
    private bool _disposed;

    /// <param name="sourceName">Имя исходника в сообщениях компилятора шейдеров.</param>
    /// <param name="name">Название режима в сообщениях об ошибках («Кадр … уже заменён»).</param>
    /// <param name="parameterCount">Число float в константном буфере; кратно четырём.</param>
    private protected GpuVolumeSimulation(Direct3DDeviceHost host, IReadOnlyList<ShaderCacheEntry> shaderEntries,
        string sourceName, string name, int parameterCount)
    {
        Host = host;
        _shaderEntries = shaderEntries;
        _sourceName = sourceName;
        Name = name;
        Parameters = new float[parameterCount];
    }

    /// <summary>Действующее правило; <c>Field</c> в нём всегда null — поле живёт на ГП.</summary>
    public TSettings Settings { get; private protected set; } = new();
    public string DeviceName => "ГП · " + Host.AdapterName;
    public int Size => Settings.Size;
    public long Step => Volatile.Read(ref _step);

    private protected Direct3DDeviceHost Host { get; }
    private protected string Name { get; }
    /// <summary>Константный буфер шейдеров; после изменения поставить <see cref="ConstantsDirty"/>.</summary>
    private protected float[] Parameters { get; }
    private protected bool ConstantsDirty { get; set; } = true;
    /// <summary>Номер шага текущего поля: базовый класс увеличивает его после каждого <see cref="StepLocked"/>.</summary>
    private protected long CurrentStep { get => _step; set => _step = value; }

    /// <summary>Шагов в одной порции подачи на ГП.</summary>
    private protected abstract int StepsPerSubmit { get; }
    private protected virtual double FenceTimeoutSeconds => 15;
    private protected virtual string TimeoutMessage => $"ГП не завершил шаги {Name}.";
    /// <summary>Отказ рисовать кадр, слот которого уже переписан более новым.</summary>
    private protected virtual bool ViewRequiresCurrentFrame => true;

    /// <summary>Наследник вызывает в конце своего конструктора.</summary>
    private protected void Initialize(TSettings settings, bool gateHeld)
    {
        settings.Validate();
        Host.AddRef();
        bool entered = false, created = false;
        try
        {
            if (!gateHeld) { Host.Gate.Wait(); entered = true; }
            ID3D11Device device = Host.Device;
            foreach (var entry in _shaderEntries)
                _shaders[entry.EntryPoint] = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, _sourceName, e.Profile)).Span);
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(Parameters.Length * 4),
                Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            for (int i = 0; i < _fences.Length; i++)
                _fences[i] = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            CreateResourcesLocked();
            ResetLocked(settings);
            created = true;
        }
        finally
        {
            if (!created) DisposeResources();
            if (entered) Host.Gate.Release();
            // The last reference destroys the device together with its gate: only after the gate is free.
            if (!created) Host.Release();
        }
    }

    internal bool Uses(Direct3DDeviceHost host) => ReferenceEquals(Host, host);

    /// <summary>Новое уравнение и поле (сохранённое или стартовая затравка); ресурсы переживают смену при той же сетке.</summary>
    public void Reset(TSettings settings)
    {
        settings.Validate();
        Host.Gate.Wait();
        try { ResetLocked(settings); }
        finally { Host.Gate.Release(); }
    }

    internal void ResetLocked(TSettings settings)
    {
        ThrowIfDisposed();
        settings.Validate();
        ResetCoreLocked(settings);
        Array.Clear(_slotVersions);
    }

    /// <summary>
    /// Ставит шаги в очередь ГП порциями и не держит устройство между ними: полосы кадра идут
    /// вперемежку. Ждёт не конца работы, а предпоследней порции — видеокарта не простаивает,
    /// пока поток спит. Отмена прекращает подачу новых порций; уже поданные шаги — целые шаги,
    /// поэтому поле остаётся согласованным. Возвращает число поданных шагов.
    /// </summary>
    public int Advance(int steps, CancellationToken token, IProgress<int>? progress = null)
    {
        if (steps < 0) throw new ArgumentOutOfRangeException(nameof(steps));
        int done = 0;
        while (done < steps && !token.IsCancellationRequested)
        {
            int count = Math.Min(StepsPerSubmit, steps - done);
            if (!TryEnter(token)) break;
            try { SubmitStepsLocked(count); }
            finally { Host.Gate.Release(); }
            done += count;
            WaitForFence(_fenceIndex ^ 1, token, holdingGate: false);
            progress?.Report(done);
        }
        return done;
    }

    /// <summary>Ждёт, пока ГП выполнит все поданные шаги.</summary>
    public void Synchronize(CancellationToken token) => WaitForFence(_fenceIndex, token, holdingGate: false);

    /// <summary>Подготовка под взятой очередью: отмена бросает исключение, поле остаётся целым.</summary>
    internal void AdvanceLocked(int steps, CancellationToken token)
    {
        for (int done = 0; done < steps;)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(StepsPerSubmit, steps - done);
            SubmitStepsLocked(count);
            done += count;
            WaitForFence(_fenceIndex ^ 1, token, holdingGate: true);
        }
        token.ThrowIfCancellationRequested();
    }

    /// <summary>Публикует текущее поле для рендера и сохранения, не трогая слот кадра <paramref name="keep"/>.</summary>
    public TVolume Publish(TVolume? keep = null)
    {
        Host.Gate.Wait();
        try { return PublishLocked(keep); }
        finally { Host.Gate.Release(); }
    }

    internal TVolume PublishLocked(TVolume? keep = null)
    {
        ThrowIfDisposed();
        int index = IsCurrent(keep) ? keep!.Slot ^ 1 : _lastSlot ^ 1;
        PublishToSlotLocked(index);
        _version++;
        _slotVersions[index] = _version;
        _lastSlot = index;
        return CreateVolume(_version, index);
    }

    private protected bool IsCurrent(TVolume? volume) =>
        volume is not null && volume.Slot is >= 0 and < SlotCount && ReferenceEquals(volume.Source, this) &&
        volume.Size == Settings.Size && _slotVersions[volume.Slot] == volume.Version;

    /// <summary>Текстура опубликованного кадра; вызывается рендером под очередью устройства.</summary>
    internal ID3D11ShaderResourceView ViewLocked(TVolume volume)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        if (ViewRequiresCurrentFrame && !IsCurrent(volume)) throw new InvalidOperationException($"Кадр {Name} уже заменён.");
        return DisplayView(volume.Slot);
    }

    /// <summary>Точное поле опубликованного кадра — контрольная точка для сохранения.</summary>
    public TField ReadCheckpoint(TVolume volume)
    {
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        Host.Gate.Wait();
        try
        {
            ThrowIfDisposed();
            if (!IsCurrent(volume)) throw new InvalidOperationException($"Кадр {Name} уже заменён более новым.");
            return ReadSlotLocked(volume);
        }
        finally { Host.Gate.Release(); }
    }

    /// <summary>Текущее поле, включая ещё не опубликованные шаги.</summary>
    public TField ReadCurrent()
    {
        Host.Gate.Wait();
        try { ThrowIfDisposed(); return ReadCurrentLocked(); }
        finally { Host.Gate.Release(); }
    }

    /// <summary>Буферы, которые не зависят от сетки; вызывается до первого сброса.</summary>
    private protected virtual void CreateResourcesLocked() { }
    /// <summary>Сетка, поле, <see cref="Settings"/>, <see cref="CurrentStep"/> и параметры шейдеров.</summary>
    private protected abstract void ResetCoreLocked(TSettings settings);
    /// <summary>Один целый шаг; счётчик шагов растёт после него.</summary>
    private protected abstract void StepLocked();
    private protected abstract void PublishToSlotLocked(int slot);
    private protected abstract TVolume CreateVolume(long version, int slot);
    private protected abstract ID3D11ShaderResourceView DisplayView(int slot);
    private protected abstract TField ReadSlotLocked(TVolume volume);
    private protected abstract TField ReadCurrentLocked();
    /// <summary>Все собственные ресурсы наследника: поле, слоты, вспомогательные буферы.</summary>
    private protected abstract void DisposeResourcesCore();

    private protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>Загружает изменённые параметры и ставит шейдер; привязку ресурсов делает наследник.</summary>
    private protected ID3D11DeviceContext BindLocked(string entry)
    {
        ThrowIfDisposed();
        ID3D11DeviceContext context = Host.Context;
        if (ConstantsDirty)
        {
            var mapped = context.Map(_constants!, MapMode.WriteDiscard);
            try { Marshal.Copy(Parameters, 0, mapped.DataPointer, Parameters.Length); }
            finally { context.Unmap(_constants!, 0); }
            ConstantsDirty = false;
        }
        context.CSSetShader(_shaders[entry]); context.CSSetConstantBuffer(0, _constants!);
        return context;
    }

    /// <summary>Запуск по всему кубу сетки группами заданного размера.</summary>
    private protected void DispatchCube(uint groupX, uint groupY, uint groupZ)
    {
        uint n = (uint)Settings.Size;
        Host.Context.Dispatch((n + groupX - 1) / groupX, (n + groupY - 1) / groupY, (n + groupZ - 1) / groupZ);
    }

    /// <summary>Копия буфера ГП в оперативную память через общий промежуточный буфер.</summary>
    private protected float[] ReadFloatsLocked(ID3D11Buffer source)
    {
        uint bytes = source.Description.ByteWidth;
        if (_staging is null || _staging.Description.ByteWidth != bytes)
        {
            _staging?.Dispose();
            _staging = Host.Device.CreateBuffer(new BufferDescription
                { ByteWidth = bytes, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        }
        ID3D11DeviceContext context = Host.Context;
        context.CopyResource(_staging, source);
        var mapped = context.Map(_staging, MapMode.Read);
        float[] values = new float[bytes / 4];
        try { Marshal.Copy(mapped.DataPointer, values, 0, values.Length); }
        finally { context.Unmap(_staging, 0); }
        return values;
    }

    private void SubmitStepsLocked(int count)
    {
        ThrowIfDisposed();
        for (int i = 0; i < count; i++)
        {
            StepLocked();
            _step++;
        }
        _fenceIndex ^= 1;
        Host.Context.End(_fences[_fenceIndex]);
        _fencePending[_fenceIndex] = true;
        Host.Context.Flush();
    }

    private void WaitForFence(int index, CancellationToken token, bool holdingGate)
    {
        if (!_fencePending[index]) return;
        var watch = Stopwatch.StartNew();
        using var waiter = new HighResolutionWaiter();
        while (!token.IsCancellationRequested)
        {
            if (!holdingGate && !TryEnter(token)) return;
            try
            {
                if (_disposed) return;
                var result = Host.Context.GetData(_fences[index], IntPtr.Zero, 0, AsyncGetDataFlags.DoNotFlush);
                result.CheckError();
                if (result.Code == 0) { _fencePending[index] = false; return; }
            }
            finally { if (!holdingGate) Host.Gate.Release(); }
            if (watch.Elapsed.TotalSeconds > FenceTimeoutSeconds) throw new TimeoutException(TimeoutMessage);
            // Short waits avoid a kernel transition. Longer ones use a local high-resolution
            // timer: neither Sleep(1)'s 15.6 ms rounding nor a busy loop on a whole CPU core.
            if (watch.ElapsedMilliseconds < 1) Thread.SpinWait(64);
            else waiter.Pause();
        }
    }

    private bool TryEnter(CancellationToken token)
    {
        try { Host.Gate.Wait(token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    /// <summary>
    /// Освобождение из окна. Ссылка на устройство отпускается после очереди: если окно уже
    /// освободило рендер, эта ссылка последняя и уничтожает устройство вместе с его очередью.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        Host.Gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            DisposeResources();
        }
        finally { Host.Gate.Release(); }
        Host.Release();
    }

    /// <summary>
    /// Освобождение под уже взятой очередью устройства (из рендера). Отпустить ссылку здесь можно:
    /// рендер держит свою до конца своего освобождения, поэтому устройство и очередь остаются живы.
    /// </summary>
    internal void DisposeWhileLocked()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeResources();
        Host.Release();
    }

    private void DisposeResources()
    {
        DisposeResourcesCore();
        _staging?.Dispose(); _staging = null;
        foreach (var shader in _shaders.Values) shader.Dispose();
        _shaders.Clear();
        foreach (var fence in _fences) fence?.Dispose();
        _constants?.Dispose();
    }
}
