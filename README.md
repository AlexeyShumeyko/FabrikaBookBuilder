<div align="center">

# Fabrika BookBuilder Studio

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20/%2011-0078D4?style=for-the-badge&logo=windows&logoColor=white)

**Настольная программа для подготовки тиража фотокниг к загрузке на печатный сайт.**

[Русский](#русский) · [English](#english)

</div>

---

## Русский

### Назначение

Фотограф верстает книгу: раскладывает фотографии по обложке и разворотам. Файлы нужно
передать на сайт, который принимает их по схеме имён — `001-00.jpg`, `001-01.jpg`.
Программа выполняет переименование и раскладку по папкам, проверяет результат и сообщает
о расхождениях.

### Возможности

**Два режима работы**

- **«Уникальные папки»** — каждая выбранная папка становится одной книгой. Обложка
  определяется автоматически (самое большое фото) или выбирается вручную, порядок
  разворотов меняется перетаскиванием. Количество файлов в папках должно совпадать;
  расхождение показывается до загрузки.
- **«Комбинированный»** — общий тираж: N книг по M разворотов. Фотография назначается
  конкретной книге, всем книгам сразу («во все развороты») или выбранным книгам.

**Экспорт**

- файлы копируются в выбранную папку с переименованием по схеме `KKK-FF.jpg`
  (`001-00.jpg`, `001-01.jpg`, …). **Оригиналы не изменяются и не удаляются**;
- в комбинированном режиме используется сквозная схема: `000-FF.jpg` на позицию,
  общую для всех книг, и `KKK-FF.jpg` там, где книга переопределяет разворот;
- в режиме уникальных папок результат раскладывается по подпапкам, по одной на книгу;
- каждая выгрузка попадает в новую папку (`PhotoBookExport`, `PhotoBookExport1`, …);
  существующие папки не перезаписываются;
- перед выгрузкой проект сохраняется.

**Проекты**

Хранятся локально в `%LOCALAPPDATA%\PhotoBookRenamer`, открываются из списка на
главном экране, показывают прогресс по тиражу. Проект, созданный предыдущей версией,
открывается без изменений.

**Обновления**

При запуске программа проверяет наличие нового релиза и предлагает обновиться.
Установка поверх текущей версии, отдельная копия не требуется.

### Системные требования

| | |
|---|---|
| ОС | Windows 10 (1809 и новее) или Windows 11, x64 |
| Рантайм | не требуется: публикуемая сборка самодостаточна |
| Диск | 161 МБ под программу (50 МБ в zip) |
| Память | 160 МБ в покое, до 500 МБ на проекте из 20 книг; см. известные ограничения |

Windows 7 и 8.1 не поддерживаются: программа собрана под .NET 8, который на них не
работает.

### Установка и обновление

1. Скачать [последний релиз](https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest)
2. Распаковать `BookBuilder-Studio-Setup.zip` и запустить
   `BookBuilder-Studio-Setup-<версия>.exe`
3. Установка требует прав администратора (запрашиваются один раз)

### Технологии

| | |
|---|---|
| Платформа | .NET 8, `net8.0-windows`, self-contained, один файл |
| Интерфейс | WPF, MVVM (`CommunityToolkit.Mvvm`) |
| Работа с изображениями | `SixLabors.ImageSharp` |
| Обновления | `Octokit`, GitHub Releases |
| Установщик | Inno Setup, сборка в GitHub Actions |
| Тесты | xUnit, 75 тестов: экспортный контракт, совместимость файла проекта, порты |

### Структура репозитория

```
PhotoBookRenamer/
├── src/
│   ├── PhotoBook.Core/           Проект, книга, страница, режим
│   ├── PhotoBook.Application/    Сценарии и порты: сборка тиража, план выгрузки,
│   │                            генерация имён, чтение и запись проекта
│   ├── PhotoBook.Infrastructure/ Файлы, изображения, журнал, обновления
│   └── PhotoBook.Desktop.Wpf/  Оболочка WPF: представления, модели представления,
│                           конвертеры, диалоги, App.xaml, точка сборки
├── tests/            Автоматические тесты
├── scripts/          Проверки: экспорт, вёрстка, прокрутка, запуск, снимки экранов
├── docs/decisions/   Принятые технические решения
└── .github/workflows/Сборка и выпуск релизов
```

### Известные ограничения

| Ограничение | Измерено |
|---|---|
| Только Windows | Решение о переносе: [docs/decisions/0001](docs/decisions/0001-ui-engine-avalonia.md) |
| Проект из 20 книг по 9 фотографий в режиме уникальных папок открывается около 42 секунд при первом запуске и 3 секунд при повторном; причина — декодирование исходных файлов целиком вместо уменьшенной копии | `scripts/perf-open.ps1` |
| В режиме уникальных папок количество файлов в папках должно совпадать; расхождение показывается до загрузки | `FileService.ValidateFoldersDetailedAsync` |
| Кнопки «?» и «Контакты» в шапке отвечают «в разработке» | раздел справки написан под движок 2.0 |
| Покрытие автоматическими тестами — экспортный контракт; остальная логика проверяется сценариями `scripts/` | [docs/decisions/0005](docs/decisions/0005-tests-are-mandatory.md) |

### Для разработчиков

- [ARCHITECTURE.md](ARCHITECTURE.md) — архитектура и правила реализации
- [BUILD.md](BUILD.md) — сборка, проверки, выпуск релиза
- [BRANCHING.md](BRANCHING.md) — ветки и версии
- [docs/decisions](docs/decisions) — принятые решения с обоснованием
- Заметки к релизам: [`docs/release-notes/`](docs/release-notes)

---

## English

### Purpose

A Windows desktop application that prepares a photo book print run for upload to a
printing site. The operator lays photos out on a cover and spreads; the program copies
them into a target folder under the naming scheme the site requires (`001-00.jpg`,
`001-01.jpg`, …). **Originals are never modified or deleted.**

### Features

- **Unique Folders mode** — each folder is one book; cover detected automatically or
  chosen manually; spread order can be rearranged; the file count must match across
  folders.
- **Combined mode** — N books × M spreads in one run; a photo can be applied to one book,
  to all books at once, or to a chosen set.
- **Export** — renames and copies into a new folder per export; optional per-book
  subfolders; run-wide `000-FF` naming in combined mode; the project is saved first.
- **Projects** — stored under `%LOCALAPPDATA%\PhotoBookRenamer`, resumable, with progress
  per book; projects from earlier versions open unchanged.
- **Auto-update** — checks GitHub Releases on start and installs over the current version.

### Requirements

Windows 10 (1809+) or Windows 11, x64. The published build is self-contained, so no
separate .NET installation is required. Windows 7 and 8.1 are not supported.

### Install

Download the [latest release](https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest),
unpack `BookBuilder-Studio-Setup.zip` and run the installer. Administrator rights are
requested once.

### Stack

.NET 8 · WPF · MVVM · SixLabors.ImageSharp · Octokit · Inno Setup · GitHub Actions
(`net8.0-windows`, self-contained, single file) · xUnit

### Known limitations

Windows only. A project of 20 books × 9 photographs opens in about 42 s on first run and
3 s afterwards; the cost is decoding whole source files instead of a reduced copy. The
header "?" and "contacts" buttons answer "in development". Automated tests cover the
export contract only.

### For developers

[ARCHITECTURE.md](ARCHITECTURE.md) · [BUILD.md](BUILD.md) ·
[BRANCHING.md](BRANCHING.md) · [docs/decisions](docs/decisions)
