using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class MandelbrotSaveStore(MandelbrotVariant variant) : FractalSaveStore<MandelbrotState>(
    MandelbrotVariantDefinition.For(variant).Identifier,
    state => state.SaveName,
    state =>
    {
        state.Variant = variant;
        return true;
    });
