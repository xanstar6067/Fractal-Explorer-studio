using System.Numerics;
using System.Text.Json.Serialization;

namespace FractalExplorerWPF.Models;

public sealed record Newton3DRoot(double Real, double Imaginary)
{
    [JsonIgnore] public Complex Value => new(Real, Imaginary);
}

/// <summary>Неизменяемые корни в сохранённом порядке: индексы бассейнов и цвета стабильны.</summary>
public sealed class Newton3DRootCatalog : IEquatable<Newton3DRootCatalog>
{
    public IReadOnlyList<Newton3DRoot> Values { get; }

    [JsonConstructor]
    public Newton3DRootCatalog(IReadOnlyList<Newton3DRoot> values) =>
        Values = Array.AsReadOnly((values ?? throw new ArgumentNullException(nameof(values))).ToArray());

    public bool Equals(Newton3DRootCatalog? other) =>
        other is not null && Values.SequenceEqual(other.Values);
    public override bool Equals(object? obj) => obj is Newton3DRootCatalog other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var root in Values) hash.Add(root);
        return hash.ToHashCode();
    }
}
