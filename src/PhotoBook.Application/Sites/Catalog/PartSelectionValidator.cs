using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>
    /// Applies the rules the product page states: what is required, what is available with what,
    /// and which quantities the site prices for the chosen elements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both dependency fields are honoured, because the site states availability twice: an
    /// option depends on another option's element ("available when density is cardboard") and
    /// is excluded by another ("not when density is paper"). They look symmetrical and are not:
    /// on a layflat the interlayer is available only for cardboard, while the option disappears
    /// for paper - and a required option that is not available must not be reported as missing.
    /// </para>
    /// <para>
    /// A part with no quantity settings at all - the photo panel products count files, not
    /// spreads - is left alone rather than forced through a range it never had.
    /// </para>
    /// <para>
    /// Requiredness is checked here and not by the site: the price endpoint answers with a
    /// price even when a required option is missing, as if it cost nothing (measured on the
    /// live endpoint on 2026-11-10). The site's page never sends such a set, because it picks
    /// a default for every option, so refusing it here matches what the page does rather than
    /// what the price endpoint tolerates.
    /// </para>
    /// </remarks>
    public sealed class PartSelectionValidator : IPartSelectionValidator
    {
        public PartCheck Check(ProductPart part, PartSelection selection)
        {
            if (part is null)
                throw new ArgumentNullException(nameof(part));
            if (selection is null)
                throw new ArgumentNullException(nameof(selection));

            var problems = new List<PartProblem>();

            foreach (var option in part.Options)
            {
                var chosen = selection.ElementOf(option.Id);
                var unavailable = UnavailableReason(part, option, selection);

                if (chosen is null)
                {
                    if (option.IsRequired && unavailable is null)
                    {
                        problems.Add(new PartProblem(
                            PartProblemKind.MissingRequired,
                            "Выберите «" + option.Title + "»."));
                    }

                    continue;
                }

                if (!Offers(option, chosen.Value))
                {
                    problems.Add(new PartProblem(
                        PartProblemKind.UnknownElement,
                        "У «" + option.Title + "» нет такого варианта."));
                    continue;
                }

                if (unavailable is not null)
                    problems.Add(new PartProblem(PartProblemKind.Unavailable, unavailable));
            }

            CheckQuantity(part, selection, problems);

            return problems.Count == 0 ? PartCheck.Ok() : PartCheck.Invalid(problems);
        }

        /// <summary>Why an option cannot be chosen with this selection, or null when it can.</summary>
        private static string? UnavailableReason(
            ProductPart part,
            ProductOption option,
            PartSelection selection)
        {
            if (DependencyId(option.DependsOnOptionId) is { } dependsOn)
            {
                var chosen = selection.ElementOf(dependsOn);
                if (chosen is null || !Contains(option.DependsOnElementIds, chosen.Value))
                {
                    return "«" + option.Title + "» можно выбрать только при «" +
                           TitleOf(part, dependsOn) + "»: " +
                           NamesOf(part, dependsOn, option.DependsOnElementIds) + ".";
                }
            }

            if (DependencyId(option.ExcludedOptionId) is { } excludedBy)
            {
                var chosen = selection.ElementOf(excludedBy);
                if (chosen is not null && Contains(option.ExcludedElementIds, chosen.Value))
                {
                    return "«" + option.Title + "» не сочетается с «" +
                           TitleOf(part, excludedBy) + "»: " +
                           NamesOf(part, excludedBy, option.ExcludedElementIds) + ".";
                }
            }

            return null;
        }

        private static void CheckQuantity(
            ProductPart part,
            PartSelection selection,
            List<PartProblem> problems)
        {
            var settings = part.Quantity;
            var range = RangeFor(settings, selection);

            if (range is null)
            {
                // No rules and no default: the site describes no quantity for this part, and
                // inventing a bound here would refuse a book the site would have accepted.
                if (settings.Rules.Count == 0)
                    return;

                problems.Add(new PartProblem(
                    PartProblemKind.QuantityNotAllowed,
                    "Для этой комбинации сайт не предлагает " + Counter(settings) + "."));
                return;
            }

            if (range.Allows(selection.Quantity))
                return;

            var text = new StringBuilder();
            text.Append(Counter(settings)).Append(": от ").Append(range.From).Append(" до ").Append(range.To);

            if (range.Step > 1)
                text.Append(" с шагом ").Append(range.Step);

            problems.Add(new PartProblem(PartProblemKind.QuantityNotAllowed, text.Append('.').ToString()));
        }

        /// <summary>
        /// The range that governs the selection: the rule of a chosen element, else the part's
        /// own default.
        /// </summary>
        private static QuantityRange? RangeFor(ProductQuantitySettings settings, PartSelection selection)
        {
            for (var index = 0; index < settings.Rules.Count; index++)
            {
                var rule = settings.Rules[index];

                if (!long.TryParse(rule.OptionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var optionId))
                    continue;

                if (selection.ElementOf(optionId) is not { } elementId)
                    continue;

                if (rule.AppliesTo(rule.OptionId, elementId.ToString(CultureInfo.InvariantCulture)))
                    return rule.Range;
            }

            return settings.Default;
        }

        private static string Counter(ProductQuantitySettings settings) =>
            string.IsNullOrWhiteSpace(settings.DisplayName)
                ? "Количество"
                : settings.DisplayName;

        private static bool Offers(ProductOption option, long elementId)
        {
            for (var index = 0; index < option.Elements.Count; index++)
            {
                if (option.Elements[index].Id == elementId)
                    return true;
            }

            return false;
        }

        private static bool Contains(IReadOnlyList<string> ids, long elementId)
        {
            for (var index = 0; index < ids.Count; index++)
            {
                if (long.TryParse(ids[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                    parsed == elementId)
                {
                    return true;
                }
            }

            return false;
        }

        private static long? DependencyId(string? value) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

        private static string TitleOf(ProductPart part, long optionId)
        {
            for (var index = 0; index < part.Options.Count; index++)
            {
                if (part.Options[index].Id == optionId)
                    return part.Options[index].Title;
            }

            return optionId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The names of the dependency elements, quoted, as the site calls them.</summary>
        private static string NamesOf(ProductPart part, long optionId, IReadOnlyList<string> elementIds)
        {
            var names = new List<string>();

            for (var index = 0; index < elementIds.Count; index++)
            {
                if (long.TryParse(elementIds[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    names.Add("«" + NameOf(part, optionId, id) + "»");
            }

            return names.Count == 0 ? "любой вариант" : string.Join(" или ", names);
        }

        private static string NameOf(ProductPart part, long optionId, long elementId)
        {
            for (var index = 0; index < part.Options.Count; index++)
            {
                var option = part.Options[index];
                if (option.Id != optionId)
                    continue;

                for (var element = 0; element < option.Elements.Count; element++)
                {
                    if (option.Elements[element].Id == elementId)
                        return option.Elements[element].Title;
                }
            }

            return elementId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
