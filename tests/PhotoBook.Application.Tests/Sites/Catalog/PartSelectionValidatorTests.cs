using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Catalog
{
    /// <summary>
    /// The rules are pinned to the layflat 21x30 as the site states them: density is required,
    /// an interlayer exists only for cardboard, and one-millimetre cardboard runs to twelve
    /// spreads while no interlayer runs to twenty.
    /// </summary>
    public class PartSelectionValidatorTests
    {
        private const long Density = 65075;
        private const long Cardboard = 417892;
        private const long Paper = 417891;

        private const long PaperInterlayer = 65739;
        private const long PaperNone = 420270;

        private const long CardboardInterlayer = 65740;
        private const long None = 420271;
        private const long CardboardOneMillimetre = 420272;

        private readonly PartSelectionValidator _validator = new();

        private static ProductPart SpreadsPart(bool withQuantity = true)
        {
            var density = new ProductOption(
                Density,
                "Плотность разворотов",
                true,
                null,
                new List<string>(),
                null,
                new List<string>(),
                new List<OptionElement>
                {
                    new(Cardboard, "Картон"),
                    new(Paper, "Бумага")
                });

            // Paper and cardboard each have their own interlayer option on the site, with
            // different quantity ranges, which is why choosing paper must not be refused for
            // "no interlayer" - it is a different option entirely.
            var paperInterlayer = new ProductOption(
                PaperInterlayer,
                "Прослойка разворотов",
                true,
                "65075",
                new List<string> { "417891" },
                "65075",
                new List<string> { "417892" },
                new List<OptionElement>
                {
                    new(PaperNone, "Без прослойки")
                });

            var cardboardInterlayer = new ProductOption(
                CardboardInterlayer,
                "Прослойка разворотов",
                true,
                "65075",
                new List<string> { "417892" },
                "65075",
                new List<string> { "417891" },
                new List<OptionElement>
                {
                    new(None, "Без прослойки"),
                    new(CardboardOneMillimetre, "Картон 1 мм")
                });

            var quantity = withQuantity
                ? new ProductQuantitySettings(
                    "Количество разворотов",
                    QuantityUnit.Spreads,
                    null,
                    false,
                    new List<QuantityRule>
                    {
                        new("65739", new List<string> { "420270" }, new QuantityRange(10, 30, 1)),
                        new("65740", new List<string> { "420271" }, new QuantityRange(2, 20, 1)),
                        new("65740", new List<string> { "420272" }, new QuantityRange(2, 12, 1))
                    })
                : new ProductQuantitySettings(
                    string.Empty,
                    QuantityUnit.Unknown,
                    null,
                    false,
                    new List<QuantityRule>());

            return new ProductPart(
                94,
                "Разворот",
                new List<ProductOption> { density, paperInterlayer, cardboardInterlayer },
                quantity);
        }

        private static PartSelection Choose(int quantity, params (long Option, long? Element)[] choices)
        {
            var elements = new Dictionary<long, long>();
            foreach (var (option, element) in choices)
            {
                if (element is { } chosen)
                    elements[option] = chosen;
            }

            return new PartSelection(elements, quantity);
        }

        [Fact]
        public void AcceptsCardboardWithNoInterlayerAtTenSpreads()
        {
            var check = _validator.Check(SpreadsPart(), Choose(10, (Density, Cardboard), (CardboardInterlayer, None)));

            Assert.True(check.IsValid, string.Join(" ", check.Problems.Select(problem => problem.Message)));
            Assert.Empty(check.Problems);
        }

        [Fact]
        public void AcceptsPaperWithItsOwnInterlayer()
        {
            // Paper uses the other interlayer option, and its own range starts at ten spreads.
            var check = _validator.Check(SpreadsPart(), Choose(10, (Density, Paper), (PaperInterlayer, PaperNone)));

            Assert.True(check.IsValid, string.Join(" ", check.Problems.Select(problem => problem.Message)));
        }

        [Fact]
        public void RefusesPaperAtNineSpreadsBecauseItsRangeStartsAtTen()
        {
            var check = _validator.Check(SpreadsPart(), Choose(9, (Density, Paper), (PaperInterlayer, PaperNone)));

            var quantity = Assert.Single(check.Problems, problem => problem.Kind == PartProblemKind.QuantityNotAllowed);
            Assert.Equal("Количество разворотов: от 10 до 30.", quantity.Message);
        }

        [Fact]
        public void AsksForARequiredOptionThatWasNotChosen()
        {
            var check = _validator.Check(SpreadsPart(), Choose(10));

            var missing = Assert.Single(check.Problems, problem => problem.Kind == PartProblemKind.MissingRequired);
            Assert.Equal("Выберите «Плотность разворотов».", missing.Message);
        }

        [Fact]
        public void AsksForTheInterlayerWhenCardboardIsChosen()
        {
            var check = _validator.Check(SpreadsPart(), Choose(10, (Density, Cardboard)));

            Assert.Contains(check.Problems, problem =>
                problem.Kind == PartProblemKind.MissingRequired &&
                problem.Message.Contains("Прослойка разворотов"));
        }

        [Fact]
        public void RefusesTheCardboardInterlayerOnPaperNamingTheDependency()
        {
            // The option is both dependent on cardboard and excluded for paper; the dependency
            // is reported first because it names what the choice needs, not what it conflicts with.
            var check = _validator.Check(SpreadsPart(), Choose(10, (Density, Paper), (CardboardInterlayer, None)));

            var unavailable = Assert.Single(check.Problems, problem => problem.Kind == PartProblemKind.Unavailable);
            Assert.Contains("только при", unavailable.Message);
            Assert.Contains("Картон", unavailable.Message);
        }

        [Fact]
        public void RefusesAQuantityBeyondTheChosenElementsRange()
        {
            var check = _validator.Check(SpreadsPart(), Choose(13, (Density, Cardboard), (CardboardInterlayer, CardboardOneMillimetre)));

            var quantity = Assert.Single(check.Problems, problem => problem.Kind == PartProblemKind.QuantityNotAllowed);
            Assert.Equal("Количество разворотов: от 2 до 12.", quantity.Message);
        }

        [Fact]
        public void RefusesAQuantityBeyondTheLongerRange()
        {
            var check = _validator.Check(SpreadsPart(), Choose(21, (Density, Cardboard), (CardboardInterlayer, None)));

            var quantity = Assert.Single(check.Problems, problem => problem.Kind == PartProblemKind.QuantityNotAllowed);
            Assert.Equal("Количество разворотов: от 2 до 20.", quantity.Message);
        }

        [Fact]
        public void RefusesAnElementTheOptionDoesNotOffer()
        {
            var check = _validator.Check(SpreadsPart(), Choose(10, (Density, 999999)));

            Assert.Contains(check.Problems, problem =>
                problem.Kind == PartProblemKind.UnknownElement &&
                problem.Message.Contains("Плотность разворотов"));
        }

        [Fact]
        public void SaysTheCombinationHasNoQuantityWhenNoRuleGovernsIt()
        {
            // Paper with an interlayer is refused, and no quantity rule names paper, so the
            // quantity cannot be checked either - the answer says so instead of guessing a range.
            var check = _validator.Check(SpreadsPart(), Choose(10, (Density, Paper)));

            Assert.Contains(check.Problems, problem =>
                problem.Kind == PartProblemKind.QuantityNotAllowed &&
                problem.Message.Contains("не предлагает"));
        }

        [Fact]
        public void LeavesAPartWithoutQuantitySettingsAlone()
        {
            var check = _validator.Check(
                SpreadsPart(withQuantity: false),
                Choose(0, (Density, Cardboard), (CardboardInterlayer, None)));

            Assert.True(check.IsValid, string.Join(" ", check.Problems.Select(problem => problem.Message)));
        }
    }
}
