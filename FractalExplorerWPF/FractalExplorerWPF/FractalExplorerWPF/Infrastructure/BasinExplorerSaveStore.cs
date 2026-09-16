using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

/// <summary>Сохранения окна бассейнов — отдельный каталог на каждый режим.</summary>
public sealed class BasinExplorerSaveStore(BasinExplorerKind kind) : FractalSaveStore<BasinExplorerState>(
    BasinExplorerCatalog.GetDefinition(kind).SaveFilePrefix, state => state.SaveName);
