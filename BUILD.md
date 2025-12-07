# Инструкции по сборке

## Требования

- .NET 8.0 SDK
- Visual Studio 2022 или JetBrains Rider (опционально)

## Сборка из командной строки

```bash
# Восстановление зависимостей
dotnet restore

# Сборка проекта
dotnet build --configuration Release

# Запуск приложения
dotnet run --project PhotoBookRenamer/PhotoBookRenamer.csproj
```

## Сборка в Visual Studio

1. Откройте `PhotoBookRenamer.sln` в Visual Studio 2022
2. Выберите конфигурацию Release
3. Нажмите Build > Build Solution (Ctrl+Shift+B)
4. Запустите проект (F5)

## Создание установщика

Для создания установщика рекомендуется использовать:

- **WiX Toolset** - для создания MSI установщика
- **Inno Setup** - для создания EXE установщика
- **Squirrel** - для создания установщика с автообновлением

### Пример с WiX

```xml
<!-- WiX проект для создания MSI -->
<Wix xmlns="http://schemas.microsoft.com/wix/2006/wi">
  <Product Id="*" Name="PhotoBook Renamer" Language="1033" Version="1.0.0" 
           Manufacturer="Your Company" UpgradeCode="YOUR-GUID-HERE">
    <Package InstallerVersion="200" Compressed="yes" InstallScope="perMachine" />
    <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
    <MediaTemplate />
    
    <Feature Id="ProductFeature" Title="PhotoBook Renamer" Level="1">
      <ComponentRef Id="ApplicationFiles" />
    </Feature>
  </Product>
</Wix>
```

## Публикация

```bash
# Создание самодостаточного приложения
dotnet publish -c Release -r win-x64 --self-contained true

# Создание зависимого от .NET приложения (меньший размер)
dotnet publish -c Release -r win-x64 --self-contained false
```

## Автообновление

Приложение включает сервис проверки обновлений через GitHub Releases.
Настройте в `UpdateService.cs`:

```csharp
private const string Owner = "YourGitHubUsername";
private const string Repo = "PhotoBookRenamer";
```

## Заметки

- Приложение использует System.Windows.Forms для FolderBrowserDialog
- Миниатюры сохраняются в `%TEMP%\PhotoBookRenamer\Thumbnails`
- Все операции с файлами выполняются асинхронно
- Оригинальные файлы никогда не изменяются





