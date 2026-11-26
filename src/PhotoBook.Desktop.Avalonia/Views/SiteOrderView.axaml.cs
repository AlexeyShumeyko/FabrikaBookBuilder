using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        /// <summary>Folds a group's books away, or brings them back.</summary>
        private void OnToggleGroup(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is SiteOrderGroup group)
                group.ToggleCollapse();
        }

        /// <summary>
        /// Chooses the group whose card is being read.
        /// </summary>
        /// <remarks>
        /// The whole head is the target, as it is in the prototype, so the card is chosen by the
        /// part of it a customer already points at. The switch and the chevron are answers of
        /// their own and are left out: taking a group out of the run must not also choose it.
        /// </remarks>
        private void OnSelectGroup(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;

            if (e.Source is Visual source &&
                (source is Button || source is CheckBox ||
                 source.GetVisualAncestors().Any(control => control is Button || control is CheckBox)))
            {
                return;
            }

            if ((sender as Control)?.DataContext is SiteOrderGroup group)
                (DataContext as SiteOrderViewModel)?.SelectGroup(group);
        }

        /// <summary>
        /// Takes the eye to the first group that needs an answer.
        /// </summary>
        /// <remarks>
        /// The plate over the page names the books and cannot show them: four groups of cards
        /// are taller than a window, and the one that is wrong can be the last of them. The
        /// button therefore scrolls rather than repeats the sentence, and it scrolls to the
        /// card the page itself considers first - the same one the flag is read from - so the
        /// two can never disagree.
        /// </remarks>
        private void OnShowProblem(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SiteOrderViewModel page || page.FirstProblemGroup is not { } target)
                return;

            var card = this.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Name == "BooksCard" && ReferenceEquals(border.DataContext, target));

            card?.BringIntoView();
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
