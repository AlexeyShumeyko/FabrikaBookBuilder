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
        SiteOrder,

        /// <summary>The guide: a screen inside the shell, not a window beside it.</summary>
        Help,

        /// <summary>The support page: the hours and the four ways to reach a person.</summary>
        Support
    }
}
