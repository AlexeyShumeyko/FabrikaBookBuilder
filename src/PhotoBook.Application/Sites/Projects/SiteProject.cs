namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>A project the site created, as the site named it.</summary>
    /// <param name="Id">The project's id on the platform.</param>
    /// <param name="ProductId">The product the project was created for.</param>
    /// <param name="Title">The project's title, which the site may leave empty.</param>
    /// <param name="Id">The project on the site.</param>
    /// <param name="ProductId">The product the project was made for.</param>
    /// <param name="Title">The project's name, empty when the site names it later.</param>
    /// <param name="FirstItemId">
    /// The book the site put into the new project, or zero when it named none. Files of that
    /// book are uploaded against it, so the number is carried rather than looked up again.
    /// </param>
    public sealed record SiteProject(long Id, long ProductId, string Title, long FirstItemId = 0);
}
