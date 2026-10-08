using System.Runtime.InteropServices;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.D3DCompiler;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Опубликованный кадр моделирования: объём клеток для рендера и точная копия клеток для сохранения.
/// Билет неизменяем; данные слота живут на ГП, пока в него не опубликован следующий кадр.
/// </summary>
public sealed record Lenia3DVolume(Lenia3DGpuSimulation Source, long Version, long Step, int Size, int Slot, double Time);

/// <summary>
/// GPU periodic Lenia: normalized radial convolution via FFT and clipped Euler growth.
/// Published slots keep the shown frame intact.
/// Caller and renderer share Direct3DDeviceHost; only checkpoints read back to the CPU.
/// </summary>
public sealed class Lenia3DGpuSimulation : IDisposable
{
    private const double FenceTimeoutSeconds = 15;

    private readonly Direct3DDeviceHost _host;
    private readonly Dictionary<string, ID3D11ComputeShader> _shaders = [];
    private readonly ID3D11Query[] _fences = new ID3D11Query[2];
    private readonly bool[] _fencePending = new bool[2];
    private readonly Slot?[] _slots = new Slot?[2];
    private readonly long[] _slotVersions = new long[2];
    private readonly float[] _parameters = new float[12];
    private ID3D11Buffer _constants = null!;
    private ID3D11Buffer? _staging;
    private FieldBuffer? _current;
    private GpuFft3D _fft = null!;
    private GpuComplexBuffer _work = null!, _scratch = null!;
    private double _time;
    private GpuComplexBuffer _kernel = null!;
    private int _fenceIndex, _lastSlot = 1;
    private bool _constantsDirty = true;
    private long _step, _version;
    private bool _disposed;

    public Lenia3DGpuSimulation(Direct3DDeviceHost host, Lenia3DSettings settings) : this(host, settings, false) { }

    internal Lenia3DGpuSimulation(Direct3DDeviceHost host, Lenia3DSettings settings, bool gateHeld, bool? tiledFft = null)
    {
        settings.Validate();
        _host = host.AddRef();
        bool entered = false, created = false;
        try
        {
            if (!gateHeld) { _host.Gate.Wait(); entered = true; }
            ID3D11Device device = _host.Device;
            foreach (var entry in Lenia3DComputeShader.CacheEntries)
                _shaders[entry.EntryPoint] = device.CreateComputeShader(ShaderBytecodeCache.GetOrCompile(entry,
                    e => Compiler.Compile(e.Source, e.EntryPoint, "Lenia3D", e.Profile)).Span);
            _constants = device.CreateBuffer(new BufferDescription { ByteWidth = 48, Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer, CPUAccessFlags = CpuAccessFlags.Write });
            for (int i = 0; i < _fences.Length; i++)
                _fences[i] = device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
            _fft = new(host, tiledFft);
            ResetLocked(settings);
            created = true;
        }
        finally
        {
            if (!created) DisposeResources();
            if (entered) _host.Gate.Release();
            // The last reference destroys the device together with its gate: only after the gate is free.
            if (!created) _host.Release();
        }
    }

    /// <summary>Для рендера, который уже держит очередь устройства.</summary>
    internal static Lenia3DGpuSimulation CreateLocked(Direct3DDeviceHost host, Lenia3DSettings settings) =>
        new(host, settings, true);

    internal bool Uses(Direct3DDeviceHost host) => ReferenceEquals(_host, host);

    public Lenia3DSettings Settings { get; private set; } = new();
    public string DeviceName => "ГП · " + _host.AdapterName;
    public int Size => Settings.Size;
    public long Step => Volatile.Read(ref _step);
    public double Time => _time;

    /// <summary>Новое уравнение и поле (сохранённое или стартовая затравка); ресурсы переживают смену при той же сетке.</summary>
    public void Reset(Lenia3DSettings settings)
    {
        settings.Validate();
        _host.Gate.Wait();
        try { ResetLocked(settings); }
        finally { _host.Gate.Release(); }
    }

