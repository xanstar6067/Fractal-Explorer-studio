# Перпендикулярные Мандельброт и Жюлиа

В WPF добавлены восемь самостоятельных пунктов каталога. Каждый имеет собственную категорию сохранений, два готовых вида, рендеримое превью и общий интерфейс семейства Мандельброта. Степень фиксирована: 2. Все восемь считаются на ЦП.

## Формулы

Используется соглашение [Kalles Fraktaler](https://mathr.co.uk/kf/manual.html#formulas), где z = x + iy, c — комплексная константа. Модули применяются перед прибавлением c.

| Вариант | x следующего шага | y следующего шага |
|---|---|---|
| Perpendicular Mandelbrot | x² − y² + Re c | −2·\|x\|·y + Im c |
| Perpendicular Burning Ship | x² − y² + Re c | −2·x·\|y\| + Im c |
| Perpendicular Celtic | \|x² − y²\| + Re c | −2·\|x\|·y + Im c |
| Perpendicular Buffalo | \|x² − y²\| + Re c | −2·x·\|y\| + Im c |

В параметрическом режиме c задаётся пикселем, z₀ = 0. В Жюлиа c фиксирована, z₀ задаётся пикселем. Миниатюра и отдельная карта выбора c используют соответствующий параметрический режим.

## Рендер и сохранения

CPU и decimal используют те же формулы. Глубокий зум до 1e1000 считает опорную орбиту в адаптивной BigFloat-точности, разности модулей — без потери малой δ, пиксели — в double или FloatExp. Все семь окрасок работают через общий движок. Distance Estimation получает полный вещественный якобиан; BLA ограничивает пропуски расстоянием до каждой линии сгиба, включая x² = y² у Celtic/Buffalo.

Новые значения перечисления добавлены в конец: числовые значения старых режимов сохранены. Категории — `PerpendicularMandelbrot`, `PerpendicularBurningShip`, `PerpendicularCeltic`, `PerpendicularBuffalo` и те же имена с префиксом `Julia`. Общие JSON/PNG, менеджер, экспорт и облачный репозиторий используют эти отдельные категории.

## Проверки

Из корня репозитория:

```powershell
dotnet build .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.slnx
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- perpendicular-family
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- poi Perpendicular --out <папка>
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- mandelbrot-saves
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- catalog
```

Проверки используют отдельный каталог данных и не обращаются к пользовательским сохранениям.
