using System.Diagnostics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Core.QuantumMath;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11ComputeShader? _orbitalCompute;
    private ID3D11PixelShader? _orbitalPixel;
    private ID3D11Buffer? _orbitalConstants;
    private ID3D11ShaderResourceView? _orbitalView;
    private QuantumOrbitalSettings? _orbitalSource;
    internal int OrbitalFieldBuilds { get; private set; }
    internal static ShaderCacheEntry OrbitalComputeEntry() => new("quantum-orbitals-compute",
        ShaderSource.Load("QuantumOrbitalsCompute.hlsl"), "Compute", "cs_5_0");
    internal static ShaderCacheEntry OrbitalPixelEntry() => new("quantum-orbitals-pixel",
        VolumeShader.Build(Fractal3DKind.QuantumOrbitals), "PSMain", "ps_5_0");

    private ID3D11ShaderResourceView OrbitalVolumeView(Fractal3DState state, CancellationToken token, IProgress<int>? progress = null)
    {
        var s = state.QuantumOrbital;
        s.Validate(); token.ThrowIfCancellationRequested();
        if (_orbitalSource?.SameField(s) == true) return _orbitalView!;
        _orbitalCompute ??= _device!.CreateComputeShader(Compile(OrbitalComputeEntry()).Span);
        _orbitalConstants ??= _device!.CreateBuffer(new BufferDescription
        {
            ByteWidth = 64,
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });
        uint side = (uint)s.Size;
        using var texture = _device!.CreateTexture3D(new Texture3DDescription
        {
            Width = side,
            Height = side,
            Depth = side,
            MipLevels = 1,
            Format = Format.R32G32B32A32_Float,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource
        });
        using var output = _device.CreateUnorderedAccessView(texture);
        using var fence = _device.CreateQuery(new QueryDescription(QueryType.Event, QueryFlags.None));
        var (a, b) = HydrogenOrbital.Weights(s);
        float[] parameters = [side,0,(float)s.Extent,(float)HydrogenOrbital.DisplayScale(s),
            s.N,s.L,s.M,(int)s.Basis,s.N2,s.L2,s.M2,(int)s.Basis,(float)a,(float)b.Real,(float)b.Imaginary,s.Superposition ? 1 : 0];
        var context = _context!;
        context.PSSetShaderResource(0, null!);
        try
        {
            const uint batch = 8;
            for (uint plane = 0; plane < side; plane += batch)
            {
                token.ThrowIfCancellationRequested(); parameters[1] = plane;
                var mapped = context.Map(_orbitalConstants, MapMode.WriteDiscard);
                try { Marshal.Copy(parameters, 0, mapped.DataPointer, parameters.Length); }
                finally { context.Unmap(_orbitalConstants, 0); }
                context.CSSetShader(_orbitalCompute); context.CSSetConstantBuffer(0, _orbitalConstants);
                context.CSSetUnorderedAccessView(0, output);
                context.Dispatch((side + 7) / 8, (side + 7) / 8, batch);
                context.CSSetUnorderedAccessView(0, null!);
                context.End(fence); context.Flush();
                var watch = Stopwatch.StartNew();
                while (true)
                {
                    var result = context.GetData(fence, IntPtr.Zero, 0, AsyncGetDataFlags.None); result.CheckError();
                    if (result.Code == 0) break;
                    token.ThrowIfCancellationRequested();
                    if (watch.Elapsed.TotalSeconds > 15) throw new TimeoutException("ГП не завершил расчёт орбитали.");
                    Thread.Sleep(1);
                }
                progress?.Report((int)(System.Math.Min(plane + batch, side) * 60 / side));
            }
        }
        finally { context.CSSetUnorderedAccessView(0, null!); }
        token.ThrowIfCancellationRequested();
        var view = _device.CreateShaderResourceView(texture);
        _orbitalView?.Dispose(); _orbitalView = view; _orbitalSource = s with { }; OrbitalFieldBuilds++;
        return view;
    }

    internal float[] ReadOrbitalField(Fractal3DState state, CancellationToken token)
    {
        Gate.Wait(token);
        try
        {
            EnsureDevice(); var view = OrbitalVolumeView(state, token);
            uint side = (uint)state.QuantumOrbital.Size;
            using var resource = view.Resource;
            using var texture = resource.QueryInterface<ID3D11Texture3D>();
            using var staging = _device!.CreateTexture3D(new Texture3DDescription
            {
                Width = side,
                Height = side,
                Depth = side,
                MipLevels = 1,
                Format = Format.R32G32B32A32_Float,
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Read
            });
            _context!.CopyResource(staging, texture);
            var mapped = _context.Map(staging, 0, MapMode.Read);
            var values = new float[side * side * side * 4];
            try
            {
                for (int z = 0; z < side; z++) for (int y = 0; y < side; y++)
                    Marshal.Copy(mapped.DataPointer + checked((int)(z * mapped.DepthPitch + y * mapped.RowPitch)), values,
                        (int)((z * side + y) * side * 4), (int)side * 4);
            }
            finally { _context.Unmap(staging, 0); }
            return values;
        }
        finally { Gate.Release(); }
    }
    private void DisposeOrbitals()
    {
        _orbitalView?.Dispose(); _orbitalCompute?.Dispose(); _orbitalPixel?.Dispose(); _orbitalConstants?.Dispose();
        _orbitalSource = null;
    }
}
