using System.Diagnostics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11ComputeShader? _lyapunovCompute;
    private ID3D11PixelShader? _lyapunovPixel;
    private ID3D11Buffer? _lyapunovConstants;
    private ID3D11Texture3D? _lyapunovTexture;
    private ID3D11ShaderResourceView? _lyapunovView;
    private Lyapunov3DSettings? _lyapunovSource;
    internal int LyapunovFieldBuilds { get; private set; }
    internal bool NeedsLyapunovField(Lyapunov3DSettings settings) => _lyapunovSource?.SameField(settings) != true;

    /// <summary>Считает поле на общем устройстве; отменённое поле никогда не публикуется.</summary>
    private ID3D11ShaderResourceView LyapunovVolumeView(Fractal3DState state, CancellationToken token,
        IProgress<int>? progress = null)
    {
        var settings = state.Lyapunov;
        settings.Validate();
        token.ThrowIfCancellationRequested();
        if (_lyapunovSource?.SameField(settings) == true) return _lyapunovView!;
        _lyapunovCompute ??= _device!.CreateComputeShader(Compile(Lyapunov3DComputeShader.CacheEntry).Span);
        _lyapunovConstants ??= _device!.CreateBuffer(new BufferDescription {
            ByteWidth = 304, Usage = ResourceUsage.Dynamic, BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write });
        uint n = (uint)settings.Size;
        using var texture = _device!.CreateTexture3D(new Texture3DDescription {
            Width = n, Height = n, Depth = n, MipLevels = 1, Format = Format.R32_Float,
            Usage = ResourceUsage.Default, BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource });
        using var output = _device.CreateUnorderedAccessView(texture);
        using var fence = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        var parameters = new float[76];
        string sequence = settings.Sequence;
        parameters[0] = n; parameters[1] = settings.Transient; parameters[2] = settings.Samples;
        parameters[3] = sequence.Length;
        parameters[4] = (float)settings.MinA; parameters[5] = (float)settings.MinB;
        parameters[6] = (float)settings.MinC; parameters[7] = (float)settings.InitialX;
        parameters[8] = (float)settings.MaxA; parameters[9] = (float)settings.MaxB;
        parameters[10] = (float)settings.MaxC;
        for (int i = 0; i < sequence.Length; i++) parameters[12+i] = sequence[i]-'A';
        var context = _context!;
        context.PSSetShaderResource(0, null!);
        // One z plane per dispatch bounds the longest command even at 128³ / 8192 iterations.
        for (uint z = 0; z < n; z++)
        {
            token.ThrowIfCancellationRequested();
            parameters[11] = z;
            var mapped = context.Map(_lyapunovConstants, MapMode.WriteDiscard);
            try { Marshal.Copy(parameters, 0, mapped.DataPointer, parameters.Length); }
            finally { context.Unmap(_lyapunovConstants, 0); }
            context.CSSetShader(_lyapunovCompute);
            context.CSSetConstantBuffer(0, _lyapunovConstants);
            context.CSSetUnorderedAccessView(0, output);
            context.Dispatch((n+7)/8, (n+7)/8, 1);
            context.CSSetUnorderedAccessView(0, null!);
            context.End(fence); context.Flush();
            var watch = Stopwatch.StartNew();
            while (true)
            {
                // Без Present драйвер может задерживать маленькие порции при запрете flush.
                // Как у Ньютона 3D, разрешаем продвижение offscreen-очереди при ожидании fence.
                var result = context.GetData(fence, IntPtr.Zero, 0, AsyncGetDataFlags.None);
                result.CheckError();
                if (result.Code == 0) break;
                token.ThrowIfCancellationRequested();
                if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("ГП не завершил расчёт поля Ляпунова.");
                Thread.Sleep(1);
            }
            progress?.Report((int)((z+1)*60/n));
        }
        token.ThrowIfCancellationRequested();
        // Keep our own COM reference after the temporary texture wrapper is disposed.
        var view = _device.CreateShaderResourceView(texture);
        _lyapunovView?.Dispose(); _lyapunovTexture?.Dispose();
        _lyapunovView = view;
        using var resource = view.Resource;
        _lyapunovTexture = resource.QueryInterface<ID3D11Texture3D>();
        _lyapunovSource = settings with { };
        LyapunovFieldBuilds++;
        return view;
    }

    /// <summary>Чтение для численной проверки; поле для обычного рендера не читается на ЦП.</summary>
    internal float[] ReadLyapunovField(Fractal3DState state, CancellationToken token)
    {
        Gate.Wait(token);
        try
        {
            EnsureDevice(); LyapunovVolumeView(state, token);
            int n = state.Lyapunov.Size;
            using var staging = _device!.CreateTexture3D(new Texture3DDescription {
                Width = (uint)n, Height = (uint)n, Depth = (uint)n, MipLevels = 1, Format = Format.R32_Float,
                Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read });
            _context!.CopyResource(staging, _lyapunovTexture!);
            var mapped = _context.Map(staging, 0, MapMode.Read);
            var values = new float[n*n*n];
            try
            {
                for (int z=0; z<n; z++) for (int y=0; y<n; y++)
                    Marshal.Copy(mapped.DataPointer + checked((int)(z*mapped.DepthPitch+y*mapped.RowPitch)),
                        values, (z*n+y)*n, n);
            }
            finally { _context.Unmap(staging, 0); }
            return values;
        }
        finally { Gate.Release(); }
    }

    private void DisposeLyapunov()
    {
        _lyapunovView?.Dispose(); _lyapunovTexture?.Dispose(); _lyapunovConstants?.Dispose();
        _lyapunovCompute?.Dispose(); _lyapunovPixel?.Dispose();
        _lyapunovSource = null;
    }
}
