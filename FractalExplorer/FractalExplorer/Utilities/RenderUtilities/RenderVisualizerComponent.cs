using System.Collections.Concurrent;

namespace FractalExplorer.Utilities.RenderUtilities
{
    /// <summary>
    /// Компонент для визуализации процесса рендеринга фракталов плитка за плиткой.
    /// Отображает активные (обрабатываемые) и завершенные плитки,
    /// помогая пользователю видеть прогресс рендеринга.
    /// </summary>
    public class RenderVisualizerComponent : IDisposable
    {
    #region Fields

    /// <summary>
    /// Размер одной плитки (тайла) в пикселях. Используется для быстрых вычислений координат.
    /// </summary>
    private readonly int _tileSize;

    /// <summary>
    /// Потокобезопасный словарь для хранения границ плиток, которые в данный момент рендерятся.
    /// </summary>
    private readonly ConcurrentDictionary<Rectangle, byte> _activeTiles = new ConcurrentDictionary<Rectangle, byte>();

    /// <summary>
    /// Объект для блокировки доступа к списку завершенных плиток.
    /// </summary>
    private readonly object _completedTilesListLock = new object();

    /// <summary>
    /// Список границ плиток, рендеринг которых завершен.
    /// </summary>
    private readonly List<Rectangle> _completedTilesList = new List<Rectangle>();

    /// <summary>
    /// Флаг, указывающий, активна ли текущая сессия рендеринга.
    /// </summary>
    private volatile bool _isRenderSessionActive = false;

    /// <summary>
    /// Ручка для рисования границ активных плиток.
    /// </summary>
    private readonly Pen _tileBorderPen;

    /// <summary>
    /// Ручка для рисования внешней границы завершенных плиток.
    /// </summary>
    private readonly Pen _sessionBorderPen;

    /// <summary>
    /// Таймер для периодического запроса перерисовки UI.
    /// </summary>
    private readonly System.Threading.Timer _redrawTimer;

    /// <summary>
    /// Флаг, указывающий, что состояние визуализатора изменилось и требуется перерисовка.
    /// </summary>
    private volatile bool _isDirty = false;

    #endregion

    #region Events

    /// <summary>
    /// Событие, которое возникает, когда компоненту требуется перерисовка UI.
    /// Главная форма должна подписаться на это событие, чтобы вызывать Invalidate().
    /// </summary>
    public event Action NeedsRedraw;

    #endregion

