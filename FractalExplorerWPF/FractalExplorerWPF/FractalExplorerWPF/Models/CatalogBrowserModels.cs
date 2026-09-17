using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace FractalExplorerWPF.Models;

/// <summary>
/// Конечная группа каталога — полный путь категории пункта. По ней сетка главного окна
/// раскладывает плитки под заголовками; один экземпляр на путь, поэтому годится как ключ группировки.
/// </summary>
public sealed class CatalogGroup
{
    public CatalogGroup(IReadOnlyList<string> categoryPath)
    {
        string[] path = categoryPath.ToArray();
        Path = path;
        Key = string.Join(" › ", path);
        Title = path.Length > 0 ? path[^1] : string.Empty;
        ParentPath = path.Length > 1 ? string.Join(" › ", path[..^1]) : string.Empty;
    }

    public IReadOnlyList<string> Path { get; }
    public string Key { get; }
    public string Title { get; }

    /// <summary>Путь без собственного названия группы: «Фракталы › Комплексная динамика».</summary>
    public string ParentPath { get; }

    public override string ToString() => Key;
}

/// <summary>Плитка каталога: пункт, его превью и пользовательские отметки.</summary>
public sealed class CatalogTile : INotifyPropertyChanged
{
    private ImageSource? _thumbnail;
    private ImageSource? _preview;
    private bool _isPreviewPending;
    private bool _isFavorite;
    private int _recentRank = -1;

    public CatalogTile(FractalCatalogItem item, CatalogGroup group)
    {
        Item = item;
        Group = group;
        SearchText = CatalogSearch.BuildSearchText(item);
    }

    public FractalCatalogItem Item { get; }
    public CatalogGroup Group { get; }
    public string DisplayName => Item.DisplayName;
    public string Description => Item.Description;
    public string Breadcrumb => Item.CategoryBreadcrumb;
    public bool CanLaunch => Item.LaunchKey is not null;

    /// <summary>Нормализованный текст для поиска: название, описание и разделы.</summary>
    internal string SearchText { get; }

    /// <summary>Уменьшенное превью для сетки.</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (!Set(ref _thumbnail, value)) return;
            OnPropertyChanged(nameof(DisplayPreview));
        }
    }

    /// <summary>Превью полного размера; держится только пока пункт открыт в панели деталей.</summary>
    public ImageSource? Preview
    {
        get => _preview;
        set
        {
            if (!Set(ref _preview, value)) return;
            OnPropertyChanged(nameof(DisplayPreview));
        }
    }

    /// <summary>Лучшее из доступных превью — для панели деталей.</summary>
    public ImageSource? DisplayPreview => _preview ?? _thumbnail;

    /// <summary>Превью ещё рендерится (лаборатории и Gray–Scott строят его на лету).</summary>
    public bool IsPreviewPending
    {
        get => _isPreviewPending;
        set => Set(ref _isPreviewPending, value);
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => Set(ref _isFavorite, value);
    }

    /// <summary>Позиция в списке недавних (0 — последний запуск), −1 — не запускался.</summary>
    public int RecentRank
    {
        get => _recentRank;
        set
        {
            if (!Set(ref _recentRank, value)) return;
            OnPropertyChanged(nameof(IsRecent));
        }
    }

    public bool IsRecent => _recentRank >= 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum CatalogScopeKind
{
    All,
    Favorites,
    Recent,
    Category
}

/// <summary>
/// Пункт меню разделов слева. Категория — любой префикс пути каталога: раздел верхнего уровня,
/// промежуточный подраздел или конечная группа; выбор показывает все пункты под этим префиксом.
/// </summary>
public sealed class CatalogScope : INotifyPropertyChanged
{
    private const double IndentStep = 14;

    private int _count;
    private bool _isDimmed;

    private CatalogScope(CatalogScopeKind kind, string title, string glyph, IReadOnlyList<string> path)
    {
        Kind = kind;
        Title = title;
        Glyph = glyph;
        Path = path;
    }

    public CatalogScopeKind Kind { get; }
    public string Title { get; }

    /// <summary>Символ Segoe Fluent Icons / MDL2; пусто у категорий.</summary>
    public string Glyph { get; }

    public IReadOnlyList<string> Path { get; }
    public int Depth => Path.Count - 1;

    /// <summary>Раздел верхнего уровня — подпись-заголовок в меню.</summary>
    public bool IsSection => Kind == CatalogScopeKind.Category && Path.Count == 1;

