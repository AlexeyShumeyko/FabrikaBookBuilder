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

### Зачем нужна

Фотограф верстает книгу: раскладывает фотографии по обложке и разворотам. Потом
файлы нужно передать на сайт, который принимает их по строгой схеме имён — `001-00.jpg`,
`001-01.jpg` и так далее. Раньше это делалось вручную: переименовать, разложить по
папкам, пересчитать.

Программа делает это за один шаг и проверяет результат.

### Что умеет

**Два режима работы:**

- **«Уникальные папки»** — каждая выбранная папка становится одной книгой. Обложка
  определяется автоматически (самое большое фото) или выбирается вручную, порядок
  разворотов меняется перетаскиванием. В папках может быть разное количество файлов —
  программа покажет предупреждение, если в папках разное число файлов, потому что
  корректная сборка требует одинаковой структуры.
- **«Комбинированный»** — общий тираж: N книг по M разворотов. Фотография кладётся в
  конкретную книгу, сразу во все книги («во все развороты») или в выбранные книги. Так
  большие заказы собираются быстро: общая обложка и общие развороты заводятся один раз.

**Экспорт:**

- файлы копируются в выбранную папку с переименованием по схеме `КК-ФФ.jpg`
  (`001-00.jpg`, `001-01.jpg`, …). **Оригиналы не изменяются и не удаляются** — программа
  только читает их;
- в комбинированном режиме используется сквозная схема: одна `000-ФФ.jpg` на позицию,
  общую для всех книг, и `ККК-ФФ.jpg` там, где конкретная книга переопределяет разворот;
- в режиме уникальных папок можно разложить результат по подпапкам — по одной на книгу;
- каждая выгрузка попадает в свою новую папку (`PhotoBookExport`, `PhotoBookExport1`, …),
  уже занятые папки не трогаются;
- перед экспортом проект сохраняется, иначе изменения потерялись бы.

**Проекты:** сохраняются локально, открываются из списка на главном экране, показывают
прогресс по тиражу. Один и тот же проект можно продолжать позже.

**Обновления:** при запуске программа проверяет, не вышел ли новый релиз, и предлагает
обновиться. Обновление ставится поверх текущей версии, установка новой копии не требуется.

### Системные требования

| | |
|---|---|
| ОС | Windows 10 (1809 и новее) или Windows 11, x64 |
| Дополнительно | ничего не нужно: .NET и все библиотеки входят в файл программы |
| Диск | ~400 МБ |
| Память | ~150 МБ в покое, до ~250 МБ на крупном проекте |

Windows 7 и 8.1 **не поддерживаются**: программа собрана под .NET 8, который на них не
работает. Поддержка Windows 7 возможна только в виде отдельной сборки под
.NET Framework 4.8 — такого релиза пока нет.

### Установка и обновление

1. Скачать последний релиз: [ссылка всегда одна и та же](https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest)
2. Запустить `BookBuilder-Studio-Setup.zip`, распаковать и запустить
   `BookBuilder-Studio-Setup-<версия>.exe`
3. Установка требует прав администратора (Windows спросит один раз)

После установки программа сама предлагает обновления при запуске.

### Технологии

| | |
|---|---|
| Платформа | .NET 8, Windows Desktop |
| Интерфейс | WPF, MVVM (`CommunityToolkit.Mvvm`) |
| Работа с изображениями | `SixLabors.ImageSharp` (только чтение размеров и уменьшенные копии) |
| Файловый слой | `System.IO.Abstractions` |
| Обновления | `Octokit` + GitHub Releases |
| Установщик | Inno Setup, сборка в GitHub Actions |
| Целевая платформа | `net8.0-windows`, self-contained, один файл |

### Структура проекта

```
PhotoBookRenamer/
├── Domain/           Сущности предметной области: проект, книга, страница, файл
├── Application/      Сценарии: сборка тиража, план экспорта, генерация имён
├── Infrastructure/   Файлы, изображения, логирование, обновления
├── Presentation/     WPF: представления, модели представления, конвертеры, диалоги
├── scripts/          Проверки: тесты экспорта, вёрстки, прокрутки, запуска
└── .github/workflows/Сборка и выпуск релизов
```

### Известные ограничения

- Только Windows. Версия для macOS в планах: язык и предметная область к этому готовы,
  переносится только слой интерфейса.
- Проект из 29 книг открывается примерно 1,5 секунды. Причина найдена и измерена
  (интерфейс строит ячейки всех книг сразу), исправление подготовлено — см. историю
  изменений. На заказы обычного размера это незаметно.
- В режиме уникальных папок количество файлов в папках может различаться: для корректной
  сборки books должны быть одинаковой длины. Программа предупреждает об этом.

### Для разработчиков

- [BUILD.md](BUILD.md) — как собрать, как проверить, как выпустить релиз
- [BRANCHING.md](BRANCHING.md) — как мы работаем с ветками и релизами
- Заметки к каждому релизу: [`docs/release-notes/`](docs/release-notes)

---

## English

### What it is

A Windows desktop application that prepares a photo book print run for upload to a
printing site. The operator lays photos out on a cover and spreads, and the program copies
them into a target folder under the naming scheme the site requires (`001-00.jpg`,
`001-01.jpg`, …). **Originals are never modified or deleted.**

### Features

- **Unique Folders mode** — each folder is one book; cover detected automatically or chosen
  manually; spread order can be rearranged.
- **Combined mode** — N books × M spreads in one run; a photo can be applied to a single
  book, to all books at once, or to a chosen set. Shared covers and spreads make large runs
  fast to assemble.
- **Export** — renames and copies into a new folder per export; optional per-book
  subfolders; run-wide naming in combined mode; the project is saved before export.
- **Projects** — saved locally, resumable, with progress per book.
- **Auto-update** — checks GitHub Releases on start and offers the newer version.

### Requirements

Windows 10 (1809+) or Windows 11, x64. No separate .NET installation is needed: the
published build is self-contained. Windows 7 / 8.1 are not supported.

### Install

Download the [latest release](https://github.com/AlexeyShumeyko/FabrikaBookBuilder/releases/latest),
unpack `BookBuilder-Studio-Setup.zip` and run the installer. Administrator rights are
requested once.

### Stack

.NET 8 · WPF · MVVM · SixLabors.ImageSharp · System.IO.Abstractions · Octokit ·
Inno Setup · GitHub Actions (`net8.0-windows`, self-contained, single file)

### Known limitations

Windows only (a macOS version is planned); a 29-book project takes about 1.5 s to open —
cause measured, fix prepared; unique-folder mode requires equal file counts per folder and
warns when they differ.
