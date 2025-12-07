using System;
using System.Diagnostics;
using System.Windows;

namespace PhotoBookRenamer.Utils
{
    public partial class ExportSuccessDialog : Window
    {
        public bool GoToFolder { get; private set; }

        public ExportSuccessDialog(string folderPath)
        {
            InitializeComponent();
            PathTextBlock.Text = folderPath;
        }

        private void GoToFolderButton_Click(object sender, RoutedEventArgs e)
        {
            GoToFolder = true;
            DialogResult = true;
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            GoToFolder = false;
            DialogResult = true;
        }
    }
}












