using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>
/// Одно устройство Direct3D 11 на окно: кадровый рендер и вычисления на ГП работают с общими
/// ресурсами, и данные между ними не проходят через оперативную память. Немедленный контекст
/// не потокобезопасен, поэтому каждый вызов к нему идёт под <see cref="Gate"/>. Устройство
/// освобождается, когда отпущена последняя ссылка (<see cref="AddRef"/>/<see cref="Release"/>).
/// </summary>
public sealed class Direct3DDeviceHost
{
    private readonly object _createLock = new();
    private readonly bool _softwareRendering;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private int _references = 1;

    /// <param name="softwareRendering">Use WARP for a GPU-independent diagnostic render.</param>
    public Direct3DDeviceHost(bool softwareRendering = false) => _softwareRendering = softwareRendering;

    /// <summary>Очередь к немедленному контексту: полоса кадра, порция шагов моделирования, снимок.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public string AdapterName { get; private set; } = "";

    public ID3D11Device Device { get { EnsureCreated(); return _device!; } }

    public ID3D11DeviceContext Context { get { EnsureCreated(); return _context!; } }

    public Direct3DDeviceHost AddRef()
    {
        if (Interlocked.Increment(ref _references) <= 1)
            throw new ObjectDisposedException(nameof(Direct3DDeviceHost));
        return this;
    }

    /// <summary>Отпускает ссылку; последняя освобождает устройство.</summary>
    public void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        _context?.ClearState();
        _context?.Dispose();
        _device?.Dispose();
        _context = null;
        _device = null;
        Gate.Dispose();
    }

    public void EnsureCreated()
    {
        if (_device is not null) return;
        lock (_createLock)
        {
            if (_device is not null) return;
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _references) <= 0, this);
            if (!_softwareRendering) TryCreateOnDedicatedAdapter();

            ReadOnlySpan<DriverType> drivers = _softwareRendering
                ? [DriverType.Warp]
                : [DriverType.Hardware, DriverType.Warp];
            foreach (DriverType driver in _device is null ? drivers : [])
            {
                if (!D3D11.D3D11CreateDevice(null, driver, DeviceCreationFlags.BgraSupport,
                        [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).Success)
                    continue;
                _context = context;
                AdapterName = driver == DriverType.Warp ? "Программный растеризатор WARP" : "Видеокарта по умолчанию";
                Volatile.Write(ref _device, device);
                break;
            }
            if (_device is null)
            {
                throw new InvalidOperationException(
                    "Не удалось создать устройство Direct3D 11. Трёхмерные фракталы считаются на видеокарте, " +
                    "поэтому нужен драйвер с поддержкой Direct3D 11 (Feature Level 11_0).");
            }
        }
    }

    /// <summary>
    /// A null hardware adapter selects adapter 0, often the integrated GPU on laptops. Frames are
    /// read back into RAM, so the adapter with the most dedicated memory needs no WPF interop.
    /// </summary>
    private void TryCreateOnDedicatedAdapter()
    {
        using IDXGIFactory1? factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        var adapters = new List<IDXGIAdapter1>();
        try
        {
            for (uint index = 0; factory is not null && factory.EnumAdapters1(index, out IDXGIAdapter1? adapter).Success; index++)
                if (adapter is not null) adapters.Add(adapter);
            foreach (IDXGIAdapter1 adapter in adapters.OrderByDescending(candidate => candidate.Description1.DedicatedVideoMemory))
            {
                if ((adapter.Description1.Flags & AdapterFlags.Software) != 0) continue;
                if (!D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                        [FeatureLevel.Level_11_0], out ID3D11Device? device, out ID3D11DeviceContext? context).Success)
                    continue;
                _context = context;
                AdapterName = adapter.Description1.Description.Trim();
                Volatile.Write(ref _device, device);
                return;
            }
        }
        finally { foreach (IDXGIAdapter1 adapter in adapters) adapter.Dispose(); }
    }
}