    internal void ResetLocked(Lenia3DSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        settings.Validate();
        int size = settings.Size;
        if (_current is null || Settings.Size != size)
        {
            DisposeFields();
            ID3D11Device device = _host.Device;
            _current = new(device, size);
            _work = new(device, size); _scratch = new(device, size); _kernel = new(device, size);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(device, size);
        }
        float[] values = settings.Field?.Values ?? Lenia3DMath.InitialField(settings);
        _host.Context.UpdateSubresource(values, _current.Buffer);
        Settings = settings with { Field = null };
        _step = settings.Field?.Step ?? 0;
        Array.Clear(_slotVersions);
        _time = settings.Field?.Time ?? 0;
        _parameters[0] = (float)settings.TimeStep;
        _parameters[1] = (float)settings.GrowthMean; _parameters[2] = (float)settings.GrowthWidth;
        _parameters[3] = (float)settings.Growth; _parameters[4] = size;
        // Kernel origin is at index zero (wrapped negative offsets); no fftshift needed.
        float[] kernel = Lenia3DMath.Kernel(settings), packed = new float[kernel.Length * 4];
        for (int i = 0; i < kernel.Length; i++) packed[i*4] = kernel[i];
        _host.Context.UpdateSubresource(packed, _work.Buffer);
        _fft.Transform(size, false, ref _work, ref _scratch);
        _host.Context.CopyResource(_kernel.Buffer, _work.Buffer);
        _constantsDirty = true;
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
            finally { _host.Gate.Release(); }
            done += count;
            progress?.Report(done);
            WaitForFence(_fenceIndex ^ 1, token, holdingGate: false);
        }
        return done;
    }

    /// <summary>Подготовка под взятой очередью: отмена бросает исключение, поле остаётся целым.</summary>
    public void Synchronize(CancellationToken token) => WaitForFence(_fenceIndex, token, holdingGate: false);

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
    public Lenia3DVolume Publish(Lenia3DVolume? keep = null)
    {
        _host.Gate.Wait();
        try { return PublishLocked(keep); }
        finally { _host.Gate.Release(); }
    }

    internal Lenia3DVolume PublishLocked(Lenia3DVolume? keep = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int index = IsCurrent(keep) ? keep!.Slot ^ 1 : _lastSlot ^ 1;
        Slot slot = _slots[index]!;
        _host.Context.CopyResource(slot.Field.Buffer, _current!.Buffer);
        Dispatch("Publish", slot.Field.View, null, slot.DisplayWrite);
        _version++;
        _slotVersions[index] = _version;
        _lastSlot = index;
        return new(this, _version, _step, Settings.Size, index, _time);
    }

    private bool IsCurrent(Lenia3DVolume? volume) =>
        volume is not null && ReferenceEquals(volume.Source, this) && volume.Size == Settings.Size &&
        _slotVersions[volume.Slot] == volume.Version;

    /// <summary>Текстура клеток опубликованного кадра; вызывается рендером под очередью устройства.</summary>
    internal ID3D11ShaderResourceView ViewLocked(Lenia3DVolume volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        if (!IsCurrent(volume)) throw new InvalidOperationException("Кадр Lenia 3D уже заменён.");
        return _slots[volume.Slot]!.DisplayView;
    }

    /// <summary>Точное поле опубликованного кадра — контрольная точка для сохранения.</summary>
    public Lenia3DField ReadCheckpoint(Lenia3DVolume volume)
    {
        if (!ReferenceEquals(volume.Source, this)) throw new ArgumentException("Кадр другого моделирования.");
        _host.Gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!IsCurrent(volume)) throw new InvalidOperationException("Кадр Lenia 3D уже заменён более новым.");
            return ReadLocked(_slots[volume.Slot]!.Field.Buffer, volume.Step, volume.Time);
        }
        finally { _host.Gate.Release(); }
    }

    /// <summary>Текущее поле, включая ещё не опубликованные шаги.</summary>
    public Lenia3DField ReadCurrent()
    {
        _host.Gate.Wait();
        try { ObjectDisposedException.ThrowIf(_disposed, this); return ReadLocked(_current!.Buffer, _step, _time); }
        finally { _host.Gate.Release(); }
    }

    private Lenia3DField ReadLocked(ID3D11Buffer source, long step, double time)
    {
        int size = Settings.Size;
        int bytes = size * size * size * 4;
        if (_staging is null || _staging.Description.ByteWidth != bytes)
        {
            _staging?.Dispose();
            _staging = _host.Device.CreateBuffer(new BufferDescription
                { ByteWidth = (uint)bytes, Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
        }
        ID3D11DeviceContext context = _host.Context;
        context.CopyResource(_staging, source);
        var mapped = context.Map(_staging, MapMode.Read);
        float[] values = new float[bytes / 4];
        try { Marshal.Copy(mapped.DataPointer, values, 0, values.Length); }
        finally { context.Unmap(_staging, 0); }
        return new(size, step, values, time, true);
    }

    // Keep submission short enough to interleave camera strips and observe cancellation.
    // Two GPU batches overlap; the CPU waits only for the older batch.
    private int StepsPerSubmit => 4;

    private void SubmitStepsLocked(int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = 0; i < count; i++)
        {
            Dispatch("Prepare", _current!.View, null, null, work: _work.WriteView);
            _fft.Transform(Size, false, ref _work, ref _scratch);
            Dispatch("Evolve", null, null, null, _work.View, _scratch.WriteView);
            (_work, _scratch) = (_scratch, _work);
            _fft.Transform(Size, true, ref _work, ref _scratch);
            Dispatch("Finish", null, _current.WriteView, null, _work.View);
            _step++; _time += Settings.TimeStep;
        }
        _fenceIndex ^= 1;
        _host.Context.End(_fences[_fenceIndex]);
        _fencePending[_fenceIndex] = true;
        _host.Context.Flush();
    }

    private void WaitForFence(int index, CancellationToken token, bool holdingGate)
    {
        if (!_fencePending[index]) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var waiter = new HighResolutionWaiter();
        while (!token.IsCancellationRequested)
        {
            if (!holdingGate && !TryEnter(token)) return;
            try
            {
                if (_disposed) return;
                var result = _host.Context.GetData(_fences[index], IntPtr.Zero, 0, AsyncGetDataFlags.DoNotFlush);
                result.CheckError();
                if (result.Code == 0) { _fencePending[index] = false; return; }
            }
            finally { if (!holdingGate) _host.Gate.Release(); }
            if (watch.Elapsed.TotalSeconds > FenceTimeoutSeconds)
                throw new TimeoutException("ГП не завершил шаги Lenia 3D.");
            // Short waits avoid a kernel transition. Longer ones use a local high-resolution
            // timer: neither Sleep(1)'s 15.6 ms rounding nor a busy loop on a whole CPU core.
            if (watch.ElapsedMilliseconds < 1) Thread.SpinWait(64);
            else waiter.Pause();
        }
    }

    private bool TryEnter(CancellationToken token)
    {
        try { _host.Gate.Wait(token); return true; }
        catch (OperationCanceledException) { return false; }
    }

    private void Dispatch(string entry, ID3D11ShaderResourceView? input,
        ID3D11UnorderedAccessView? output, ID3D11UnorderedAccessView? display,
        ID3D11ShaderResourceView? spectrum = null, ID3D11UnorderedAccessView? work = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ID3D11DeviceContext context = _host.Context;
        if (_constantsDirty)
        {
            var mapped = context.Map(_constants, MapMode.WriteDiscard);
            try { Marshal.Copy(_parameters, 0, mapped.DataPointer, _parameters.Length); }
            finally { context.Unmap(_constants, 0); }
            _constantsDirty = false;
        }
        context.CSSetShader(_shaders[entry]); context.CSSetConstantBuffer(0, _constants);
        context.CSSetShaderResource(0, input!);
        context.CSSetShaderResource(1, spectrum!);
        context.CSSetShaderResource(2, _kernel.View);
        context.CSSetUnorderedAccessView(2, work!);
        context.CSSetUnorderedAccessView(0, output!);
        context.CSSetUnorderedAccessView(1, display!);
        uint n = (uint)Settings.Size;
        context.Dispatch((n + Lenia3DComputeShader.GroupX - 1) / Lenia3DComputeShader.GroupX,
            (n + Lenia3DComputeShader.GroupY - 1) / Lenia3DComputeShader.GroupY,
            (n + Lenia3DComputeShader.GroupZ - 1) / Lenia3DComputeShader.GroupZ);
        context.CSSetShaderResource(0, null!);
        context.CSSetShaderResource(1, null!);
        context.CSSetShaderResource(2, null!);
        context.CSSetUnorderedAccessView(2, null!);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetUnorderedAccessView(1, null!);
    }

    /// <summary>
    /// Освобождение из окна. Ссылка на устройство отпускается после очереди: если окно уже
    /// освободило рендер, эта ссылка последняя и уничтожает устройство вместе с его очередью.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _host.Gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            DisposeResources();
        }
        finally { _host.Gate.Release(); }
        _host.Release();
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
        _host.Release();
    }

    private void DisposeResources()
    {
        DisposeFields();
        _staging?.Dispose(); _staging = null;
        _fft?.Dispose();
        foreach (var shader in _shaders.Values) shader.Dispose();
        _shaders.Clear();
        foreach (var fence in _fences) fence?.Dispose();
        _constants?.Dispose();
    }

    private void DisposeFields()
    {
        _current?.Dispose(); _current = null;
        _kernel?.Dispose(); _kernel = null!;
        _work?.Dispose(); _scratch?.Dispose(); _work = _scratch = null!;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }

    private sealed class FieldBuffer : IDisposable
    {
        public ID3D11Buffer Buffer { get; }
        public ID3D11ShaderResourceView View { get; } = null!;
        public ID3D11UnorderedAccessView WriteView { get; } = null!;
        public FieldBuffer(ID3D11Device device, int side)
        {
            Buffer = device.CreateBuffer(new BufferDescription { ByteWidth = (uint)(side * side * side * 4),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                MiscFlags = ResourceOptionFlags.BufferStructured, StructureByteStride = 4 });
            try { View = device.CreateShaderResourceView(Buffer); WriteView = device.CreateUnorderedAccessView(Buffer); }
            catch { View?.Dispose(); Buffer.Dispose(); throw; }
        }
        public void Dispose() { WriteView.Dispose(); View.Dispose(); Buffer.Dispose(); }
    }

    /// <summary>Опубликованный кадр: точное поле и объём клеток в формате R32_Float для рендера.</summary>
    private sealed class Slot : IDisposable
    {
        public FieldBuffer Field { get; }
        public ID3D11Texture3D Display { get; } = null!;
        public ID3D11ShaderResourceView DisplayView { get; } = null!;
        public ID3D11UnorderedAccessView DisplayWrite { get; } = null!;
        public Slot(ID3D11Device device, int side)
        {
            Field = new(device, side);
            try
            {
                Display = device.CreateTexture3D(new Texture3DDescription
                {
                    Width = (uint)side, Height = (uint)side, Depth = (uint)side, MipLevels = 1, Format = Format.R32_Float,
                    Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess
                });
                DisplayView = device.CreateShaderResourceView(Display);
                DisplayWrite = device.CreateUnorderedAccessView(Display);
            }
            catch { DisplayWrite?.Dispose(); DisplayView?.Dispose(); Display?.Dispose(); Field.Dispose(); throw; }
        }
        public void Dispose() { DisplayWrite.Dispose(); DisplayView.Dispose(); Display.Dispose(); Field.Dispose(); }
    }
}
