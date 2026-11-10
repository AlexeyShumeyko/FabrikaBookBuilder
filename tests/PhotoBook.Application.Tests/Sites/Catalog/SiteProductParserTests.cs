using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Infrastructure.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Catalog
{
    /// <summary>
    /// The catalog model is read from the product page as the site sends it. The two shapes
    /// below are trimmed copies of what the live site returned on 2026-11-10, one from each
    /// kind of counting: a layflat counts spreads and a page book counts pages.
    /// </summary>
    public class SiteProductParserTests
    {
        private const string Layflat = """
        {
          "product": {
            "id": 4494,
            "kind": "universal",
            "name": "Лайфлат",
            "calculator": {
              "variants": [
                {
                  "id": 9255,
                  "name": "21x30",
                  "calculator": {
                    "options": [
                      {
                        "id": 70519, "name": "Ламинация обложки", "required": true,
                        "dependOptionId": "", "dependElementsIds": null,
                        "excludedOptionId": "", "excludedElementsIds": null,
                        "elements": [
                          { "id": 450642, "name": "Глянцевая" },
                          { "id": 450641, "name": "Зернистая" }
                        ]
                      }
                    ],
                    "parts": [
                      {
                        "id": 94,
                        "name": "Разворот",
                        "calculator": {
                          "options": [
                            {
                              "id": 65075, "name": "Плотность разворотов", "required": true,
                              "dependOptionId": "", "dependElementsIds": null,
                              "excludedOptionId": "", "excludedElementsIds": null,
                              "elements": [
                                { "id": 417892, "name": "Картон" },
                                { "id": 417891, "name": "Бумага" }
                              ]
                            },
                            {
                              "id": 65740, "name": "Прослойка разворотов", "required": true,
                              "dependOptionId": "65075", "dependElementsIds": ["417892"],
                              "excludedOptionId": "65075", "excludedElementsIds": ["417891"],
                              "elements": [
                                { "id": 420271, "name": "Без прослойки (2–20 разворотов)" },
                                { "id": 420272, "name": "Картон 1 мм (2–12 разворотов)" }
                              ]
                            }
                          ],
                          "quantitySettings": {
                            "displayName": "Количество разворотов",
                            "quantityByOptionsSettings": {
                              "enabled": "1",
                              "rules": [
                                { "option_id": "65740", "option_elements_ids": ["420271"],
                                  "range_from": "2", "range_to": "20", "range_step": "1" },
                                { "option_id": "65740", "option_elements_ids": ["420272"],
                                  "range_from": "2", "range_to": "12", "range_step": "1" }
                              ]
                            }
                          }
                        }
                      }
                    ]
                  }
                }
              ]
            }
          }
        }
        """;

        private const string PageBook = """
        {
          "product": {
            "id": 4503,
            "kind": "universal",
            "name": "Постраничная книга",
            "calculator": {
              "variants": [
                {
                  "id": 9267,
                  "name": "21x30",
                  "calculator": {
                    "options": [],
                    "parts": [
                      {
                        "id": 96,
                        "name": "Страница",
                        "calculator": {
                          "options": [],
                          "quantitySettings": {
                            "displayName": "Количество страниц",
                            "allowManualInput": "1",
                            "from": "24",
                            "to": "160",
                            "step": "2",
                            "quantityByOptionsSettings": {
                              "enabled": "1",
                              "rules": [
                                { "option_id": "66504", "option_elements_ids": ["425091"],
                                  "range_from": "24", "range_to": "160", "range_step": "2" }
                              ]
                            }
                          }
                        }
                      }
                    ]
                  }
                }
              ]
            }
          }
        }
        """;

        private static SiteProduct Parse(string json, string slug)
        {
            var result = new SiteProductParser().Parse(json, slug);

            Assert.True(result.IsSuccess, result.Error);
            return result.Value!;
        }

        [Fact]
        public void Reads_the_option_that_depends_on_another_option()
        {
            var product = Parse(Layflat, "layflat");
            var variant = Assert.Single(product.Variants);
            var part = Assert.Single(variant.Parts);
            var interlayer = part.Options.Single(option => option.Id == 65740);

            Assert.True(interlayer.IsRequired);
            Assert.Equal("65075", interlayer.DependsOnOptionId);
            Assert.Equal(new[] { "417892" }, interlayer.DependsOnElementIds);
            Assert.Equal("65075", interlayer.ExcludedOptionId);
            Assert.Equal(new[] { "417891" }, interlayer.ExcludedElementIds);
            Assert.Equal(2, interlayer.Elements.Count);
        }

        [Fact]
        public void Keeps_the_cover_options_apart_from_the_spreads()
        {
            var product = Parse(Layflat, "layflat");
            var variant = Assert.Single(product.Variants);

            Assert.Equal(9255, variant.Id);
            Assert.Equal("21x30", variant.Title);
            Assert.Equal(70519, Assert.Single(variant.CoverOptions).Id);
            Assert.Equal(94, Assert.Single(variant.Parts).Id);
        }

        [Fact]
        public void Counts_the_layflat_in_spreads_and_the_page_book_in_pages()
        {
            var layflat = Parse(Layflat, "layflat");
            var pageBook = Parse(PageBook, "polygraphy-book");

            Assert.Equal(QuantityUnit.Spreads, layflat.Variants[0].Parts[0].Quantity.Unit);
            Assert.Equal(QuantityUnit.Pages, pageBook.Variants[0].Parts[0].Quantity.Unit);
        }

        [Fact]
        public void Gives_each_element_its_own_range_under_one_option()
        {
            var quantity = Parse(Layflat, "layflat").Variants[0].Parts[0].Quantity;

            Assert.Equal(2, quantity.Rules.Count);

            var cardboard = quantity.Rules.Single(rule => rule.AppliesTo("65740", "420272"));
            var noInterlayer = quantity.Rules.Single(rule => rule.AppliesTo("65740", "420271"));

            Assert.Equal(new QuantityRange(2, 12, 1), cardboard.Range);
            Assert.Equal(new QuantityRange(2, 20, 1), noInterlayer.Range);
            Assert.True(cardboard.Range.Allows(8));
            Assert.False(cardboard.Range.Allows(14));
        }

        [Fact]
        public void Reads_the_range_the_whole_part_uses()
        {
            var quantity = Parse(PageBook, "polygraphy-book").Variants[0].Parts[0].Quantity;

            Assert.Equal("Количество страниц", quantity.DisplayName);
            Assert.Equal(new QuantityRange(24, 160, 2), quantity.Default);
            Assert.True(quantity.AllowsManualInput);
            Assert.True(quantity.Default!.Allows(24));
            Assert.False(quantity.Default!.Allows(25));
        }

        [Fact]
        public void Leaves_the_unit_unknown_when_the_site_names_the_quantity_otherwise()
        {
            var json = """
            {
              "product": {
                "id": 1, "name": "Что-то новое", "kind": "universal",
                "calculator": { "variants": [ { "id": 2, "name": "21x30", "calculator": {
                  "parts": [ { "id": 3, "name": "Разворот", "calculator": { "quantitySettings": {
                    "displayName": "Количество альбомов" } } } ] } } ] }
              }
            }
            """;

            var quantity = Parse(json, "unknown").Variants[0].Parts[0].Quantity;

            Assert.Equal(QuantityUnit.Unknown, quantity.Unit);
            Assert.Equal("Количество альбомов", quantity.DisplayName);
        }

        [Fact]
        public void Reports_a_broken_document_instead_of_throwing()
        {
            var result = new SiteProductParser().Parse("{ this is not json", "layflat");

            Assert.False(result.IsSuccess);
            Assert.Contains("неизвестном формате", result.Error);
        }

        [Fact]
        public void Refuses_an_answer_that_has_no_product()
        {
            var result = new SiteProductParser().Parse("""{"slug":"layflat"}""", "layflat");

            Assert.False(result.IsSuccess);
            Assert.Contains("нет описания продукта", result.Error);
        }
    }
}
