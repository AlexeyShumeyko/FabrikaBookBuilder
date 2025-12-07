<div align="center">

# 📚 PhotoBook Renamer

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-239120?style=for-the-badge&logo=c-sharp&logoColor=white)
![WPF](https://img.shields.io/badge/WPF-0078D4?style=for-the-badge&logo=windows&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green?style=for-the-badge)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?style=for-the-badge&logo=windows&logoColor=white)

**Professional desktop application for renaming photo book images with support for multiple working modes**

[English](#english) • [Русский](#русский)

</div>

---

## English

### 🎯 Overview

PhotoBook Renamer is a professional Windows desktop application designed for organizing and renaming photo book images. Built with modern .NET 8.0 and WPF, it follows Clean Architecture principles and implements MVVM pattern for maintainable, scalable code.

### ✨ Features

#### 📁 Unique Folders Mode
- Select multiple folders (each folder = one book)
- Automatic cover detection by highest resolution
- Manual cover selection
- File count validation in folders
- Export with renaming in `KK-FF.jpg` format

#### 🔄 Combined Mode
- Load JPG files via drag & drop
- Create book structure with customizable page count
- Drag & drop files to cover and page slots
- Apply file to one book, all books, or selected books
- Pre-export validation

### 🏗️ Architecture

The project follows **Clean Architecture** principles with clear separation of concerns:

```
PhotoBookRenamer/
├── Domain/              # Core business entities and value objects
├── Application/          # Application-specific business logic
├── Infrastructure/       # External concerns (file system, image processing, logging)
└── Presentation/        # UI layer (Views, ViewModels, Converters, Dialogs)
```

### 🛠️ Technologies

- **.NET 8.0** - Modern cross-platform framework
- **WPF** - Rich desktop UI framework
- **MVVM** - Architectural pattern for UI separation
- **ImageSharp** - High-performance image processing
- **Dependency Injection** - Loose coupling and testability
- **CommunityToolkit.Mvvm** - MVVM helpers and commands
- **ModernWpfUI** - Modern Windows UI components

### 📦 Dependencies

- `CommunityToolkit.Mvvm` (8.2.2)
- `Microsoft.Extensions.DependencyInjection` (8.0.0)
- `Microsoft.Extensions.Logging` (8.0.0)
- `ModernWpfUI` (0.9.6)
- `SixLabors.ImageSharp` (3.1.12)
- `System.IO.Abstractions` (19.2.69)
- `Octokit` (13.0.1)

### ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+O` | Open files |
| `Ctrl+Shift+O` | Open folders |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Del` | Delete from project |
| `Ctrl+S` | Export |
| `Ctrl+Shift+S` | Export with folder selection |
| `Ctrl+E` | Reset project |
| `Ctrl+1` | Switch to Unique Folders mode |
| `Ctrl+2` | Switch to Combined mode |

### 📝 Naming Format

Files are renamed to format: `KK-FF.jpg`

- `KK` - Book index (01-99)
- `FF` - File index (00-99)
  - `00` - Cover
  - `01+` - Pages

### 🔒 Security

- Application **never modifies** original files
- Only **copies** of files are created
- All operations are asynchronous
- Complete error handling and validation

### ⚡ Performance

- Asynchronous image loading
- Thumbnail caching
- UI virtualization for large lists
- Multi-threaded processing

### 📋 Requirements

- Windows 10/11
- .NET 8.0 Runtime

### 🚀 Building

```bash
# Restore dependencies
dotnet restore

# Build project
dotnet build --configuration Release

# Run application
dotnet run
```

### 📄 License

MIT License

---

## Русский

### 🎯 Обзор

PhotoBook Renamer — профессиональное десктопное приложение для Windows, предназначенное для организации и переименования изображений фотоальбомов. Построено на современном .NET 8.0 и WPF, следует принципам Clean Architecture и реализует паттерн MVVM для поддерживаемого и масштабируемого кода.

### ✨ Возможности

#### 📁 Режим уникальных папок
- Выбор нескольких папок (каждая папка = одна книга)
- Автоматическое определение обложки по наибольшему разрешению
- Ручной выбор обложки
- Валидация количества файлов в папках
- Экспорт с переименованием в формате `KK-FF.jpg`

#### 🔄 Комбинированный режим
- Загрузка JPG файлов через drag & drop
- Создание структуры книг с настраиваемым количеством страниц
- Перетаскивание файлов на слоты обложек и страниц
- Применение файла к одной книге, всем книгам или выбранным
- Валидация перед экспортом

### 🏗️ Архитектура

Проект следует принципам **Clean Architecture** с четким разделением ответственности:

```
PhotoBookRenamer/
├── Domain/              # Основные бизнес-сущности и объекты-значения
├── Application/         # Бизнес-логика приложения
├── Infrastructure/       # Внешние зависимости (файловая система, обработка изображений, логирование)
└── Presentation/        # Слой UI (Views, ViewModels, Converters, Dialogs)
```

### 🛠️ Технологии

- **.NET 8.0** - Современный кроссплатформенный фреймворк
- **WPF** - Богатый фреймворк для десктопного UI
- **MVVM** - Архитектурный паттерн для разделения UI
- **ImageSharp** - Высокопроизводительная обработка изображений
- **Dependency Injection** - Слабая связанность и тестируемость
- **CommunityToolkit.Mvvm** - Вспомогательные классы и команды MVVM
- **ModernWpfUI** - Современные компоненты Windows UI

### 📦 Зависимости

- `CommunityToolkit.Mvvm` (8.2.2)
- `Microsoft.Extensions.DependencyInjection` (8.0.0)
- `Microsoft.Extensions.Logging` (8.0.0)
- `ModernWpfUI` (0.9.6)
- `SixLabors.ImageSharp` (3.1.12)
- `System.IO.Abstractions` (19.2.69)
- `Octokit` (13.0.1)

### ⌨️ Горячие клавиши

| Сочетание | Действие |
|-----------|----------|
| `Ctrl+O` | Открыть файлы |
| `Ctrl+Shift+O` | Открыть папки |
| `Ctrl+Z` | Отменить |
| `Ctrl+Y` | Повторить |
| `Del` | Удалить из проекта |
| `Ctrl+S` | Экспорт |
| `Ctrl+Shift+S` | Экспорт с выбором папки |
| `Ctrl+E` | Сброс проекта |
| `Ctrl+1` | Переключиться на режим уникальных папок |
| `Ctrl+2` | Переключиться на комбинированный режим |

### 📝 Формат именования

Файлы переименовываются в формат: `KK-FF.jpg`

- `KK` - Индекс книги (01-99)
- `FF` - Индекс файла (00-99)
  - `00` - Обложка
  - `01+` - Страницы

### 🔒 Безопасность

- Приложение **никогда не изменяет** оригинальные файлы
- Создаются только **копии** файлов
- Все операции выполняются асинхронно
- Полная обработка ошибок и валидация

### ⚡ Производительность

- Асинхронная загрузка изображений
- Кэширование миниатюр
- Виртуализация UI для больших списков
- Многопоточная обработка

### 📋 Требования

- Windows 10/11
- .NET 8.0 Runtime

### 🚀 Сборка

```bash
# Восстановление зависимостей
dotnet restore

# Сборка проекта
dotnet build --configuration Release

# Запуск приложения
dotnet run
```

### 📄 Лицензия

MIT License

---

<div align="center">

**Made with ❤️ using .NET and WPF**

[⬆ Back to top](#-photobook-renamer)

</div>
