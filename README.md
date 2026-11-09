<div align="center">

# Fabrika BookBuilder Studio

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Avalonia](https://img.shields.io/badge/Avalonia-11.2-8B44AC?style=for-the-badge)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D4?style=for-the-badge&logo=windows&logoColor=white)

**Настольная программа для подготовки тиража фотокниг к загрузке на печатный сайт.**

[Русский](#русский) · [English](#english)

</div>

---

## Русский

### Назначение

Фотограф верстает книгу: раскладывает фотографии по обложке и разворотам. Файлы нужно
передать на печатный сайт, который принимает их по схеме имён — `001-00.jpg`,
`001-01.jpg`. Программа раскладывает копии по нужным именам, проверяет результат и
сообщает о расхождениях. **Исходные файлы не изменяются и не удаляются.**

### Возможности

**Два режима работы**

- **«Уникальные папки»** — каждая выбранная папка становится одной книгой. Обложка
  определяется автоматически (самое большое фото) или выбирается вручную, порядок
  разворотов меняется перетаскиванием. Количество файлов в папках должно совпадать;
  расхождение показывается до загрузки.
- **«Комбинированный»** — общий тираж: N книг по M разворотов. Фотография назначается
  конкретной книге, всем книгам сразу или выбранным книгам.

**Экспорт**

- копии раскладываются в новую папку с именами по схеме `KKK-FF.jpg` (`001-00.jpg`,
  `001-01.jpg`, …); существующие папки не перезаписываются;
- в комбинированном режиме используется сквозная схема `000-FF.jpg` для позиции, общей
  для всех книг, и `KKK-FF.jpg` там, где книга переопределяет разворот;
- в режиме уникальных папок результат раскладывается по подпапкам, по одной на книгу;
- проект сохраняется перед выгрузкой.

**Проекты**

Хранятся локально в `%LOCALAPPDATA%\PhotoBookRenamer`, открываются из списка на главном
экране и показывают готовность по тиражу. Проект, созданный версией 1.1.x, открывается
без изменений.

**Обновления**

При запуске программа проверяет наличие нового релиза и предлагает обновиться.
Устанавливается поверх текущей версии.

### Системные требования

| | |
|---|---|
| ОС | Windows 10 (1809 и новее) или Windows 11, x64 |
| Рантайм | не требуется: публикуемая сборка самодостаточна |
| Диск | около 160 МБ под программу |
| Память | от 160 МБ в покое; зависит от размера проекта |

Windows 7 и 8.1 не поддерживаются: программа собрана под .NET 8, который на них не
работает.

### Установка и обновление

1. Скачать [последний релиз](https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest).
2. Распаковать `BookBuilder-Studio-Setup.zip` и запустить
   `BookBuilder-Studio-Setup-<версия>.exe`.
3. Установка требует прав администратора (запрашиваются один раз).

### Технологии

| | |
|---|---|
| Платформа | .NET 8, `net8.0`, self-contained, один файл |
| Интерфейс | Avalonia 11.2, MVVM (`CommunityToolkit.Mvvm`) |
| Работа с изображениями | `SixLabors.ImageSharp` |
| Обновления | `Octokit`, GitHub Releases |
| Установщик | Inno Setup, сборка в GitHub Actions |
| Тесты | xUnit, 125 тестов |

### Производительность

Замерено на рабочей машине (12 потоков, SSD), скриптами `scripts/measure-project-load.ps1`
и `scripts/perf-open-avalonia.ps1`.

| Сценарий | Значение |
|---|---|
| Список проектов, 43 карточки, возврат из проекта | 0,6 с |
| Список проектов, первый запуск программы | 2,4 с, включая запуск процесса |
| Комбинированный проект 50 книг × 20 разворотов, до разворотов на экране | 4,5 с |
| Уменьшенные копии, 374 фотографии (1,95 ГБ), первый раз | 11,0 с |
| Уменьшенные копии, повторно | 25 мс |

Порядок величин определяется декодированием JPEG. Подробный разбор и история измерений —
`doc/PERF-2.0.md`.

### Структура репозитория

```
FabrikaBookBuilder/
├── src/
│   ├── PhotoBook.Core/              проект, книга, разворот, план выгрузки
│   ├── PhotoBook.Application/       сценарии и порты
│   ├── PhotoBook.Infrastructure/    файлы, изображения, проекты, журнал, обновления
│   ├── PhotoBook.Presentation/      модели представления, команды, конвертеры
│   └── PhotoBook.Desktop.Avalonia/  оболочка: представления, стили, точка сборки
├── tests/                           автоматические тесты
├── scripts/                         проверки, сценарии, замеры, снимки экранов
├── docs/decisions/                  принятые технические решения
├── docs/release-notes/              тексты релизов для пользователей
└── .github/workflows/               проверка сборки и выпуск релизов
```

### Известные ограничения

| Ограничение | Основание |
|---|---|
| Только Windows | исполняемый файл и установщик собраны под Windows; перенос на другой движок не планируется |
| В режиме уникальных папок число файлов в папках должно совпадать | расхождение показывается до загрузки, а не исправляется автоматически |
| Кнопки «Тема», «Контакты» и «Войти» — заготовки | раздел справки и учётные записи входят в следующий выпуск |
| Тесты покрывают контракты и сценарии, а не интерфейс | [docs/decisions/0005](docs/decisions/0005-tests-are-mandatory.md) |

### Документация

- [ARCHITECTURE.md](ARCHITECTURE.md) — слои, порты, правила реализации
- [BUILD.md](BUILD.md) — сборка, проверки, выпуск релиза
- [BRANCHING.md](BRANCHING.md) — ветки, версии, язык
- [docs/decisions](docs/decisions) — принятые решения с обоснованием
- [docs/release-notes](docs/release-notes) — тексты релизов

---

## English

### Purpose

A Windows desktop application that prepares a photo book print run for upload to a printing
site. The operator lays photographs out on a cover and spreads; the program copies them into
a target folder under the naming scheme the site requires (`001-00.jpg`, `001-01.jpg`, …).
**Originals are never modified or deleted.**

### Features

- **Unique Folders** — each folder is one book; the cover is detected or chosen; spread order
  is rearranged by dragging; the file count must match across folders.
- **Combined** — N books × M spreads in one run; a photograph goes to one book, to all books,
  or to a chosen set.
- **Export** — copies into a new folder per export, optional per-book subfolders, run-wide
  `000-FF` naming in combined mode; the project is saved first.
- **Projects** — stored under `%LOCALAPPDATA%\PhotoBookRenamer`, with progress per book;
  projects from 1.1.x open unchanged.
- **Auto-update** — checks GitHub Releases at start and installs over the current version.

### Requirements

Windows 10 (1809+) or Windows 11, x64. The published build is self-contained; no separate
.NET installation is required.

### Stack

.NET 8 · Avalonia 11.2 · MVVM (CommunityToolkit.Mvvm) · SixLabors.ImageSharp · Octokit ·
Inno Setup · GitHub Actions · xUnit (125 tests)

### Performance

Measured on the development machine: projects list 0.6 s when returning from a project,
2.4 s on first launch including process start; a 50 books × 20 spreads combined project
takes 4.5 s to show its spreads; 374 photographs produce reduced copies in 11.0 s cold and
25 ms warm.

### Documentation

[ARCHITECTURE.md](ARCHITECTURE.md) · [BUILD.md](BUILD.md) · [BRANCHING.md](BRANCHING.md) ·
[docs/decisions](docs/decisions)
