# Точка продолжения: защита ввода WPF

Остановка по просьбе пользователя 19 октября 2026 года перед исчерпанием квоты. Работа **не завершена**. Все изменения сохранены в рабочем дереве, коммитов и PR не создавалось. Не откатывать и не начинать заново.

## Задача и согласованное поведение

Пользователь попросил проверить все фракталы: неверные значения тихо блокируют новый рендер, прежнее изображение остаётся. Затем разрешил внедрить единую систему исправления ввода и сделать её обязательной для новых добавлений.

Только WPF. Исходный аудит — `INPUT_VALIDATION_AUDIT.md`; контракт внедрения — `INPUT_VALIDATION.md`. AGENTS.md и CLAUDE.md обновлены одинаково: новые/изменяемые числовые поля обязаны использовать общий механизм.

Число во время набора остаётся черновиком. Enter, потеря фокуса и явное действие завершают ввод: выход за диапазон ограничивается, мусор/NaN/Infinity возвращается к последнему корректному значению, пояснение видно у поля. Escape восстанавливает значение. Связанные поля проверяются совместно. Формулы не угадываются и не заменяются; ошибка должна показываться явно. Сохраняется точность decimal и FloatExp вплоть до 1e1000.

## Реализовано в рабочем дереве

- Новый общий `Controls/NumericInput.cs`: Integer/Real/Decimal/Zoom/Angle, динамические границы, зависимые пары, Prepare/CommitAll/Include, удаление динамических полей Forget, сообщения ShowIssue. Integer использует BigInteger для переполнения; нечётные сетки поддерживают шаг 2; углы оборачиваются; отрицательный Flame scale сохраняет прежнюю семантику abs.
- NumericSpinner завершает ввод перед/после шага. Общий стиль TextBox показывает рамку и tooltip. Поля со связью на Slider.Value регистрируются автоматически при Loaded.
- Явная регистрация в новых partial-файлах `Views/*Window.NumericInput.cs`: семейства Мандельброта/Жюлиа, Newton/Nova/Phoenix/Collatz, BasinExplorer, DomainColoring, Buddhabrot, Apollonian, Serpinsky, IFS/Flame, математические лаборатории, все 3D-режимы, Gray–Scott, Turing, SnowCrystal, Sandpile, DLA, L-системы, McMullen, TranscendentalJulia, DynamicSystem, редакторы преобразований и палитры. В соответствующих xaml.cs добавлены Prepare и CommitAll в пути чтения и явных действий.
- Панели 3D-симуляций и редакторы Kleinian/Antoine/Newton3D/Buddhabrot4D/TuringReaction/LSystemRandomizer подключены к общему механизму. Динамические слои Turing регистрируются/забываются при Loaded/Unloaded и перестроении.
- Zoom-поля и камера используют фактическое применённое значение через appliedValue, чтобы округлённый текст не снижал точность. Камера: конечные координаты, безопасные границы преобразования во float; yaw/roll оборачиваются.
- Gray–Scott: размеры 32..8192, произведение не больше 16777216, связанный диапазон окраски. Serpinsky: диапазон итераций зависит от режима. DynamicSystem: диапазоны по режимам и зависимые параметры, включая бюджет Popcorn.
- Документы INPUT_VALIDATION.md, WINDOWS.md и Verification/README.md описывают механизм; обязательные правила внесены в обе копии инструкций.
- Новая группа `numeric-input` в Verification (NumericInputVerification.cs и Program.cs). Старые проверки, ожидавшие тихий отказ/исключение на неверном поле, обновлены под исправление с сообщением.

## Что проверено, а что ещё нет

Успешные сборки WPF-решения и Verification: 0 предупреждений/ошибок. **Но после последней успешной сборки внесены дополнительные изменения, перечисленные ниже. Текущее дерево ещё не собрано.**

Успешные группы на предшествующей сборке:

- numeric-input: 215 конфигураций окон, 9322 вхождения зарегистрированных полей. Для каждого мусор/NaN/переполнение, завершение и последующее чтение параметров. Есть настоящий повторный рендер Mandelbrot после неверного поля, точные decimal/FloatExp, пары, черновик и исправление.
- numeric;
- deep-viewport: 108 состояний, 7 окон, 1560 проверок;
- fractal3d-modes: 41 режим, 2374 строки снимка и настройки 3D;
- basins (все 11, включая обновлённые проверки UI), planar;
- newton: 21 конфигурация, глубокий зум 1e1000;
- phoenix: глубокий зум 1e1000;
- deep-frames: 287 кадров.

Регрессионный цикл остановлен Ctrl+C через exec session 78798; инструмент вернул завершение с exit code 1, цикл больше не выполняется. В момент остановки выполнялась `mandelbrot-saves`: частичные проверки прошли до JuliaCelticMandelbar (515 случаев), **полный проход группы не подтверждён**. Не трактовать этот exit code как найденную регрессию: это ручное прерывание.

Полный журнал частичного цикла:
`C:\Users\pro\.codex\visualizations\2026\10\19\01a152ed-e827-78c1-a765-4595476984e1\InputAudit\validation-tests.log`.

Ранее первый цикл упал в basins на устаревшем ожидании исключения от NaN. Ожидание исправлено, сборка и basins/planar прошли повторно. Это не оставшаяся ошибка.

### Последние изменения, ещё НЕ собранные и НЕ проверенные

