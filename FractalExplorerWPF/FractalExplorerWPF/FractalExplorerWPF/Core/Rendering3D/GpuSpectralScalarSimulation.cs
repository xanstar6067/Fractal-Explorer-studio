using FractalExplorerWPF.Infrastructure;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace FractalExplorerWPF.Core.Rendering3D;

/// <summary>Shared scalar Fourier pipeline: packed field/nonlinearity, spectral update, projection,
/// publication and raw checkpoints. Models provide their equation and display compute passes.</summary>
public abstract class GpuSpectralScalarSimulation<TSettings, TField, TVolume>
    : GpuVolumeSimulation<TSettings, TField, TVolume>
    where TSettings : class, IGpuSimulationSettings, new()
    where TVolume : class, IGpuVolumeTicket<object>
{
    private readonly GpuFieldSlot?[] _slots = new GpuFieldSlot?[SlotCount];
    private readonly bool? _tiledFft;
    private readonly Format _displayFormat;
    private GpuStructuredBuffer? _field;
    private GpuComplexBuffer _work = null!, _scratch = null!;
    private GpuFft3D _fft = null!;
    private int _fieldSize;

    private protected GpuSpectralScalarSimulation(Direct3DDeviceHost host, IReadOnlyList<ShaderCacheEntry> shaders,
        string sourceName, string name, int parameters, Format displayFormat, bool? tiledFft = null)
        : base(host, shaders, sourceName, name, parameters)
    {
        _displayFormat = displayFormat; _tiledFft = tiledFft;
    }

    private protected override int StepsPerSubmit => 4;
    private protected sealed override void CreateResourcesLocked() => _fft = new(Host, _tiledFft);
    private protected GpuStructuredBuffer FieldBuffer => _field!;

    /// <summary>Called after settings and equation constants are assigned. Disk fields can bypass projection.</summary>
    private protected void UploadScalarFieldLocked(float[] values, long step, bool project)
    {
        if (_field is null || _fieldSize != Size)
        {
            DisposeFields(); _fieldSize = Size;
            _field = new(Host.Device, Size * Size * Size, 4);
            _work = new(Host.Device, Size); _scratch = new(Host.Device, Size);
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new(Host.Device, Size, 4, _displayFormat);
        }
        Host.Context.UpdateSubresource(values, _field.Buffer); CurrentStep = step;
        if (project) ProjectLocked();
    }

    private protected sealed override void StepLocked()
    {
        ForwardScalarSpectrumLocked("Prepare", FieldBuffer.View);
        SpectralPassLocked("Evolve"); InverseScalarSpectrumLocked();
        DispatchScalarLocked("Finish", output: FieldBuffer.WriteView, spectrum: _work.View);
    }

    private protected void ProjectLocked()
    {
        ForwardScalarSpectrumLocked("Prepare", FieldBuffer.View);
        SpectralPassLocked("Filter"); InverseScalarSpectrumLocked();
        DispatchScalarLocked("Finish", output: FieldBuffer.WriteView, spectrum: _work.View);
    }

    private protected void ForwardScalarSpectrumLocked(string entry, ID3D11ShaderResourceView field)
    {
        DispatchScalarLocked(entry, field, work: _work.WriteView);
        _fft.Transform(Size, false, ref _work, ref _scratch);
    }

    private protected void SpectralPassLocked(string entry)
    {
        DispatchScalarLocked(entry, spectrum: _work.View, work: _scratch.WriteView);
        (_work, _scratch) = (_scratch, _work);
    }
    private protected void InverseScalarSpectrumLocked() => _fft.Transform(Size, true, ref _work, ref _scratch);
    private protected virtual void PrepareDisplayLocked(ID3D11ShaderResourceView field) { }

    private protected sealed override void PublishToSlotLocked(int slot)
    {
        GpuFieldSlot target = _slots[slot]!;
        Host.Context.CopyResource(target.Field.Buffer, FieldBuffer.Buffer);
        PrepareDisplayLocked(target.Field.View);
        DispatchScalarLocked("Publish", target.Field.View, display: target.Display.WriteView, spectrum: _work.View);
    }

    private protected sealed override ID3D11ShaderResourceView DisplayView(int slot) => _slots[slot]!.Display.View;
    private protected float[] ReadScalarCurrentLocked() => ReadFloatsLocked(FieldBuffer.Buffer);
    private protected float[] ReadScalarSlotLocked(TVolume volume) => ReadFloatsLocked(_slots[volume.Slot]!.Field.Buffer);

    private protected void DispatchScalarLocked(string entry, ID3D11ShaderResourceView? input = null,
        ID3D11UnorderedAccessView? output = null, ID3D11UnorderedAccessView? display = null,
        ID3D11ShaderResourceView? spectrum = null, ID3D11UnorderedAccessView? work = null)
    {
        var context = BindLocked(entry);
        context.CSSetShaderResource(0, input!); context.CSSetShaderResource(1, spectrum!);
        context.CSSetUnorderedAccessView(0, output!); context.CSSetUnorderedAccessView(1, display!); context.CSSetUnorderedAccessView(2, work!);
        DispatchCube(16, 4, 2);
        context.CSSetShaderResource(0, null!); context.CSSetShaderResource(1, null!);
        context.CSSetUnorderedAccessView(0, null!); context.CSSetUnorderedAccessView(1, null!); context.CSSetUnorderedAccessView(2, null!);
    }

    private protected sealed override void DisposeResourcesCore() { DisposeFields(); _fft?.Dispose(); }
    private void DisposeFields()
    {
        _field?.Dispose(); _field = null;
        _work?.Dispose(); _scratch?.Dispose(); _work = _scratch = null!;
        for (int i = 0; i < _slots.Length; i++) { _slots[i]?.Dispose(); _slots[i] = null; }
    }
}
