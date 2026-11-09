<div align="center">

# Fabrika BookBuilder Studio

Приложение для Windows, которое готовит тираж фотокниг к загрузке на печатный сайт:
раскладывает копии фотографий по обложке и разворотам и присваивает им имена по схеме
сайта. Исходные файлы не изменяются.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=sharp&logoColor=white)
![Avalonia](https://img.shields.io/badge/Avalonia-11.2-8B44AC?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D4?style=for-the-badge&logo=windows&logoColor=white)
![Tests](https://img.shields.io/badge/tests-125%20passing-3FB950?style=for-the-badge)
![License](https://img.shields.io/badge/license-proprietary-6E7681?style=for-the-badge)
![Version](https://img.shields.io/badge/version-2.0.0-4F46E5?style=for-the-badge)

</div>

---

## Возможности

### Режимы работы

| Режим | Правила |
|---|---|
| Уникальные папки | Каждая выбранная папка — одна книга. Обложка определяется автоматически (наибольший файл) или выбирается вручную; порядок разворотов меняется перетаскиванием. Число файлов в папках должно совпадать, расхождение показывается до загрузки. |
| Комбинированный | Общий тираж: N книг по M разворотов. Фотография назначается одной книге, всем книгам или выбранному набору. |

### Экспорт

- Копии раскладываются в новую папку с именами `KKK-FF.jpg` (`001-00.jpg`, `001-01.jpg`,
  …). Существующие папки не перезаписываются.
- Комбинированный режим использует сквозную схему: `000-FF.jpg` для позиции, общей для
  всех книг, `KKK-FF.jpg` — там, где книга переопределяет разворот.
- Уникальные папки раскладываются по подпапкам, по одной на книгу.
- Проект сохраняется перед выгрузкой.

### Проекты

Проекты хранятся в `%LOCALAPPDATA%\PhotoBookRenamer`, открываются из списка на главном
экране и показывают готовность по тиражу. Файл проекта версии 1.1.x открывается без
изменений.

### Обновление

При запуске программа проверяет наличие нового релиза на GitHub и устанавливает его
поверх текущей версии.

## Требования

| Параметр | Значение |
|---|---|
| ОС | Windows 10 версии 1809 или новее, Windows 11; x64 |
| .NET | не требуется: публикуемая сборка самодостаточна |
| Диск | 160 МБ |
| Память | от 160 МБ без открытого проекта |

Windows 7 и Windows 8.1 не поддерживаются: платформа .NET 8 работает на Windows 10 и
Windows 11.

## Установка

1. Скачать [последний релиз](https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest).
2. Распаковать `BookBuilder-Studio-Setup.zip`.
3. Запустить `BookBuilder-Studio-Setup-<версия>.exe`. Права администратора требуются один
   раз, при установке.

## Технологии

| Слой | Технология |
|---|---|
| Платформа | .NET 8, `net8.0`, self-contained, один исполняемый файл |
| Интерфейс | Avalonia 11.2, MVVM (CommunityToolkit.Mvvm) |
| Изображения | SixLabors.ImageSharp |
| Обновления | Octokit, GitHub Releases |
| Установщик | Inno Setup, сборка в GitHub Actions |
| Тесты | xUnit, 125 тестов |

## Производительность

Замеры на машине разработки (Windows 10, 12 потоков, SSD). Скрипты:
`scripts/measure-project-load.ps1`, `scripts/perf-open-avalonia.ps1`.

| Сценарий | Время |
|---|---|
| Список проектов, 43 карточки, возврат из проекта | 0,6 с |
| Список проектов, первый запуск программы | 2,4 с, включая запуск процесса |
| Комбинированный проект 50 книг × 20 разворотов, до разворотов на экране | 4,5 с |
| Уменьшенные копии, 374 фотографии (1,95 ГБ), первый раз | 11,0 с |
| Уменьшенные копии, повторно | 25 мс |

Время определяется декодированием JPEG. Разбор измерений — `doc/PERF-2.0.md`.

## Структура репозитория

```
FabrikaBookBuilder/
├── src/
│   ├── PhotoBook.Core/              проект, книга, разворот, план выгрузки
│   ├── PhotoBook.Application/       сценарии и порты
│   ├── PhotoBook.Infrastructure/    файлы, изображения, проекты, журнал, обновления
│   ├── PhotoBook.Presentation/      модели представления и команды
│   └── PhotoBook.Desktop.Avalonia/  оболочка: представления, стили, точка сборки
├── tests/                           автоматические тесты
├── scripts/                         проверки, сценарии, замеры, снимки экранов
├── docs/decisions/                  принятые технические решения
├── docs/release-notes/              тексты релизов
└── .github/workflows/               проверка сборки и выпуск релизов
```

## Ограничения

| Ограничение | Основание |
|---|---|
| Только Windows | исполняемый файл и установщик собраны под Windows; перенос на другой движок не планируется |
| Уникальные папки: число файлов в папках должно совпадать | расхождение показывается до загрузки и не исправляется автоматически |
| Кнопки «Тема», «Контакты», «Войти» не реализованы | раздел справки и учётные записи входят в следующий выпуск |
| Автоматические тесты покрывают контракты и сценарии, не интерфейс | [docs/decisions/0005](docs/decisions/0005-tests-are-mandatory.md) |

## Документация

- [ARCHITECTURE.md](ARCHITECTURE.md) — слои, порты, правила реализации
- [BUILD.md](BUILD.md) — сборка, проверки, выпуск релиза
- [BRANCHING.md](BRANCHING.md) — ветки, версии, язык
- [docs/decisions](docs/decisions) — принятые решения с обоснованием
- [docs/release-notes](docs/release-notes) — тексты релизов

## Лицензия

Собственническая лицензия. Исключительные права принадлежат автору; организации, в которой
автор состоит в трудовых отношениях, предоставлено право полного использования. Любое иное
использование, включая коммерческое, требует письменного разрешения. Условия — в файле
[LICENSE](LICENSE).

---

## English

A Windows application that prepares a photo book print run for upload to a printing site:
it places copies of the photographs on the cover and spreads and names them as the site
requires. Originals are never modified or deleted.

**Modes.** Unique Folders — one folder per book, cover detected or chosen, spreads
reordered by dragging, equal file counts required. Combined — N books × M spreads, a
photograph applies to one book, all books or a chosen set.

**Export.** Copies into a new folder per export, per-book subfolders in Unique Folders,
`000-FF` run-wide names in Combined, project saved first.

**Requirements.** Windows 10 version 1809 or later, Windows 11, x64. The published build is
self-contained.

**Stack.** .NET 8 · Avalonia 11.2 · MVVM (CommunityToolkit.Mvvm) · SixLabors.ImageSharp ·
Octokit · Inno Setup · GitHub Actions · xUnit (125 tests).

**Licence.** Proprietary. All rights reserved by the author; the author's employer is
granted full use. Any other use, including commercial use, requires written permission. See
[LICENSE](LICENSE).
