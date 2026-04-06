using FractalExplorer.Engines;
using System.Text.Json.Serialization;

namespace FractalExplorer.Utilities.SaveIO.SaveStateImplementations
{
    public sealed class FlameFractalSaveState : FractalSaveStateBase
    {
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Scale { get; set; }
        public int Samples { get; set; }
        public int IterationsPerSample { get; set; }
        public int WarmupIterations { get; set; }
        public double Exposure { get; set; }
        public double Gamma { get; set; }
        public List<FlameTransform> Transforms { get; set; } = new();

        // Совместимость со старыми сохранениями (до переименования полей).
        // Эти свойства нужны только для десериализации и не записываются обратно в JSON.
        [JsonPropertyName("Iterations")]
        public int LegacyIterations
        {
            set
            {
                if (value > 0 && IterationsPerSample <= 0)
                {
                    IterationsPerSample = value;
                }
            }
        }

        [JsonPropertyName("Warmup")]
        public int LegacyWarmup
        {
            set
            {
                if (value >= 0 && WarmupIterations <= 0)
                {
                    WarmupIterations = value;
                }
            }
        }

        public FlameFractalSaveState() { }

        public FlameFractalSaveState(string fractalType)
        {
            FractalType = fractalType;
        }
    }
}
