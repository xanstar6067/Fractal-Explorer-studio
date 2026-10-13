using FractalExplorerWPF.Models;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

public sealed record PhaseFieldCrystal3DVolume(PhaseFieldCrystal3DGpuSimulation Source, long Version, long Step, int Size, int Slot)
    : IGpuVolumeTicket<PhaseFieldCrystal3DGpuSimulation>;

/// <summary>Conserved periodic PFC with a sixth-order linear operator, dealiased cubic potential,
/// and an independently smoothed RMS modulation channel used only for presentation.</summary>
public sealed class PhaseFieldCrystal3DGpuSimulation
    : GpuSpectralScalarSimulation<PhaseFieldCrystal3DSettings, PhaseFieldCrystal3DField, PhaseFieldCrystal3DVolume>
{
    private double _mean;
    public PhaseFieldCrystal3DGpuSimulation(Direct3DDeviceHost host, PhaseFieldCrystal3DSettings settings) : this(host, settings, false) { }
    internal PhaseFieldCrystal3DGpuSimulation(Direct3DDeviceHost host, PhaseFieldCrystal3DSettings settings, bool gateHeld, bool? tiledFft = null)
        : base(host, PhaseFieldCrystal3DComputeShader.CacheEntries, "PhaseFieldCrystal3D", "фазового поля кристалла 3D", 12,
            Format.R32G32_Float, tiledFft) => Initialize(settings, gateHeld);
    internal static PhaseFieldCrystal3DGpuSimulation CreateLocked(Direct3DDeviceHost host, PhaseFieldCrystal3DSettings settings)
        => new(host, settings, true);
    private protected override void ResetCoreLocked(PhaseFieldCrystal3DSettings settings)
    {
        Settings = settings with { Field = null, Live = null };
        _mean = settings.Field?.Mean ?? settings.Mean;
        Parameters[0] = (float)settings.Undercooling; Parameters[1] = (float)(settings.WaveNumber * settings.WaveNumber);
        Parameters[2] = (float)(settings.TimeStep * settings.Mobility); Parameters[3] = (float)_mean;
        Parameters[4] = settings.Size; Parameters[5] = (float)(Math.Tau / (settings.Size * settings.Spacing));
        Parameters[7] = (float)(settings.WaveNumber * settings.Size * settings.Spacing / Math.Sqrt(2)); ConstantsDirty = true;
        UploadScalarFieldLocked(settings.Field?.Values ?? InitialField(settings), settings.Field?.Step ?? 0, settings.Field is null);
    }
    private protected override void PrepareDisplayLocked(ID3D11ShaderResourceView field)
    {
        ForwardScalarSpectrumLocked("Measure", field); SpectralPassLocked("OrderFilter"); InverseScalarSpectrumLocked();
    }
    private protected override PhaseFieldCrystal3DVolume CreateVolume(long version, int slot) => new(this, version, CurrentStep, Size, slot);
    private protected override PhaseFieldCrystal3DField ReadSlotLocked(PhaseFieldCrystal3DVolume volume)
        => new(Size, volume.Step, ReadScalarSlotLocked(volume), _mean, true);
    private protected override PhaseFieldCrystal3DField ReadCurrentLocked()
        => new(Size, CurrentStep, ReadScalarCurrentLocked(), _mean, true);

    public void Inject(double x, double y, double z, double radius, PhaseFieldCrystal3DBrush mode)
    {
        if (!double.IsFinite(x + y + z + radius) || x is < 0 or > 1 || y is < 0 or > 1 ||
            z is < 0 or > 1 || radius is <= 0 or > .3 || !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(radius));
        Host.Gate.Wait();
        try
        {
            Parameters[6] = (float)mode; Parameters[8] = (float)x; Parameters[9] = (float)y;
            Parameters[10] = (float)z; Parameters[11] = (float)radius; ConstantsDirty = true;
            DispatchScalarLocked("Inject", output: FieldBuffer.WriteView); ProjectLocked();
        }
        finally { Host.Gate.Release(); }
    }

    internal static float[] InitialField(PhaseFieldCrystal3DSettings s)
    {
        int n = s.Size, limit = n / 4 - 1;
        int m = Math.Clamp((int)Math.Round(s.WaveNumber * n * s.Spacing / (Math.Tau * Math.Sqrt(2))), 1, limit);
        bool grains = s.SeedShape is PhaseFieldCrystal3DSeed.TwoGrains or PhaseFieldCrystal3DSeed.GrainBoundary;
        if (grains) m = Math.Min(m, Math.Max(1, (int)((limit - 1) / 1.4)));
        int a = Math.Max(1, (int)Math.Round(.8 * m)), b = Math.Max(1, (int)Math.Round(.6 * m));
        var random = new Random(s.Seed); float[] field = new float[n * n * n]; double sum = 0;
        double Bcc(double x, double y, double z, bool rotated)
        {
            double u = Math.Cos(rotated ? a*x+b*y : m*x), v = Math.Cos(rotated ? -b*x+a*y : m*y), w = Math.Cos(m*z);
            return u*v+v*w+w*u;
        }
        for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            double px = Math.Tau*x/n, py = Math.Tau*y/n, pz = Math.Tau*z/n;
            double nx = (x+.5)/n-.5, ny = (y+.5)/n-.5, nz = (z+.5)/n-.5;
            double envelope = Math.Exp(-(nx*nx+ny*ny+nz*nz)/.035), modulation;
            switch (s.SeedShape)
            {
                case PhaseFieldCrystal3DSeed.Bcc: modulation = .22*Bcc(px,py,pz,false); break;
                case PhaseFieldCrystal3DSeed.Nucleus: modulation = .4*envelope*Bcc(px,py,pz,false); break;
                case PhaseFieldCrystal3DSeed.TwoGrains:
                    double left = Math.Exp(-((nx+.2)*(nx+.2)+ny*ny+nz*nz)/.018);
                    double right = Math.Exp(-((nx-.2)*(nx-.2)+ny*ny+nz*nz)/.018);
                    modulation = .4*(left*Bcc(px,py,pz,false)+right*Bcc(px,py,pz,true)); break;
                case PhaseFieldCrystal3DSeed.GrainBoundary:
                    double weight = .5+.5*Math.Tanh(Math.Cos(px)/.15);
                    modulation = .22*((1-weight)*Bcc(px,py,pz,false)+weight*Bcc(px,py,pz,true)); break;
                case PhaseFieldCrystal3DSeed.Layers:
                    int frequency = Math.Clamp((int)Math.Round(s.WaveNumber*n*s.Spacing/Math.Tau),1,limit);
                    modulation = .3*Math.Cos(frequency*pz); break;
                case PhaseFieldCrystal3DSeed.Columns:
                    int h = Math.Clamp((int)Math.Round(s.WaveNumber*n*s.Spacing/(2*Math.Tau)),1,limit/2);
                    int k = Math.Clamp((int)Math.Round(Math.Sqrt(3)*h),1,limit);
                    modulation = .18*(Math.Cos(2*h*px)+Math.Cos(-h*px+k*py)+Math.Cos(-h*px-k*py)); break;
                default: modulation = 0; break;
            }
            int i = (z*n+y)*n+x; field[i] = (float)(s.Mean+modulation+s.Noise*(2*random.NextDouble()-1)); sum += field[i];
        }
        double correction = (float)s.Mean-sum/field.Length;
        for (int i=0;i<field.Length;i++) field[i] = (float)(field[i]+correction);
        return field;
    }
}
