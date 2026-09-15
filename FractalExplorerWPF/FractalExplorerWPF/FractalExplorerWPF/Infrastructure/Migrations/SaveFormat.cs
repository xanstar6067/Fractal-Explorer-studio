using System.Text.Json;
using System.Text.Json.Nodes;

namespace FractalExplorerWPF.Infrastructure.Migrations;

/// <summary>Переводит JSON одного сохранения из версии N в N + 1. Категория — каталог режима.</summary>
public sealed record SaveFormatUpgrade(string Description, Action<string, JsonObject> Apply);

/// <summary>
/// Версия формата файла сохранения. Каждый файл несёт её в свойстве
/// <see cref="VersionProperty"/>, поэтому сохранение остаётся читаемым, даже если его
/// скопировали из старой резервной копии или с другого компьютера: при чтении недостающие
/// апгрейды применяются в памяти, а на диск файл перезаписывается только при следующем
/// сохранении пользователем.
/// <para>
/// Как изменить формат: добавить <see cref="SaveFormatUpgrade"/> в конец <see cref="BuiltInUpgrades"/>.
/// Уже выпущенные апгрейды не править и не удалять — по ним читаются старые файлы.
/// </para>
/// </summary>
public static class SaveFormat
{
    public const string VersionProperty = "SaveFormatVersion";

    /// <summary>Апгрейд с индексом i переводит формат из версии i + 1 в i + 2.</summary>
    private static readonly IReadOnlyList<SaveFormatUpgrade> BuiltInUpgrades = [];

    /// <summary>Шов для проверок: подменяет список апгрейдов.</summary>
    internal static IReadOnlyList<SaveFormatUpgrade>? UpgradesOverrideForTests { get; set; }

    internal static IReadOnlyList<SaveFormatUpgrade> Upgrades => UpgradesOverrideForTests ?? BuiltInUpgrades;

    /// <summary>Версия, которую пишет эта сборка. Файлы без отметки считаются версией 1.</summary>
    public static int CurrentVersion => Upgrades.Count + 1;

    public static int ReadVersion(JsonObject save) =>
        save[VersionProperty] is JsonValue value && value.TryGetValue(out int version) && version >= 1 ? version : 1;

    /// <summary>Ставит отметку текущей версии первым свойством объекта.</summary>
    public static void Stamp(JsonObject save)
    {
        save.Remove(VersionProperty);
        save.Insert(0, VersionProperty, CurrentVersion);
    }

    /// <summary>
    /// Применяет недостающие апгрейды. Файл более новой версии оставляется как есть и читается
    /// по мере возможности: неизвестные свойства сериализатор пропускает.
    /// </summary>
    public static void UpgradeInPlace(string category, JsonObject save)
    {
        IReadOnlyList<SaveFormatUpgrade> upgrades = Upgrades;
        for (int version = ReadVersion(save); version < upgrades.Count + 1; version++)
        {
            upgrades[version - 1].Apply(category, save);
            save[VersionProperty] = version + 1;
        }
    }

    /// <summary>Сериализует состояние с отметкой версии формата.</summary>
    public static string Serialize<TState>(TState state, JsonSerializerOptions options)
    {
        JsonObject save = JsonSerializer.SerializeToNode(state, options)?.AsObject()
            ?? throw new InvalidOperationException("Состояние сохранения сериализовалось в null.");
        Stamp(save);
        return save.ToJsonString(options);
    }
}
