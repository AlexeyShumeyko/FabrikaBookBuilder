namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// Who the signed-in account belongs to, as the site names it.
    /// </summary>
    /// <param name="Id">The account's id on the platform.</param>
    /// <param name="DisplayName">How the person is called: the account's name, or its address.</param>
    /// <param name="Email">The address the site holds for the account.</param>
    /// <remarks>
    /// The name is resolved where the profile is read rather than here, because the site's
    /// fields are uneven: some accounts have a full name, others only a first name and a
    /// surname, and a few have neither. The header shows one line, so the choice has to be made
    /// once instead of in every window that says hello.
    /// </remarks>
    public sealed record SiteProfile(long Id, string DisplayName, string Email);
}
