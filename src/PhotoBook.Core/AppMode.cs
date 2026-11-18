namespace PhotoBook.Core
{
    /// <summary>The screen the shell is currently showing.</summary>
    public enum AppMode
    {
        StartScreen,
        ProjectList,
        UniqueFolders,
        Combined,

        /// <summary>The order page: what will go to the site, opened from the project.</summary>
        SiteOrder
    }
}
