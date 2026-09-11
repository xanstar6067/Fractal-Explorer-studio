using System.Text.Json;
using FractalExplorerWPF.Infrastructure.Serialization;

namespace FractalExplorerWPF.Infrastructure;

public static class JsonOptionsFactory
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new WpfColorJsonConverter());
        options.Converters.Add(new NumericsComplexJsonConverter());
        options.Converters.Add(new FloatExpJsonConverter());
        return options;
    }
}
