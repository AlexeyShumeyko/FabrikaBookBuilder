using System.Linq;
using PhotoBook.Application.Sites.Catalog;
using PhotoBook.Infrastructure.Sites.Catalog;

namespace PhotoBook.Application.Tests.Sites.Catalog
{
    /// <summary>
    /// The uploader's own document for one format is read as the site sends it. The shape below
    /// is a trimmed copy of what the live site returned on 2026-11-15 for «Лайфлат 21x30»
    /// (product 4494, component 9255).
    /// </summary>
    public class SiteFormatParserTests
    {
        private const string Layflat = """
        {
          "productId": 4494,
          "productComponentId": 9255,
          "productTitle": "Лайфлат  ",
          "productKind": "kit",
          "productComponentsTitles": { "9255": "21x30", "9257": "20x20" },
          "productPartsTitles": { "93": "Обложка", "94": "Разворот" },
          "calculatorParts": [
            {
              "title": "Формат", "type": "select", "name": "params[productComponentId]",
              "elements": [
                { "value": 9255, "title": "21x30" },
                { "value": 9257, "title": "20x20" }
              ],
              "value": 9255, "required": true
            },
            { "productPartId": 93, "type": "hidden", "name": "params[productPartQuantity][93]", "value": "1" },
            {
              "optionId": 70519, "title": "Ламинация обложки", "type": "select",
              "name": "params[options][70519]", "value": null,
              "elements": [
                { "value": 450642, "title": "Глянцевая" },
                { "value": 450641, "title": "Зернистая" }
              ],
              "required": 1, "productPartId": 93
            },
            {
              "productPartId": 94, "title": "Количество разворотов", "type": "range",
              "name": "params[productPartQuantity][94]",
              "options": { "min": "2", "max": "30", "step": "", "allowManualInput": "1" },
              "value": null,
              "quantityByOptionsSettings": {
                "enabled": "1",
                "rules": [
                  { "option_elements_ids": ["420270"], "option_id": "65739", "range_from": "10", "range_step": "1", "range_to": "30" }
                ]
              }
            },
            {
              "optionId": 65075, "title": "Плотность разворота", "type": "select",
              "name": "params[options][65075]", "value": null,
              "elements": [
                { "value": 417892, "title": "Картон" },
                { "value": 417891, "title": "Бумага" }
              ],
              "required": 1, "productPartId": 94
            },
            {
              "optionId": 65739, "title": "Прослойка для бумаги", "type": "select",
              "name": "params[options][65739]", "value": null,
              "dependencies": {
                "depend": "65075", "dependElements": ["417891"],
                "excluded": "65075", "excludedElements": ["417892"]
              },
              "elements": [
                { "value": 420270, "title": "Без прослойки (10–30 разворотов)" }
              ],
              "required": 1, "productPartId": 94
            }
          ],
          "integration": {
            "id": 37556,
            "elements": [
              { "id": 26121, "settings": { "productPartId": "93", "filesCellsCountTo": "1" } },
              {
                "id": 26129,
                "settings": {
                  "productPartId": "94", "pagesCountPartId": "94",
                  "pagesCountMultiplier": "1", "pagesCountAddition": "0"
                }
              }
            ]
          }
        }
        """;

        [Fact]
        public void ReadsTheFormatTheCustomerPicked()
        {
            var product = new SiteFormatParser().Parse(Layflat, 4494, 9255).Value!;

            Assert.Equal(4494, product.Id);
            Assert.Equal("Лайфлат", product.Title);
            Assert.Single(product.Variants);
            Assert.Equal(9255, product.Variants[0].Id);
            Assert.Equal("21x30", product.Variants[0].Title);
        }

        [Fact]
        public void KnowsThePartThePublisherSendsPagesInto()
        {
            var parts = new SiteFormatParser().Parse(Layflat, 4494, 9255).Value!.Variants[0].Parts;

            var cover = parts.Single(part => part.Id == 93);
            var spreads = parts.Single(part => part.Id == 94);

            Assert.Equal("Обложка", cover.Title);
            Assert.False(cover.HoldsPages);
            Assert.Equal(1, cover.Quantity.Default!.From);

            Assert.Equal("Разворот", spreads.Title);
            Assert.True(spreads.HoldsPages);
            Assert.Equal(2, spreads.Quantity.Default!.From);
            Assert.Equal(30, spreads.Quantity.Default!.To);
        }

        [Fact]
        public void KeepsTheOptionsOfEachPartWithTheirChoicesAndDependencies()
        {
            var parts = new SiteFormatParser().Parse(Layflat, 4494, 9255).Value!.Variants[0].Parts;

            var cover = parts.Single(part => part.Id == 93).Options.Single();
            var spreads = parts.Single(part => part.Id == 94).Options;

            Assert.Equal(450642, cover.Elements[0].Id);
            Assert.True(cover.IsRequired);

            var paper = spreads.Single(option => option.Id == 65739);
            Assert.Equal("65075", paper.DependsOnOptionId);
            Assert.Equal("65075", paper.ExcludedOptionId);
            Assert.Equal(new[] { "417891" }, paper.DependsOnElementIds);
            Assert.Equal(new[] { "417892" }, paper.ExcludedElementIds);
        }

        [Fact]
        public void SaysSoWhenTheDocumentIsNotTheOneExpected()
        {
            Assert.False(new SiteFormatParser().Parse("[]", 4494, 9255).IsSuccess);
            Assert.False(new SiteFormatParser().Parse("{ not json", 4494, 9255).IsSuccess);
            Assert.False(new SiteFormatParser().Parse("", 4494, 9255).IsSuccess);
        }
    }
}