    #region Constructor

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="RenderVisualizerComponent"/>.
    /// </summary>
    /// <param name="tileSize">Размер одной плитки в пикселях. Должен быть больше 0.</param>
    public RenderVisualizerComponent(int tileSize)
    {
        _tileSize = tileSize > 0 ? tileSize : 32; // Проверка на корректность размера плитки

        _tileBorderPen = new Pen(Color.FromArgb(180, 77, 255, 77), 1.5f); // Зеленый для активных плиток
        _sessionBorderPen = new Pen(Color.FromArgb(180, 255, 0, 0), 2f);   // Красный для границы сессии

        // Таймер изначально не активен, запустится при старте сессии рендеринга.
        _redrawTimer = new System.Threading.Timer(OnRedrawTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Уведомляет визуализатор о начале новой сессии рендеринга.
    /// Очищает списки плиток и запускает таймер перерисовки.
    /// </summary>
    public void NotifyRenderSessionStart()
    {
        _isRenderSessionActive = true;
        _activeTiles.Clear();
        lock (_completedTilesListLock)
        {
            _completedTilesList.Clear();
        }
        _isDirty = true; // Отмечаем, что состояние изменилось для немедленной перерисовки
        _redrawTimer.Change(0, 33); // Запускаем таймер с интервалом ~30 FPS
    }

    /// <summary>
    /// Уведомляет визуализатор о завершении текущей сессии рендеринга.
    /// Останавливает таймер и запрашивает финальную перерисовку для очистки визуализации.
    /// </summary>
    public void NotifyRenderSessionComplete()
    {
        _isRenderSessionActive = false;
        _redrawTimer.Change(Timeout.Infinite, Timeout.Infinite); // Останавливаем таймер
        _isDirty = true; // Отмечаем, что состояние изменилось
        OnRedrawTimerTick(null); // Запрашиваем финальную перерисовку, чтобы очистить холст
    }

    /// <summary>
    /// Уведомляет визуализатор о начале рендеринга конкретной плитки.
    /// </summary>
    /// <param name="tileBounds">Границы плитки, рендеринг которой начался.</param>
    public void NotifyTileRenderStart(Rectangle tileBounds)
    {
        if (_isRenderSessionActive)
        {
            _activeTiles.TryAdd(tileBounds, 0); // Добавляем плитку в список активных
            _isDirty = true; // Отмечаем, что состояние изменилось
        }
    }

    /// <summary>
    /// Уведомляет визуализатор о завершении рендеринга конкретной плитки.
    /// </summary>
    /// <param name="tileBounds">Границы плитки, рендеринг которой завершился.</param>
    public void NotifyTileRenderComplete(Rectangle tileBounds)
    {
        _activeTiles.TryRemove(tileBounds, out _); // Удаляем плитку из списка активных
        if (_isRenderSessionActive)
        {
            lock (_completedTilesListLock)
            {
                _completedTilesList.Add(tileBounds); // Добавляем в список завершенных
            }
            _isDirty = true; // Отмечаем, что состояние изменилось
        }
    }

    /// <summary>
    /// Оптимизированный метод отрисовки визуализации. Вызывается из события Paint формы.
    /// Рисует внешнюю границу завершенных плиток и зеленые рамки активных плиток.
    /// </summary>
    /// <param name="graphics">Объект <see cref="Graphics"/> для рисования.</param>
    public void DrawVisualization(Graphics graphics)
    {
        // Если сессия неактивна и нет активных плиток, рисовать нечего.
        if (!_isRenderSessionActive && _activeTiles.IsEmpty)
        {
            return;
        }

        List<Rectangle> currentCompletedTilesSnapshot;
        lock (_completedTilesListLock)
        {
            currentCompletedTilesSnapshot = new List<Rectangle>(_completedTilesList);
        }

        // 1. Отрисовка красной рамки завершённых плиток.
        // Этот алгоритм рисует границу только там, где нет соседней завершенной плитки.
        if (_sessionBorderPen != null && currentCompletedTilesSnapshot.Any())
        {
            // Создаем Set для быстрого (O(1)) поиска координат плиток
            var tileMap = new HashSet<Point>();
            foreach (var rect in currentCompletedTilesSnapshot)
            {
                tileMap.Add(new Point(rect.X / _tileSize, rect.Y / _tileSize));
            }

            foreach (var tile in currentCompletedTilesSnapshot)
            {
                var tileCoord = new Point(tile.X / _tileSize, tile.Y / _tileSize);

                // Рисуем верхнюю грань, если сверху нет соседа
                if (!tileMap.Contains(new Point(tileCoord.X, tileCoord.Y - 1)))
                {
                    graphics.DrawLine(_sessionBorderPen, tile.Left, tile.Top, tile.Right, tile.Top);
                }

                // Рисуем нижнюю грань, если снизу нет соседа
                if (!tileMap.Contains(new Point(tileCoord.X, tileCoord.Y + 1)))
                {
                    graphics.DrawLine(_sessionBorderPen, tile.Left, tile.Bottom, tile.Right, tile.Bottom);
                }

                // Рисуем левую грань, если слева нет соседа
                if (!tileMap.Contains(new Point(tileCoord.X - 1, tileCoord.Y)))
                {
                    graphics.DrawLine(_sessionBorderPen, tile.Left, tile.Top, tile.Left, tile.Bottom);
                }

                // Рисуем правую грань, если справа нет соседа
                if (!tileMap.Contains(new Point(tileCoord.X + 1, tileCoord.Y)))
                {
                    graphics.DrawLine(_sessionBorderPen, tile.Right, tile.Top, tile.Right, tile.Bottom);
                }
            }
        }

        // 2. Отрисовка зелёных рамок для активных (обрабатываемых) плиток.
        // Эти рамки всегда рисуются, чтобы показать, какая плитка в данный момент рендерится.
        if (!_activeTiles.IsEmpty && _tileBorderPen != null)
        {
            foreach (var tileRect in _activeTiles.Keys)
            {
                graphics.DrawRectangle(_tileBorderPen, tileRect.X, tileRect.Y, tileRect.Width - 1, tileRect.Height - 1);
            }
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Метод, вызываемый таймером. Проверяет, было ли состояние изменено, и запрашивает перерисовку UI.
    /// </summary>
    /// <param name="state">Состояние объекта (не используется).</param>
    private void OnRedrawTimerTick(object state)
    {
        if (_isDirty)
        {
            _isDirty = false; // Сбрасываем флаг, так как перерисовка будет запрошена.
            NeedsRedraw?.Invoke(); // Вызываем событие, чтобы форма запросила Invalidate().
        }
    }

    #endregion

    #region IDisposable Implementation

    /// <summary>
    /// Освобождает ресурсы, используемые компонентом.
    /// </summary>
    public void Dispose()
    {
        _redrawTimer?.Dispose();
        _tileBorderPen?.Dispose();
        _sessionBorderPen?.Dispose();
        // Подавляем финализацию, так как ресурсы уже освобождены.
        GC.SuppressFinalize(this);
    }

    #endregion
    }
}
