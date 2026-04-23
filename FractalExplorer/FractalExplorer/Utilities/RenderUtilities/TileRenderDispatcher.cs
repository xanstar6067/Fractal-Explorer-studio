using System.Collections.Concurrent;

namespace FractalExplorer.Utilities.RenderUtilities
{
    /// <summary>
    /// Стратегия выбора порядка плиток перед распределением по рабочим потокам.
    /// </summary>
    public enum TileSchedulingStrategy
    {
        Classic,      // От центра к краям (наш новый вариант)
        Linear,       // Строго сверху-вниз
        Spiral,       // Прямоугольная спираль
        Randomized,   // Случайно
        Checkerboard, // Шахматный порядок
        Diagonal,     // Диагональная волна
        EdgesInward,  // От краев к центру
        MortonCurve   // Z-Кривая Мортона
    }

    /// <summary>
    /// Глобальная конфигурация выбранного шаблона рендера плиток.
    /// </summary>
    public static class RenderPatternSettings
    {
        /// <summary>
        /// Текущий шаблон рендера, выбранный пользователем в launcher-форме.
        /// </summary>
        public static TileSchedulingStrategy SelectedPattern { get; set; } = TileSchedulingStrategy.Classic;
    }

    internal interface ITileSchedulingTemplate
    {
        IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles);
    }

    internal sealed class ClassicTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            if (tiles.Count <= 1)
            {
                return tiles;
            }

            // 1. Вычисляем крайние координаты для нахождения центра в один проход
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            foreach (var tile in tiles)
            {
                if (tile.Bounds.X < minX) minX = tile.Bounds.X;
                if (tile.Bounds.X > maxX) maxX = tile.Bounds.X;
                if (tile.Bounds.Y < minY) minY = tile.Bounds.Y;
                if (tile.Bounds.Y > maxY) maxY = tile.Bounds.Y;
            }

            // 2. Находим глобальный центр координат
            double centerX = minX + (maxX - minX) / 2.0;
            double centerY = minY + (maxY - minY) / 2.0;

            // 3. Сортируем тайлы по квадрату евклидова расстояния от центра
            // (x - Cx)^2 + (y - Cy)^2
            var sortedTiles = tiles.OrderBy(tile =>
            {
                double dx = tile.Bounds.X - centerX;
                double dy = tile.Bounds.Y - centerY;
                return (dx * dx) + (dy * dy);
            }).ToList();

            return sortedTiles;
        }
    }

    internal sealed class SpiralTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            if (tiles.Count <= 1)
            {
                return tiles;
            }

            var xValues = tiles.Select(t => t.Bounds.X).Distinct().OrderBy(v => v).ToArray();
            var yValues = tiles.Select(t => t.Bounds.Y).Distinct().OrderBy(v => v).ToArray();
            int columns = xValues.Length;
            int rows = yValues.Length;

            var xIndexMap = xValues.Select((value, index) => new { value, index }).ToDictionary(x => x.value, x => x.index);
            var yIndexMap = yValues.Select((value, index) => new { value, index }).ToDictionary(y => y.value, y => y.index);

            var tileLookup = new Dictionary<(int col, int row), TileInfo>();
            foreach (var tile in tiles)
            {
                int col = xIndexMap[tile.Bounds.X];
                int row = yIndexMap[tile.Bounds.Y];
                tileLookup[(col, row)] = tile;
            }

            var ordered = new List<TileInfo>(tiles.Count);
            var visited = new HashSet<(int col, int row)>();
            int maxRadius = Math.Max(columns, rows);
            int centerCol = (columns - 1) / 2;
            int centerRow = (rows - 1) / 2;

            for (int radius = 0; radius <= maxRadius && ordered.Count < tiles.Count; radius++)
            {
                int left = centerCol - radius;
                int right = centerCol + radius;
                int top = centerRow - radius;
                int bottom = centerRow + radius;

                for (int col = left; col <= right; col++) TryAdd(col, top);
                for (int row = top + 1; row <= bottom; row++) TryAdd(right, row);
                for (int col = right - 1; col >= left; col--) TryAdd(col, bottom);
                for (int row = bottom - 1; row > top; row--) TryAdd(left, row);
            }

            return ordered;

            void TryAdd(int col, int row)
            {
                if (col < 0 || col >= columns || row < 0 || row >= rows) return;
                if (!visited.Add((col, row))) return;
                if (tileLookup.TryGetValue((col, row), out var tile))
                {
                    ordered.Add(tile);
                }
            }
        }
    }

    internal sealed class RandomizedTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            if (tiles.Count <= 1)
            {
                return tiles;
            }

            var randomizedTiles = tiles.ToList();
            var random = Random.Shared;
            for (int i = randomizedTiles.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (randomizedTiles[i], randomizedTiles[j]) = (randomizedTiles[j], randomizedTiles[i]);
            }

            return randomizedTiles;
        }
    }

    internal sealed class CheckerboardTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            if (tiles.Count <= 1) return tiles;

            // Находим индексы столбцов и строк для создания сетки (как в Spiral/Morton)
            var xValues = tiles.Select(t => t.Bounds.X).Distinct().OrderBy(v => v).ToArray();
            var yValues = tiles.Select(t => t.Bounds.Y).Distinct().OrderBy(v => v).ToArray();

            var xIndexMap = xValues.Select((value, index) => new { value, index }).ToDictionary(x => x.value, x => x.index);
            var yIndexMap = yValues.Select((value, index) => new { value, index }).ToDictionary(y => y.value, y => y.index);

            // Сортируем: сначала четная сумма индексов (col + row), затем нечетная
            return tiles.OrderBy(tile =>
            {
                int col = xIndexMap[tile.Bounds.X];
                int row = yIndexMap[tile.Bounds.Y];
                return (col + row) % 2; // Сначала 0, потом 1
            })
            .ThenBy(tile => yIndexMap[tile.Bounds.Y])
            .ThenBy(tile => xIndexMap[tile.Bounds.X])
            .ToList();
        }
    }

    internal sealed class DiagonalTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            // Просто сортируем по сумме координат X и Y
            return tiles.OrderBy(tile => tile.Bounds.X + tile.Bounds.Y).ToList();
        }
    }

    internal sealed class EdgesInwardTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            if (tiles.Count <= 1) return tiles;

            double minX = tiles.Min(t => t.Bounds.X);
            double maxX = tiles.Max(t => t.Bounds.X);
            double minY = tiles.Min(t => t.Bounds.Y);
            double maxY = tiles.Max(t => t.Bounds.Y);

            double centerX = minX + (maxX - minX) / 2.0;
            double centerY = minY + (maxY - minY) / 2.0;

            // Отличие только в OrderByDescending
            return tiles.OrderByDescending(tile =>
            {
                double dx = tile.Bounds.X - centerX;
                double dy = tile.Bounds.Y - centerY;
                return (dx * dx) + (dy * dy);
            }).ToList();
        }
    }

    internal sealed class LinearTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            // Сортировка сверху-вниз, при совпадении Y — слева-направо
            return tiles.OrderBy(t => t.Bounds.Y).ThenBy(t => t.Bounds.X).ToList();
        }
    }

    internal sealed class MortonCurveTileSchedulingTemplate : ITileSchedulingTemplate
    {
        public IReadOnlyList<TileInfo> Build(IReadOnlyList<TileInfo> tiles)
        {
            if (tiles.Count <= 1)
            {
                return tiles;
            }

            var xValues = tiles.Select(t => t.Bounds.X).Distinct().OrderBy(v => v).ToArray();
            var yValues = tiles.Select(t => t.Bounds.Y).Distinct().OrderBy(v => v).ToArray();

            var xIndexMap = xValues.Select((value, index) => new { value, index }).ToDictionary(x => x.value, x => x.index);
            var yIndexMap = yValues.Select((value, index) => new { value, index }).ToDictionary(y => y.value, y => y.index);

            return tiles
                .OrderBy(tile => InterleaveBits(
                    (uint)xIndexMap[tile.Bounds.X],
                    (uint)yIndexMap[tile.Bounds.Y]))
                .ToList();
        }

        private static ulong InterleaveBits(uint x, uint y)
        {
            ulong result = 0;
            for (int bit = 0; bit < 32; bit++)
            {
                result |= ((ulong)(x >> bit) & 1UL) << (bit * 2);
                result |= ((ulong)(y >> bit) & 1UL) << (bit * 2 + 1);
            }

            return result;
        }
    }

    /// <summary>
    /// Управляет параллельным рендерингом плиток (тайлов), распределяя их по рабочим потокам.
    /// </summary>
    public class TileRenderDispatcher
    {
        #region Fields

        /// <summary>
        /// Коллекция плиток, которые необходимо отрендерить.
        /// Порядок плиток в этой коллекции определяет порядок их обработки,
        /// что может быть важно для прогрессивного рендеринга.
        /// </summary>
        private readonly IEnumerable<TileInfo> _tilesToRender;

        /// <summary>
        /// Максимальное количество одновременно работающих потоков (задач) для рендеринга.
        /// </summary>
        private readonly int _maxConcurrency;

        /// <summary>
        /// Стратегия, по которой будет подготовлен порядок обработки плиток.
        /// </summary>
        private readonly TileSchedulingStrategy _schedulingStrategy;

        private static readonly IReadOnlyDictionary<TileSchedulingStrategy, ITileSchedulingTemplate> _templates =
            new Dictionary<TileSchedulingStrategy, ITileSchedulingTemplate>
            {
                [TileSchedulingStrategy.Classic] = new ClassicTileSchedulingTemplate(),
                [TileSchedulingStrategy.Linear] = new LinearTileSchedulingTemplate(),
                [TileSchedulingStrategy.Spiral] = new SpiralTileSchedulingTemplate(),
                [TileSchedulingStrategy.Randomized] = new RandomizedTileSchedulingTemplate(),
                [TileSchedulingStrategy.Checkerboard] = new CheckerboardTileSchedulingTemplate(),
                [TileSchedulingStrategy.Diagonal] = new DiagonalTileSchedulingTemplate(),
                [TileSchedulingStrategy.EdgesInward] = new EdgesInwardTileSchedulingTemplate(),
                [TileSchedulingStrategy.MortonCurve] = new MortonCurveTileSchedulingTemplate()
            };

        #endregion

        #region Constructor

        /// <summary>
        /// Инициализирует новый экземпляр класса <see cref="TileRenderDispatcher"/>.
        /// </summary>
        /// <param name="tiles">Коллекция плиток для рендеринга. Порядок, в котором они предоставлены, важен для последовательности обработки.</param>
        /// <param name="maxConcurrency">Максимальное количество одновременно работающих потоков/задач. Должно быть не менее 1.</param>
        /// <param name="schedulingStrategy">Стратегия распределения порядка плиток перед рендерингом.</param>
        /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="tiles"/> равно null.</exception>
        public TileRenderDispatcher(
            IEnumerable<TileInfo> tiles,
            int maxConcurrency,
            TileSchedulingStrategy schedulingStrategy = TileSchedulingStrategy.Classic)
        {
            _tilesToRender = tiles ?? throw new ArgumentNullException(nameof(tiles));
            _maxConcurrency = Math.Max(1, maxConcurrency); // Гарантируем, что минимум один поток будет работать
            _schedulingStrategy = schedulingStrategy;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Асинхронно запускает процесс рендеринга всех плиток, распределяя их по доступным потокам.
        /// </summary>
        /// <param name="renderAction">Действие, которое нужно выполнить для каждой плитки.
        /// Принимает объект <see cref="TileInfo"/> (информация о плитке) и <see cref="CancellationToken"/> (токен отмены)
        /// и возвращает <see cref="Task"/>, представляющий асинхронную операцию рендеринга плитки.</param>
        /// <param name="token">Токен для отмены операции. Если отмена запрошена, рендеринг будет прерван.</param>
        /// <returns>Задача, которая завершится после того, как все плитки будут обработаны или операция будет отменена.</returns>
        public async Task RenderAsync(Func<TileInfo, CancellationToken, Task> renderAction, CancellationToken token)
        {
            var preparedTiles = PrepareTileSequence(_tilesToRender, _schedulingStrategy);

            // Используем потокобезопасную очередь для управления задачами плиток.
            var tileQueue = new ConcurrentQueue<TileInfo>(preparedTiles);
            var tasks = new List<Task>();

            // Запускаем ограниченное количество параллельных задач (рабочих потоков).
            for (int i = 0; i < _maxConcurrency; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    // Каждый рабочий поток извлекает плитки из очереди, пока она не станет пустой.
                    while (tileQueue.TryDequeue(out TileInfo tile))
                    {
                        if (token.IsCancellationRequested)
                        {
                            // Если отмена запрошена, текущая задача завершается.
                            return;
                        }
                        await renderAction(tile, token);
                    }
                }, token)); // Передаем токен отмены в каждую задачу
            }

            // Ожидаем завершения всех запущенных рабочих потоков.
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Формирует порядок обработки плиток в зависимости от выбранной стратегии.
        /// </summary>
        private static IReadOnlyList<TileInfo> PrepareTileSequence(
            IEnumerable<TileInfo> sourceTiles,
            TileSchedulingStrategy strategy)
        {
            var tiles = sourceTiles as List<TileInfo> ?? sourceTiles.ToList();
            if (tiles.Count <= 1)
            {
                return tiles;
            }

            if (_templates.TryGetValue(strategy, out var template))
            {
                return template.Build(tiles);
            }

            return _templates[TileSchedulingStrategy.Classic].Build(tiles);
        }

        #endregion
    }
}
