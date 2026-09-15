using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class MathematicalLaboratorySaveStore(MathematicalLaboratoryKind kind)
    : FractalSaveStore<MathematicalLaboratoryState>(
        $"MathematicalLaboratory_{kind}", state => state.SaveName, state => state.Kind == kind);
