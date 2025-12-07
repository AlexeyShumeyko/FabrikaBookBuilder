using System;
using System.Windows;
using System.Windows.Input;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public partial class PageNumberDialog : Window
    {
        public int? SelectedPageNumber { get; private set; }
        public int MaxPageNumber { get; set; } = 1;

        public PageNumberDialog(int maxPageNumber)
        {
            InitializeComponent();
            MaxPageNumber = maxPageNumber;
            PageNumberTextBox.Focus();
            PageNumberTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PageNumberTextBox.Text, out int pageNumber))
            {
                if (pageNumber >= 1 && pageNumber <= MaxPageNumber)
                {
                    SelectedPageNumber = pageNumber;
                    DialogResult = true;
                }
                else
                {
                    MessageBox.Show(
                        $"Номер разворота должен быть от 1 до {MaxPageNumber}.",
                        "Ошибка",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    PageNumberTextBox.Focus();
                    PageNumberTextBox.SelectAll();
                }
            }
            else
            {
                MessageBox.Show(
                    "Пожалуйста, введите корректный номер разворота.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                PageNumberTextBox.Focus();
                PageNumberTextBox.SelectAll();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void PageNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
            }
        }
    }
}






