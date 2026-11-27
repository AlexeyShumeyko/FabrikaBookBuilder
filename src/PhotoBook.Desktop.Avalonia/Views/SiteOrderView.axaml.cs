using System;
using System.Collections.Generic;
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
        /// The whole card is the target - the head, the rows of books and the plate under them -
        /// so a group is chosen by a press anywhere on it rather than only by the narrow strip
        /// of its head. The switch, the chevrons and the tools on a row are answers of their
        /// own and are left out: taking a group out of the run must not also choose it.
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

        // The prototype's sprite, point for point: the four arrows of «move», the folder with a
        // plus, and the cross of the red line at the menu's foot.
        private const string MoveIcon =
            "M12 3v18M3 12h18M9 6l3-3 3 3M9 18l3 3 3-3M6 9l-3 3 3 3M18 9l3 3-3 3";

        private const string NewGroupIcon =
            "M3.5 8A2.5 2.5 0 0 1 6 5.5h3.6l1.8 2.2H18A2.5 2.5 0 0 1 20.5 10v3" +
            "M3.5 8v9.2A2.5 2.5 0 0 0 6 19.7h6" +
            "M17.5 14v6M14.5 17h6";

        private const string RemoveIcon = "M6 6l12 12M18 6L6 18";

        /// <summary>
        /// Opens the book's own menu: the groups it can move to, a group of its own, and the
        /// line that takes it out of the run.
        /// </summary>
        /// <remarks>
        /// The menu is the prototype's, built from the page's groups at the moment of the click
        /// rather than bound: the list changes after every move, and a bound menu would show the
        /// groups the book has already left. Only one menu is open at a time - a second set of
        /// dots closes the first, and so does the same set of dots pressed again.
        /// </remarks>
        private void OnMoveBook(object? sender, RoutedEventArgs e)
        {
            if (sender is not Control control ||
                control.DataContext is not OrderBookRow row ||
                DataContext is not SiteOrderViewModel page)
            {
                return;
            }

            var wasOpen = row.IsMenuOpen;

            foreach (var other in page.Groups.SelectMany(group => group.Books))
                other.CloseMenu();

            if (wasOpen)
                return;

            var current = page.GroupOf(row);
            var entries = new List<BookMenuEntry>();

            foreach (var group in page.Groups.Where(group => !ReferenceEquals(group, current)))
            {
                var target = group;
                entries.Add(BookMenuEntry.Item(
                    "В группу " + target.Number,
                    target.SelectedFormat?.DisplayText ?? "Формат не выбран",
                    MoveIcon,
                    () => page.MoveBook(row, target)));
            }

            entries.Add(BookMenuEntry.Item(
                "В новую группу",
                "Создать группу под этот формат",
                NewGroupIcon,
                () => page.MoveBook(row, null)));

            // The book stays in the project and keeps its row; it is only taken out of the run,
            // so the last line is red and says so.
            entries.Add(BookMenuEntry.Separator());
            entries.Add(BookMenuEntry.Item(
                row.IsExcluded ? "Вернуть книгу в загрузку" : "Исключить книгу из загрузки",
                row.IsExcluded ? "Книга снова будет отправлена" : "Книга останется в проекте",
                RemoveIcon,
                row.ToggleExcluded,
                danger: true));

            row.ShowMenu(entries);
        }

        /// <summary>Runs the line of the book's menu that was pressed.</summary>
        private void OnMenuEntry(object? sender, RoutedEventArgs e)
        {
            if ((sender as Control)?.DataContext is not BookMenuEntry entry || entry.Run is null)
                return;

            if (DataContext is SiteOrderViewModel page)
            {
                foreach (var row in page.Groups.SelectMany(group => group.Books))
                    row.CloseMenu();
            }

            entry.Run();
        }
    }
}