    public string DisplayTitle => IsSection ? Title.ToUpperInvariant() : Title;
    public Thickness Indent => new(Kind == CatalogScopeKind.Category ? Math.Max(0, Depth - 1) * IndentStep : 0, 0, 0, 0);
    /// <summary>Полный путь вложенной категории; у остальных пунктов подсказки нет.</summary>
    public string? ToolTipText => Kind == CatalogScopeKind.Category && Path.Count > 1 ? string.Join(" › ", Path) : null;

    /// <summary>Сколько пунктов попадает в раздел с учётом текущего поиска.</summary>
    public int Count
    {
        get => _count;
        set
        {
            if (_count == value) return;
            _count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }
    }

    /// <summary>Во время поиска в разделе нет совпадений.</summary>
    public bool IsDimmed
    {
        get => _isDimmed;
        set
        {
            if (_isDimmed == value) return;
            _isDimmed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDimmed)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static CatalogScope All() => new(CatalogScopeKind.All, "Все режимы", "\uE8A9", []);
    public static CatalogScope Favorites() => new(CatalogScopeKind.Favorites, "Избранное", "\uE734", []);
    public static CatalogScope Recent() => new(CatalogScopeKind.Recent, "Недавние", "\uE823", []);
    public static CatalogScope Category(IReadOnlyList<string> path) => new(CatalogScopeKind.Category, path[^1], string.Empty, path.ToArray());

    public bool Includes(CatalogTile tile) => Kind switch
    {
        CatalogScopeKind.All => true,
        CatalogScopeKind.Favorites => tile.IsFavorite,
        CatalogScopeKind.Recent => tile.IsRecent,
        CatalogScopeKind.Category => StartsWith(tile.Item.CategoryPath, Path),
        _ => false
    };

    /// <summary>
    /// Меню в порядке каталога: «Все режимы», «Избранное», «Недавние», затем каждый различный
    /// префикс пути категорий — раздел, подраздел, группа.
    /// </summary>
    public static IReadOnlyList<CatalogScope> Build(IEnumerable<FractalCatalogItem> catalog)
    {
        var scopes = new List<CatalogScope> { All(), Favorites(), Recent() };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (FractalCatalogItem item in catalog)
        {
            for (int length = 1; length <= item.CategoryPath.Count; length++)
            {
                string[] prefix = item.CategoryPath.Take(length).ToArray();
                if (seen.Add(string.Join("\u001F", prefix))) scopes.Add(Category(prefix));
            }
        }
        return scopes;
    }

    private static bool StartsWith(IReadOnlyList<string> path, IReadOnlyList<string> prefix)
    {
        if (path.Count < prefix.Count) return false;
        for (int index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(path[index], prefix[index], StringComparison.Ordinal)) return false;
        }
        return true;
    }
}

/// <summary>
/// Поиск каталога: все слова запроса должны встретиться в названии, описании или разделах.
/// Регистр, «ё/е» и разновидности дефиса не различаются («gray-scott» находит «Gray–Scott»).
/// </summary>
public static class CatalogSearch
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (char source in text)
        {
            char lower = char.ToLowerInvariant(source);
            builder.Append(lower switch
            {
                'ё' => 'е',
                '\u2010' or '\u2011' or '\u2012' or '\u2013' or '\u2014' or '\u2212' => '-',
                '\u00A0' => ' ',
                _ => lower
            });
        }
        return builder.ToString();
    }

    public static IReadOnlyList<string> Tokenize(string? query) =>
        Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static string BuildSearchText(FractalCatalogItem item) =>
        Normalize(string.Join('\n', new[] { item.DisplayName, item.Description }.Concat(item.CategoryPath)));

    public static bool Matches(FractalCatalogItem item, string? query) =>
        Matches(BuildSearchText(item), Tokenize(query));

    internal static bool Matches(string searchText, IReadOnlyList<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (!searchText.Contains(token, StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>«1 режим», «3 режима», «57 режимов».</summary>
    public static string CountModes(int count)
    {
        int lastTwo = Math.Abs(count) % 100;
        int last = lastTwo % 10;
        string word = lastTwo is >= 11 and <= 14 ? "режимов"
            : last == 1 ? "режим"
            : last is >= 2 and <= 4 ? "режима"
            : "режимов";
        return $"{count} {word}";
    }
}
