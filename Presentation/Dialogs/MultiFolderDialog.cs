using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public static class MultiFolderDialog
    {
        public static List<string>? SelectFolders()
        {
            return System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var folders = new List<string>();
                
                while (true)
                {
                    using var dialog = new FolderBrowserDialog
                    {
                        Description = folders.Count == 0 
                            ? "Выберите первую папку с фотографиями" 
                            : $"Выбрано папок: {folders.Count}. Выберите следующую (Отмена для завершения)",
                        UseDescriptionForTitle = true
                    };

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        if (!folders.Contains(dialog.SelectedPath))
                        {
                            folders.Add(dialog.SelectedPath);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(
                                "Эта папка уже выбрана!",
                                "Предупреждение",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                return folders.Count > 0 ? folders : null;
            });
        }
    }
}

