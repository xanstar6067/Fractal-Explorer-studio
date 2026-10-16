using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure;

public sealed class McMullenSaveStore() : FractalSaveStore<McMullenState>("McMullen", s => s.SaveName,
    s => { s.Validate(); return s.FractalType == "McMullen"; });
