# Сборка, проверки и выпуск релиза

Всё, что нужно, чтобы собрать программу, убедиться, что она работает, и выпустить
релиз. Ветки и версии — [BRANCHING.md](BRANCHING.md).

---

## 1. Требования

| Что | Версия | Обязательно |
|---|---|---|
| .NET SDK | 8.0 | да |
| Visual Studio | 2022, 17.8+ | нет, всё делается из командной строки |
| PowerShell | 7.4 для скриптов `scripts/` | да для проверок |
| Inno Setup | 6.2+ | только для локальной сборки установщика |

Публикуемый файл самодостаточен: .NET и все библиотеки входят в него, устанавливать
рантайм на машину пользователя не нужно.

## 2. Сборка и запуск

```bash
git clone <url>
cd FabrikaBookBuilder
dotnet restore
dotnet build PhotoBookRenamer.sln -c Release
```

Запуск без Visual Studio:

```bash
dotnet run --project src\PhotoBook.Desktop.Avalonia -c Release
```

Готовый файл: `src\PhotoBook.Desktop.Avalonia\bin\Release\net8.0\PhotoBookRenamer.exe`.
Путь к нему знает одна функция — `Get-AppExe` в `scripts\common.ps1`; скрипты не
записывают его сами.

> **Сборка падает с MSB3021/MSB3027 или не обновляет файл** — почти всегда запущена сама
> программа: `PhotoBookRenamer.exe` занят. Закройте её и повторите сборку.

## 3. Проверки

Скрипты лежат в `scripts\` и запускаются при закрытой программе:

```bash
pwsh -File scripts\check-resource-refs.ps1
```

| Проверка | Что проверяет |
|---|---|
| `dotnet build PhotoBookRenamer.sln -c Release` | компиляция; предупреждения считаются ошибками |
| `dotnet test` | правила, сценарии и контракты |
| `dotnet format --verify-no-changes` | стиль и переводы строк |
| `check-resource-keys.ps1` | нет двух `x:Key` с одним именем |
| `check-resource-refs.ps1` | каждая ссылка `{StaticResource}` и `{DynamicResource}` разрешается |
| `audit-closed-steps.ps1` | перепроверяет по текущему коду каждый шаг, который считается закрытым |
| `check-workflow-scripts.ps1` | каждый блок `run:` в пайплайнах — корректный PowerShell |
| `check-release-notes.ps1` | в тексте релиза нет ссылок, адресов и разметки, которую не отрисует окно обновления |
| `verify-startup.ps1` | программа запускается и показывает окно |
| `test-*.ps1` | сценарии: экспорт, структура, переименование, прокрутка, диалоги |
| `capture-golden.ps1`, `compare-to-golden.ps1` | эталонные снимки экранов и сравнение с ними |

Сценарии работают с фикстурами: `make-test-fixture.ps1`, `make-combined-fixture.ps1`,
`make-big-fixture.ps1`, `make-empty-fixture.ps1`. Удаление после проверок:

```bash
pwsh -File scripts\clean-test-projects.ps1 -WhatIf   # посмотреть, что будет удалено
pwsh -File scripts\clean-test-projects.ps1
```

Индекс проектов (`%LOCALAPPDATA%\PhotoBookRenamer\Projects\projects.json`) фикстуры
правят **как текст**: он хранит не-ASCII как `\uXXXX`, и обход через `ConvertTo-Json`
портит кириллицу.

### Замеры производительности

```bash
# открытие проекта из списка: задержка ответа интерфейса, CPU, память
pwsh -File scripts\perf-open-avalonia.ps1 -Mode combined -Label "50x20"

