using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Shell.Views
{
    /// <summary>
    /// The order page. Going back is the view's own job because the page does not know which
    /// project it was opened from - the navigator does. The price is not a command of this view:
    /// the page asks for it itself once the answers are complete.
    /// </summary>
    public partial class SiteOrderView : UserControl
    {
        public SiteOrderView() => InitializeComponent();

        private void OnBack(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteOrderViewModel)?.Back();

        private void OnUpload(object? sender, RoutedEventArgs e) =>
            (DataContext as SiteOrderViewModel)?.Upload();

        /// <summary>Opens or closes one book's files, from the chevron on its row.</summary>
        private void OnToggleBook(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is OrderBookRow row)
                row.IsExpanded = !row.IsExpanded;
        }

        /// <summary>
        /// Offers the groups this book can move to, and a group of its own.
        /// </summary>
        /// <remarks>
        /// The menu is built from the page's groups at the moment of the click rather than
        /// bound: the list changes after every move, and a bound menu would show the groups
        /// the book has already left.
        /// </remarks>
        private void OnMoveBook(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control control ||
                control.DataContext is not OrderBookRow row ||
                DataContext is not SiteOrderViewModel page)
            {
                return;
            }

            var current = page.GroupOf(row);
            var flyout = new MenuFlyout();

            foreach (var group in page.Groups.Where(group => !ReferenceEquals(group, current)))
            {
                var target = group;
                flyout.Items.Add(Item(
                    "В группу " + target.Number + " — " + target.SummaryText,
                    () => page.MoveBook(row, target)));
            }

            if (flyout.Items.Count > 0)
                flyout.Items.Add(new Separator());

            flyout.Items.Add(Item("В новую группу", () => page.MoveBook(row, null)));

            // The book stays in the group and keeps its row; it is only taken out of the run.
            flyout.Items.Add(new Separator());
            flyout.Items.Add(Item(
                row.IsExcluded ? "Вернуть книгу в загрузку" : "Исключить книгу из загрузки",
                row.ToggleExcluded));

            flyout.ShowAt(control);
        }

        private static MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }
    }
}
