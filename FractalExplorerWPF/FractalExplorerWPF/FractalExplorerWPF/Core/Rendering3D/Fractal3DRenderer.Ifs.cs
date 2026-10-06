using System.Runtime.InteropServices;
using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed partial class Fractal3DRenderer
{
    private ID3D11PixelShader? _ifsPixelShader;
    private ID3D11PixelShader? _flamePixelShader;
    private ID3D11PixelShader? _buddhabrotPixelShader;
    private Buddhabrot4DOrbitCloud? _buddhabrotCloud;
    internal int BuddhabrotSamplingBuilds { get; private set; }
    private ID3D11PixelShader? _dlaPixelShader;
    private ID3D11PixelShader? _grayScottPixelShader;
    private int _volumeSide;
    private GrayScott3DSettings? _graySeedSettings;
    private GrayScott3DField? _graySeedField;
    private Dla3DCluster? _dlaCluster;
    private Format _volumeFormat;
    // Metadata belongs to the uploaded volume, not to an unfinished/canceled growth batch.
    public int DlaParticleCount { get; private set; }
    public bool DlaBoundaryReached { get; private set; }
    public double DlaRadius { get; private set; }
    private ID3D11SamplerState? _ifsSampler;
    private ID3D11Texture3D? _ifsVolumeTexture;
    private ID3D11ShaderResourceView? _ifsVolumeView;
    private Fractal3DState? _ifsVolumeState;

    private ID3D11PixelShader GetIfsPixelShader(Fractal3DKind kind)
    {
        if (kind == Fractal3DKind.GrayScott3D)
            return _grayScottPixelShader ??= _device!.CreatePixelShader(Compile(GrayScottPixelShaderEntry()).Span);
        if (kind == Fractal3DKind.Buddhabrot4D)
            return _buddhabrotPixelShader ??= _device!.CreatePixelShader(Compile(BuddhabrotPixelShaderEntry()).Span);
        if (kind == Fractal3DKind.Dla3D)
            return _dlaPixelShader ??= _device!.CreatePixelShader(Compile(DlaPixelShaderEntry()).Span);
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
        bool colorVolume = state.Kind is Fractal3DKind.Flame3D or Fractal3DKind.Buddhabrot4D;
        bool grayScott = state.Kind == Fractal3DKind.GrayScott3D;
        int side = grayScott ? state.GrayScott.Size : colorVolume ? Flame3DVolume.Side : state.Kind == Fractal3DKind.Dla3D ? Dla3DVolume.Side : Ifs3DVolume.Side;
        int bytesPerCell = grayScott || colorVolume ? 8 : state.Kind == Fractal3DKind.Dla3D ? 2 : 1;
        Format format = grayScott ? Format.R32G32_Float : colorVolume ? Format.R16G16B16A16_Float : state.Kind == Fractal3DKind.Dla3D ? Format.R8G8_UNorm : Format.R8_UNorm;
        byte[] voxels = state.Kind switch
        {
            Fractal3DKind.GrayScott3D => BuildGrayScottVolume(state, token),
            Fractal3DKind.Buddhabrot4D => BuildBuddhabrotVolume(state, token),
            Fractal3DKind.Dla3D => BuildDlaVolume(state, token),
            Fractal3DKind.Flame3D => Flame3DVolume.Build(state, token),
            Fractal3DKind.StrangeAttractor => Attractor3DVolume.Build(state, token),
            _ => Ifs3DVolume.Build(state, token)
        };
        token.ThrowIfCancellationRequested();
        // Once upload starts, an allocation/map failure must not leave a valid-looking
        // snapshot pointing at a replaced or partially updated texture.
        _ifsVolumeState = null;

        if (_ifsVolumeTexture is not null && (_volumeFormat != format || _volumeSide != side))
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
                Format = format,
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
        _volumeFormat = format;
        _volumeSide = side;
        _ifsVolumeState = state.Clone();
        if (state.Kind == Fractal3DKind.Dla3D)
        {
            DlaParticleCount = _dlaCluster!.Count;
            DlaBoundaryReached = _dlaCluster.BoundaryReached;
            DlaRadius = _dlaCluster.Radius / (Dla3DVolume.Side / 2d);
        }
    }

    private byte[] BuildGrayScottVolume(Fractal3DState state, CancellationToken token)
    {
        state.GrayScott.Validate(); token.ThrowIfCancellationRequested();
        var field = state.GrayScott.Field;
        if (field is null)
        {
            var seedSettings = state.GrayScott with { CutAxis = 0, CutPosition = 0, Threshold = .18, StepsPerFrame = 16 };
            if (_graySeedSettings != seedSettings || _graySeedField is null)
            {
                using var engine = GrayScott3DEngineFactory.Create(seedSettings, out _);
                for (int remaining = seedSettings.InitialSteps; remaining > 0; remaining -= Math.Min(remaining, 256))
                    engine.Advance(Math.Min(remaining, 256), token);
                var prepared = engine.Snapshot(); token.ThrowIfCancellationRequested();
                _graySeedSettings = seedSettings; _graySeedField = prepared;
            }
            field = _graySeedField;
        }
        var values = field.Values;
        byte[] bytes = new byte[values.Length * 4]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private byte[] BuildDlaVolume(Fractal3DState state, CancellationToken token)
    {
        Dla3DSettings settings = (state.Dla ?? new()).Normalized();
        if (_dlaCluster is null || !_dlaCluster.Settings.SameGrowth(settings) || _dlaCluster.Count > settings.ParticleCount)
            _dlaCluster = new(settings);
        _dlaCluster.GrowTo(settings.ParticleCount, token);
        return Dla3DVolume.Build(_dlaCluster, token);
    }

    private byte[] BuildBuddhabrotVolume(Fractal3DState state, CancellationToken token)
    {
        var settings = state.Buddhabrot ?? new();
        settings.Validate();
        if (_buddhabrotCloud is null || !_buddhabrotCloud.Settings.SameSampling(settings))
        {
            var cloud = Buddhabrot4DOrbitCloud.Build(settings, token);
            token.ThrowIfCancellationRequested();
            _buddhabrotCloud = cloud;
            BuddhabrotSamplingBuilds++;
        }
        return Buddhabrot4DVolume.Build(_buddhabrotCloud, settings, token);
    }

    internal static bool SameVolumeGeometry(Fractal3DState first, Fractal3DState second)
    {
        if (first.Kind != second.Kind) return false;
        if (first.Kind == Fractal3DKind.GrayScott3D)
            return ReferenceEquals(first.GrayScott.Field, second.GrayScott.Field) &&
                first.GrayScott.Size == second.GrayScott.Size && first.GrayScott.Seed == second.GrayScott.Seed &&
                first.GrayScott.SeedShape == second.GrayScott.SeedShape && (first.GrayScott.Field is not null ||
                    first.GrayScott.Feed == second.GrayScott.Feed && first.GrayScott.Kill == second.GrayScott.Kill &&
                    first.GrayScott.DiffusionU == second.GrayScott.DiffusionU && first.GrayScott.DiffusionV == second.GrayScott.DiffusionV &&
                    first.GrayScott.Backend == second.GrayScott.Backend);
        if (first.Kind == Fractal3DKind.Buddhabrot4D)
            return (first.Buddhabrot ?? new()).SameVolume(second.Buddhabrot ?? new());
        if (first.Kind == Fractal3DKind.Dla3D)
        {
            Dla3DSettings a = (first.Dla ?? new()).Normalized(), b = (second.Dla ?? new()).Normalized();
            return a.SameGrowth(b) && a.ParticleCount == b.ParticleCount;
        }
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
        _buddhabrotPixelShader?.Dispose();
        _buddhabrotCloud = null;
        _dlaPixelShader?.Dispose();
        _grayScottPixelShader?.Dispose();
        _graySeedSettings = null; _graySeedField = null;
        _dlaCluster = null;
        _ifsSampler?.Dispose();
        _ifsVolumeState = null;
    }
}
