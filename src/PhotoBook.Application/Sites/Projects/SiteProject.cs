namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>A project the site created, as the site named it.</summary>
    /// <param name="Id">The project's id on the platform.</param>
    /// <param name="ProductId">The product the project was created for.</param>
    /// <param name="Title">The project's title, which the site may leave empty.</param>
    public sealed record SiteProject(long Id, long ProductId, string Title);
}