# список проектов: время до первой карточки и до всех карточек
pwsh -File scripts\measure-project-load.ps1
```

`perf-open-avalonia.ps1` измеряет не «время клика», а задержку ответа потока интерфейса
на кросс-процессный запрос: её пользователь видит как зависание окна.

### Тесты

```bash
dotnet test                                              # все тесты
dotnet test --collect:"XPlat Code Coverage"               # с отчётом покрытия
```

125 тестов в `tests\PhotoBook.Application.Tests`: экспортный контракт, совместимость
формата проекта, раскладка слотов, проверка папок, выбор обложки, уменьшенные копии,
типы аргументов команд. Покрытие измеряется и не должно уменьшаться.

Образец проекта для проверки совместимости —
`tests\PhotoBook.Application.Tests\data\project-1.1.1.json`: формат, которым сохраняла
проект версия 1.1.1, с намеренными сложностями (кириллица как `\u`, книга с меньшим
числом разворотов, пустой слот, отсутствующие `imageWidth`). Пути вымышленные, данные
пользователей в репозиторий не попадают.

### Правило для пайплайна

Правку `.github/workflows/*.yml` нельзя отправлять, не выполнив её локально: YAML
остаётся валидным, когда команда внутри него собирает неверную строку, и ошибка выходит
только на этапе публикации.

1. `scripts\check-workflow-scripts.ps1` — синтаксис всех блоков `run:`.
2. Выполнить изменённый шаг локально, подставив значения вместо `${{ }}`.
3. Коммит.

## 4. Выпуск релиза

Релиз собирается **только** из `master`: на это настроен
`.github/workflows/build-and-release.yml`, и он же это проверяет.

Порядок:

1. Поднять версию в `src\PhotoBook.Desktop.Avalonia\PhotoBook.Desktop.Avalonia.csproj`:

   ```xml
   <Version>2.0.0</Version>
   ```

   Обязательный шаг. Сборка отказывается выпускать версию, для которой уже есть тег,
   или версию не больше последней опубликованной.

2. Написать заметки в `docs\release-notes\v<версия>.md` — они становятся описанием
   релиза на GitHub и показываются в окне обновления.

3. Прогнать проверки из раздела 3.

4. Создать пулреквест из рабочей ветки в `master`: прямая отправка в `master` запрещена
   защитой ветки.

5. Дождаться GitHub Actions и проверить, что релиз `v<версия>` создан и в нём есть
   `BookBuilder-Studio-Setup.zip`.

### Что делает пайплайн

| Шаг | Результат |
|---|---|
| `dotnet publish` | один самодостаточный `.exe` |
| Inno Setup | установщик `BookBuilder-Studio-Setup-<версия>.exe` |
| zip | `BookBuilder-Studio-Setup.zip` — файл с постоянным именем |
| GitHub Release | тег `v<версия>`, описание из `docs/release-notes/` |
| Предыдущие релизы | установщик заменяется во всех прошлых релизах |

Последний пункт объясняет, почему ссылка на скачивание не меняется от версии к версии:
клиент открывает «последний релиз» и получает свежий установщик, а установленные копии
находят обновление сами.

Постоянные ссылки:

- последний релиз: `https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest`
- файл напрямую: `https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest/download/BookBuilder-Studio-Setup.zip`

### Ручная пересборка

Вкладка **Actions → Build and Release → Run workflow**, в поле `confirm` ввести
`RELEASE`. Запуски сериализованы: новый встаёт в очередь.

## 5. Разбор проблем

| Симптом | Причина и что делать |
|---|---|
| MSB3021/MSB3027, файл не обновляется | запущена программа — закройте и повторите |
| Ошибки только в рантайме, сборка зелёная | проверить ресурсы: `check-resource-keys.ps1`, `check-resource-refs.ps1` |
| В программе квадраты вместо значков | сбились коды символов шрифта значков — `glyph-proof.ps1` |
| Повреждён список проектов | `scripts\repair-projects-json.ps1` |
| `projects.json` показывает мусор вместо кириллицы | файл правили через `ConvertTo-Json`; восстанавливать текстовой правкой |
| Скрипт не находит сборку | собрать `Release`; путь к exe знает `Get-AppExe` в `common.ps1` |
