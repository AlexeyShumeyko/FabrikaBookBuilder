using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using PhotoBookRenamer.ViewModels;

namespace PhotoBookRenamer.Views
{
    public partial class ProjectListView : UserControl
    {
        public ProjectListView(ProjectListViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void AppLogoImage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Image image)
            {
                try
                {
                    // Пытаемся загрузить иконку из разных мест
                    var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                    
                    if (File.Exists(iconPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        image.Source = bitmap;
                    }
                    else
                    {
                        // Пытаемся загрузить как ресурс
                        try
                        {
                            var uri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
                            var bitmap = new BitmapImage(uri);
                            bitmap.Freeze();
                            image.Source = bitmap;
                        }
                        catch
                        {
                            // Если не удалось загрузить, оставляем пустым
                        }
                    }
                }
                catch
                {
                    // Игнорируем ошибки загрузки
                }
            }
        }
    }
}










