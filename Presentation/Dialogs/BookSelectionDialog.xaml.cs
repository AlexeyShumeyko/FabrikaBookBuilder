using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PhotoBookRenamer.Domain;

namespace PhotoBookRenamer.Presentation.Dialogs
{
    public class BookSelectionItem
    {
        public Book Book { get; set; } = null!;
        public bool IsSelected { get; set; }
        public string Name => Book.Name ?? "Без названия";
    }

    public partial class BookSelectionDialog : Window
    {
        public List<Book> SelectedBooks { get; private set; } = new();

        public BookSelectionDialog(IEnumerable<Book> books)
        {
            InitializeComponent();
            
            var items = books.Select(b => new BookSelectionItem { Book = b, IsSelected = false }).ToList();
            BooksListBox.ItemsSource = items;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var items = BooksListBox.ItemsSource as IEnumerable<BookSelectionItem>;
            if (items != null)
            {
                SelectedBooks = items.Where(i => i.IsSelected).Select(i => i.Book).ToList();
            }
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}













