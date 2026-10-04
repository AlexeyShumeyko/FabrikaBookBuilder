using System.Windows;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public partial class ExportSuccessDialog : Window
    {
        /// <summary>True when the user asked to open the output folder in Explorer.</summary>
        public bool GoToFolder { get; private set; }

        public ExportSuccessDialog(string folderPath)
        {
            InitializeComponent();
            PathTextBlock.Text = folderPath;
            SummaryText.Text = BuildSummary(folderPath);
        }

        private static string BuildSummary(string folderPath)
        {
            int fileCount = 0;
            try
            {
                if (System.IO.Directory.Exists(folderPath))
                    fileCount = System.IO.Directory.GetFiles(folderPath, "*.jpg").Length;
            }
            catch
            {
                // A count is a nicety; never let it break the dialog.
            }

            return fileCount > 0
                ? $"Готово {fileCount} файлов. Папка готова к загрузке на печать."
                : "Все файлы сформированы и готовы к отправке на печать.";
        }

        private void OnGoToFolderClick(object sender, RoutedEventArgs e)
        {
            GoToFolder = true;
            DialogResult = true;
        }

        private void OnContinueClick(object sender, RoutedEventArgs e)
        {
            GoToFolder = false;
            DialogResult = true;
        }
    }
}
