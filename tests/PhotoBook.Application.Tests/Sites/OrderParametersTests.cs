using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Presentation.ViewModels;

namespace PhotoBook.Application.Tests.Sites
{
    /// <summary>
    /// The rows of the format parameters are updated in place while the window arranges them.
    /// A row that announced an unchanged answer sent the window's own write-back through its
    /// setter, which rebuilt the block and announced again - the page died of it once.
    /// </summary>
    public class OrderParametersTests
    {
        private const string Option = "Плотность разворотов";

        [Fact]
        public void TheSameAnswerArrivingAgainChangesNothing()
        {
            var row = Row(out var changes);

            row.Selected = row.Elements[0];
            var after = changes();

            row.Selected = new ParameterElement(row.Elements[0].Element);

            Assert.Equal(after, changes());
            Assert.Equal(row.Elements[0], row.Selected);
        }

        [Fact]
        public void UpdatingTheRowKeepsAnUnchangedAnswerQuiet()
        {
            var row = Row(out var changes);
            row.Selected = row.Elements[0];
            var after = changes();

            var raised = 0;
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(OrderParameterRow.Selected))
                    raised++;
            };

            row.Show(new[] { new ParameterElement(row.Elements[0].Element) }, row.Elements[0]);

            Assert.Equal(0, raised);
            Assert.Equal(after, changes());
            Assert.Single(row.Elements);
        }

        [Fact]
        public void TheSameChoicesKeepTheListTheWindowAlreadyShows()
        {
            // A replaced list makes the window drop its own selection and write "no answer"
            // back, which reads as a new answer and rebuilds the block - forever.
            var row = Row(out _);
            var shown = row.Elements;

            row.Show(new[]
            {
                new ParameterElement(new OptionElement(417892, "Картон")),
                new ParameterElement(new OptionElement(417891, "Бумага")),
            }, null);

            Assert.Same(shown, row.Elements);
        }

        [Fact]
        public void ABetterAnswerIsAnnouncedOnce()
        {
            var row = Row(out var changes);

            row.Selected = row.Elements[1];

            Assert.Equal(1, changes());
            Assert.Equal(row.Elements[1], row.Selected);
        }

        private static OrderParameterRow Row(out Func<int> changes)
        {
            var count = 0;

            var row = new OrderParameterRow(
                new ProductOption(
                    65075,
                    Option,
                    true,
                    null,
                    Array.Empty<string>(),
                    null,
                    Array.Empty<string>(),
                    new[]
                    {
                        new OptionElement(417892, "Картон"),
                        new OptionElement(417891, "Бумага"),
                    }),
                new[]
                {
                    new ParameterElement(new OptionElement(417892, "Картон")),
                    new ParameterElement(new OptionElement(417891, "Бумага")),
                },
                null,
                () => count++);

            changes = () => count;
            return row;
        }
    }
}
