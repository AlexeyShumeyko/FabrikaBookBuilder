using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace PhotoBookRenamer.Utils
{
    public class FilePathToThumbnailConverter : IValueConverter
    {
        // КРИТИЧЕСКИ ВАЖНО: Создаем уникальный хэш для полного пути файла
        // Это предотвращает конфликты при одинаковых именах файлов в разных папках
        private string GetFilePathHash(string filePath)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(filePath);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16);
            }
        }
        
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string filePath && !string.IsNullOrEmpty(filePath))
            {
                var thumbDir = Path.Combine(Path.GetTempPath(), "PhotoBookRenamer", "Thumbnails");
                // КРИТИЧЕСКИ ВАЖНО: Используем хэш полного пути вместо имени файла
                var filePathHash = GetFilePathHash(filePath);
                var thumbName = $"{filePathHash}_thumb.jpg";
                var thumbPath = Path.Combine(thumbDir, thumbName);

                if (File.Exists(thumbPath))
                {
                    try
                    {
                        // КРИТИЧЕСКИ ВАЖНО: Используем CacheOption.OnLoad чтобы файл сразу загружался в память
                        // и не блокировался для других процессов
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.UriSource = new Uri(thumbPath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze(); // Делаем неизменяемым для потокобезопасности
                        
                        return bitmap;
                    }
                    catch
                    {
                        // Если не удалось загрузить миниатюру, используем оригинал
                    }
                }
                
                // Возвращаем оригинальный путь, если миниатюра не найдена или не загрузилась
                if (File.Exists(filePath))
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 200; // Ограничиваем размер для производительности
                        bitmap.DecodePixelHeight = 200;
                        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
                        return bitmap;
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}





