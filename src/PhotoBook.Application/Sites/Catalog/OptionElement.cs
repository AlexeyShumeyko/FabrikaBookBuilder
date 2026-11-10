namespace PhotoBook.Application.Sites.Catalog
{
    /// <summary>One choice inside an option: "Картон", "Глянцевая", "Без прослойки".</summary>
    public sealed record OptionElement(long Id, string Title);
}
