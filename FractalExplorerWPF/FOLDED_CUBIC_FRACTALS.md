# Celtic Mandelbar, Cubic Quasi Burning Ship и Flying Squirrel

Шесть отдельных WPF-режимов, три пары параметрической плоскости и Жюлиа. Каждый имеет плитку, вычисляемое превью, два готовых вида и свою категорию сохранений. Теперь разделы Мандельброта и Жюлиа содержат по 14 режимов.

## Формулы

Соглашения — [официальная документация Kalles Fraktaler](https://mathr.co.uk/kf/manual.html), разделы Mandelbar Celtic, Cubic Quasi Burning Ship и Cubic Flying Squirrel (Buffalo Imag). Здесь z = x + iy, w = u + iv. Модули результата берутся **до** добавления c.

| Параметрический ключ | Ключ Жюлиа | Шаг |
|---|---|---|
| `CelticMandelbar` | `JuliaCelticMandelbar` | w = (x − iy)²; z′ = |u| + iv + c |
| `CubicQuasiBurningShip` | `JuliaCubicQuasiBurningShip` | w = (|x| + iy)³; z′ = u − i|v| + c |
| `CubicFlyingSquirrel` | `JuliaCubicFlyingSquirrel` | w = (x + iy)³; z′ = u + i|v| + c |

На параметрической плоскости z₀ = 0, c задаётся пикселем. У Жюлиа z₀ задаётся пикселем, c фиксирована и выбирается на карте соответствующей параметрической формулы. Степень фиксирована: 2 у Celtic Mandelbar и 3 у двух кубических режимов; поле Power в файле сохранения формулу не меняет. Сглаживание новых кубических режимов использует log(3).

## Движок и интерфейс

Все шесть режимов рассчитываются на ЦП. Общие навигация, семь раскрасок, палитры, SSAA, сохранения, облако и экспорт доступны через `MandelbrotWindow`; варианты Жюлиа используют `JuliaConstantPickerWindow` и соответствующую миниатюру C. Отдельных классов окон не требуется.

Поддерживается глубокий зум до 1e1000 с точным центром BigFloat, адаптивной точностью опорной орбиты и double/FloatExp-отклонениями. Опорные компоненты кубических орбит и ещё не отражённая Im(Z³) также сохраняются в FloatExp: знак не теряется при уходе компоненты за диапазон double или её сокращении на линии отражения. Возмущение куба раскрывается точно как 3W²δ + 3Wδ² + δ³. Модули применяются через устойчивую разность |a + δa| − |a|, включая переходы через линии отражения. Вещественные BLA-матрицы ограничивают радиус по входным и выходным линиям отражения и остатку куба. Distance Estimation использует полный вещественный якобиан.

Ключи enum добавлены в конец: числовые значения прежних режимов сохранены. Формат сохранений не меняется; шесть категорий используют одноимённые идентификаторы.

## Проверка

Из корня репозитория:

```powershell
dotnet build .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.slnx
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- folded-cubic
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- poi Cubic --out <папка>
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- poi CelticMandelbar --out <папка>
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- mandelbrot-saves
dotnet run --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -- catalog --out <папка>
```

`folded-cubic` независимо проверяет 378 коротких орбит и сглаживание, шаги на осях/диагоналях, якобианы, семь окрасок, оба представления отклонений, реальное ускорение BLA до выхода орбиты, BigFloat-эталон до 1e1000 и освещение во внешней области без исчезновения рельефа, карты C и собственные превью. Параметрические кубические границы проверяются вокруг ±i√2, вычисленных в BigFloat; Жюлиа при c = 0 — вокруг единичной окружности.