1. NumericInput: динамические границы Slider, fallback из точного Slider.Value, Rule.AppliedText и публичные appliedValue для Real/Decimal/Angle/Zoom, точный FloatExp.ToInvariantString.
2. Prepare вызывает Commit(updateSource:false), чтобы при каждом кадре не отправлять округлённый отображаемый текст в модель. При неизменённом корректном вводе UpdateSource вызывается только при updateSource && binding.IsDirty. **Проверить, что BindingExpression.IsDirty доступен и сборка проходит.**
3. LastGood и Escape валидируют appliedValue, чтобы некорректная модель не испортила fallback. Zoom-регистрации семи глубоких окон, Buddhabrot/Serpinsky и камера передают фактические значения.
4. DomainColoring: исключение разбора формулы показывается через ShowIssue на FormulaBox, текст сохраняется, корректная формула снимает сообщение. Lyapunov Pattern получает явное сообщение о неверном шаблоне.
5. Новые тесты: Slider со StringFormat F2 не теряет точность при Prepare/восстановлении; явное обновление binding и динамический Maximum; неактивная Pair; FloatExp 1e750 с округлённым отображением; неверная/исправленная формула DomainColoring и неверный шаблон Lyapunov.

## Продолжить в таком порядке

1. Прочитать AGENTS.md и CLAUDE.md, INPUT_VALIDATION.md, этот файл; посмотреть git diff. Сохранить существующие изменения.
2. Собрать **текущее** решение и Verification, прогнать numeric-input. Устранить ошибки последних изменений, особенно BindingExpression.IsDirty, точность binding и формулы.
3. Исправить найденный, но пока не исправленный момент в `Views/GrayScottWindow.NumericInput.cs`: границы концов диапазона окраски сейчас искусственно ±double.MaxValue/4 ради конечной разности. Это отбрасывает допустимые большие значения. Предпочтительно полный конечный диапазон double и Pair с условием `max > min && double.IsFinite(max - min)` с понятным пояснением.
4. Продолжить оставшиеся группы регрессии (ниже). Общий механизм недавно менялся, поэтому при необходимости повторить затронутые уже прошедшие группы на новой сборке.
5. Особое внимание обновлённым GPU-проверкам restart/apply и экрану подготовки: они пока не выполнялись после изменений. Пассивный набор должен оставаться черновиком и не перезапускать симуляцию; явный Restart/Apply исправляет поле и выполняется.
6. Просмотреть сложные ограничения: сумма повторов гибрида <=256 остаётся прежней HybridValidationText; гиперболическая пара (p-2)(q-2)>4 остаётся явной проверкой модели. Не подбирать формулы автоматически. Проверить полноту сообщений и отсутствие тихого отказа.
7. Финальная сборка, релевантные проверки, git diff --check, byte equality AGENTS/CLAUDE. Только после этого сообщать о завершении.

Оставшиеся группы предыдущего последовательного цикла:

```text
mandelbrot-saves julia-saves folded-polynomials rauzy laboratory mcmullen
transcendental-julia snow-crystal sandpile lsystemrandom gray-scott turing
gray-scott-gpu turing-gpu preparation3d newton3d kleinian antoine buddhabrot4d
gray-scott3d turing3d cahn-hilliard3d lenia3d physarum3d kobayashi3d
excitable3d ginzburg-landau3d swift-hohenberg3d phase-field-crystal3d
popcorn hopalong symmetric-icons
```

Изменённые старые проверки: PlanarBasinVerification, PhysicalBasinVerification, TuringVerification (Loaded шаблонных полей), GrayScottGpuVerification/TuringGpuVerification (0 исправляется до 32, вместо огромной сетки 99999), Newton3DVerification, Preparation3DVerification, Excitable/GinzburgLandau/SwiftHohenberg/PhaseFieldCrystal3DVerification. В последних ожидается корректное применение явной команды и шаг от исправленного снимка, а не старое молчаливое отклонение. Проверки пассивного набора в других симуляциях сохранены.

## Команды и окружение

Команды из корня репозитория. Обычная сборка в этой среде столкнулась с отказом чтения `C:\Users\pro\AppData\Local\Microsoft SDKs` при probing SDK. Рабочий обход без повышения прав — передать TargetPlatformMoniker:

```powershell
dotnet build .\FractalExplorerWPF\FractalExplorerWPF\FractalExplorerWPF.slnx --no-restore -p:TargetPlatformMoniker=Windows
dotnet build .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj --no-restore -p:TargetPlatformMoniker=Windows
dotnet run --no-restore --project .\FractalExplorerWPF\Verification\SavePreviewVerification.csproj -p:TargetPlatformMoniker=Windows -- numeric-input
```

После сборки можно запускать группы напрямую, избегая повторной сборки:

```powershell
dotnet .\FractalExplorerWPF\Verification\bin\Debug\net10.0-windows\SavePreviewVerification.dll <группа>
git diff --check
git diff --no-index -- AGENTS.md CLAUDE.md
```

Последние две проверки перед остановкой прошли. Настоящие данные пользователя не менялись: Verification использует OverrideDataRoot. Скриншоты не делались. bin/obj вручную не редактировались. Повышения прав и отказа автоматического approval-review не было; попытка CIM-инвентаризации процессов получила обычный Access denied, после чего цикл успешно остановлен Ctrl+C.
