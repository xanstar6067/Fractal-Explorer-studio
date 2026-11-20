using System.Numerics;
using System.Runtime.InteropServices;
using FractalExplorerWPF.Core.WaveMath;
using FractalExplorerWPF.Infrastructure;
using FractalExplorerWPF.Models;
using FractalExplorerWPF.Models.Fractal3DModes;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct StandingWaveField(Vector4 Grid, Vector4 A, Vector4 B, Vector4 C, Vector4 D);

    private ID3D11ComputeShader? _standingWaveCompute;
    private ID3D11PixelShader? _standingWavePixel;
    private ID3D11Buffer? _standingWaveConstants;
    private ID3D11UnorderedAccessView? _standingWaveOutput;
    private ID3D11ShaderResourceView? _standingWaveView;
    private StandingWaveField _standingWaveField;
    private uint _standingWaveSide;

    private static ShaderCacheEntry StandingWaveComputeEntry() =>
        new("standing-waves-compute", ShaderSource.Load("StandingWavesCompute.hlsl"), "Compute", "cs_5_0");

    private static ShaderCacheEntry StandingWavePixelEntry() =>
        new("standing-waves-pixel", VolumeShader.Build(Fractal3DKind.StandingWaves), "PSMain", "ps_5_0");

    private ID3D11ShaderResourceView StandingWaveVolumeView(Fractal3DState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = StandingWavesMode.Settings(state);
        uint side = (uint)settings.Resolution;
        double amplitude = settings.Modes.Sum(mode => mode.Amplitude);
        Vector4 Mode(int index) => index < settings.Modes.Length
            ? StandingWave.Parameters(settings, settings.Modes[index], amplitude) : Vector4.Zero;

        var field = new StandingWaveField(new Vector4(side, 0, 0, 0), Mode(0), Mode(1), Mode(2), Mode(3));
        var context = _context!;
        if (field == _standingWaveField)
        {
            context.PSSetConstantBuffer(1, _standingWaveConstants!);
            return _standingWaveView!;
        }

        _standingWaveCompute ??= _device!.CreateComputeShader(Compile(StandingWaveComputeEntry()).Span);
        _standingWaveConstants ??= _device!.CreateBuffer(new BufferDescription
        {
            ByteWidth = (uint)Marshal.SizeOf<StandingWaveField>(),
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write
        });
        context.PSSetShaderResource(0, null!);
        EnsureStandingWaveTexture(side);

        var mapped = context.Map(_standingWaveConstants, MapMode.WriteDiscard);
        try { Marshal.StructureToPtr(field, mapped.DataPointer, false); }
        finally { context.Unmap(_standingWaveConstants, 0); }

        context.CSSetShader(_standingWaveCompute);
        context.CSSetConstantBuffer(0, _standingWaveConstants);
        context.CSSetUnorderedAccessView(0, _standingWaveOutput!);
        try { context.Dispatch(side / 8, side / 8, side / 4); }
        finally { context.CSSetUnorderedAccessView(0, null!); }

        _standingWaveField = field;
        context.PSSetConstantBuffer(1, _standingWaveConstants);
        return _standingWaveView!;
    }

    private void EnsureStandingWaveTexture(uint side)
    {
        if (_standingWaveSide == side) return;
        using var texture = _device!.CreateTexture3D(new Texture3DDescription
        {
            Width = side, Height = side, Depth = side, MipLevels = 1,
            Format = Format.R32G32_Float,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource
        });
        var output = _device.CreateUnorderedAccessView(texture);
        ID3D11ShaderResourceView view;
        try { view = _device.CreateShaderResourceView(texture); }
        catch { output.Dispose(); throw; }
        _standingWaveOutput?.Dispose();
        _standingWaveView?.Dispose();
        _standingWaveOutput = output;
        _standingWaveView = view;
        _standingWaveSide = side;
        _standingWaveField = default;
    }

    private void DisposeStandingWaves()
    {
        _standingWaveView?.Dispose();
        _standingWaveOutput?.Dispose();
        _standingWaveCompute?.Dispose();
        _standingWavePixel?.Dispose();
        _standingWaveConstants?.Dispose();
    }
}
