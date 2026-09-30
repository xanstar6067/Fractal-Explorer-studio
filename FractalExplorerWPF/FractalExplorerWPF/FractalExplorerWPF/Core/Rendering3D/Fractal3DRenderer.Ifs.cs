using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11PixelShader? _ifsPixelShader;
    private ID3D11PixelShader? _flamePixelShader;
    private bool _colorVolume;
    private ID3D11SamplerState? _ifsSampler;
    private ID3D11Texture3D? _ifsVolumeTexture;
    private ID3D11ShaderResourceView? _ifsVolumeView;
    private Fractal3DState? _ifsVolumeState;

    private ID3D11PixelShader GetIfsPixelShader(Fractal3DKind kind)
    {
        if (kind == Fractal3DKind.Flame3D)
            return _flamePixelShader ??= _device!.CreatePixelShader(Compile(FlamePixelShaderEntry()).Span);
        if (_ifsPixelShader is not null) return _ifsPixelShader;
        _ifsPixelShader = _device!.CreatePixelShader(Compile(IfsPixelShaderEntry()).Span);
        return _ifsPixelShader;
    }

    private ID3D11SamplerState GetIfsSampler() =>
        _ifsSampler ??= _device!.CreateSamplerState(
            new SamplerDescription(Filter.MinMagMipLinear, TextureAddressMode.Clamp));

    private void EnsureIfsVolume(Fractal3DState state, CancellationToken token)
    {
        if (_ifsVolumeState is not null && SameVolumeGeometry(_ifsVolumeState, state)) return;
        bool colorVolume = state.Kind == Fractal3DKind.Flame3D;
        int side = colorVolume ? Flame3DVolume.Side : Ifs3DVolume.Side;
        int bytesPerCell = colorVolume ? 8 : 1;
        byte[] voxels = colorVolume ? Flame3DVolume.Build(state, token) : state.Kind == Fractal3DKind.StrangeAttractor
            ? Attractor3DVolume.Build(state, token)
            : Ifs3DVolume.Build(state, token);
        token.ThrowIfCancellationRequested();
        // Once upload starts, an allocation/map failure must not leave a valid-looking
        // snapshot pointing at a replaced or partially updated texture.
        _ifsVolumeState = null;

        if (_ifsVolumeTexture is not null && _colorVolume != colorVolume)
        {
            _context!.PSSetShaderResource(0, null!);
            _ifsVolumeView?.Dispose(); _ifsVolumeTexture.Dispose();
            _ifsVolumeView = null; _ifsVolumeTexture = null;
        }
        if (_ifsVolumeTexture is null)
        {
            _ifsVolumeTexture = _device!.CreateTexture3D(new Texture3DDescription
            {
                Width = (uint)side,
                Height = (uint)side,
                Depth = (uint)side,
                MipLevels = 1,
                Format = colorVolume ? Format.R16G16B16A16_Float : Format.R8_UNorm,
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.Write
            });
            _ifsVolumeView = _device.CreateShaderResourceView(_ifsVolumeTexture);
        }

        ID3D11DeviceContext context = _context!;
        MappedSubresource mapped = context.Map(_ifsVolumeTexture, 0, MapMode.WriteDiscard);
        try
        {
            int rowBytes = side * bytesPerCell;
            if (mapped.RowPitch == rowBytes && mapped.DepthPitch == rowBytes * side)
            {
                Marshal.Copy(voxels, 0, mapped.DataPointer, voxels.Length);
            }
            else
            {
                for (int z = 0; z < side; z++)
                for (int y = 0; y < side; y++)
                {
                    nint destination = IntPtr.Add(mapped.DataPointer, checked((int)(z * mapped.DepthPitch + y * mapped.RowPitch)));
                    Marshal.Copy(voxels, (z * side + y) * rowBytes, destination, rowBytes);
                }
            }
        }
        finally { context.Unmap(_ifsVolumeTexture, 0); }
        _colorVolume = colorVolume;
        _ifsVolumeState = state.Clone();
    }

    internal static bool SameVolumeGeometry(Fractal3DState first, Fractal3DState second)
    {
        if (first.Kind != second.Kind) return false;
        if (first.Kind == Fractal3DKind.Flame3D)
        {
            var a = first.Flame; var b = second.Flame;
            return first.Iterations == second.Iterations && a.Seed == b.Seed && a.Warmup == b.Warmup &&
                a.Transforms.Count == b.Transforms.Count && a.Transforms.Zip(b.Transforms).All(pair =>
                    pair.First.Weight == pair.Second.Weight && pair.First.Variation == pair.Second.Variation &&
                    pair.First.Amount == pair.Second.Amount && pair.First.ColorSpeed == pair.Second.ColorSpeed &&
                    pair.First.Color == pair.Second.Color &&
                    Flame3DSettings.Values(pair.First.Map).SequenceEqual(Flame3DSettings.Values(pair.Second.Map)));
        }
        if (first.Kind == Fractal3DKind.StrangeAttractor)
        {
            Attractor3DSettings a = first.Attractor, b = second.Attractor;
            return first.Iterations == second.Iterations && a.System == b.System &&
                a.A == b.A && a.B == b.B && a.C == b.C && a.D == b.D && a.E == b.E && a.F == b.F &&
                a.TimeStep == b.TimeStep && a.StartX == b.StartX && a.StartY == b.StartY && a.StartZ == b.StartZ;
        }
        if (first.Iterations != second.Iterations || first.IfsTransforms.Count != second.IfsTransforms.Count)
            return false;
        for (int i = 0; i < first.IfsTransforms.Count; i++)
        {
            Ifs3DTransform a = first.IfsTransforms[i], b = second.IfsTransforms[i];
            if (a.M11 != b.M11 || a.M12 != b.M12 || a.M13 != b.M13 ||
                a.M21 != b.M21 || a.M22 != b.M22 || a.M23 != b.M23 ||
                a.M31 != b.M31 || a.M32 != b.M32 || a.M33 != b.M33 ||
                a.Tx != b.Tx || a.Ty != b.Ty || a.Tz != b.Tz || a.Probability != b.Probability)
                return false;
        }
        return true;
    }

    private void DisposeIfsResources()
    {
        _ifsVolumeView?.Dispose();
        _ifsVolumeTexture?.Dispose();
        _ifsPixelShader?.Dispose();
        _flamePixelShader?.Dispose();
        _ifsSampler?.Dispose();
        _ifsVolumeState = null;
    }
}
