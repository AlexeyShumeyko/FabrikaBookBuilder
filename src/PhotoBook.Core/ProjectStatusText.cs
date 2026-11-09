namespace PhotoBook.Core
{
    /// <summary>
    /// What a project is called on screen, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The badge on a project card and the options in the list's status filter are the same
    /// words. They used to be written out separately, and they were wrong in a way nobody
    /// could see until both were on the same screen: the card said "в процессе сборки" and
    /// the filter that selected that same state was labelled "готов к печати". Somebody who
    /// pressed the filter was shown cards carrying a different word, and had no way to know
    /// the filter had worked.
    /// </para>
    /// <para>
    /// So: one table, read by both. A status cannot be renamed on one screen without the
    /// other following, because there is nothing to change twice.
    /// </para>
    /// <para>
    /// The names are the ones a photographer would use, not the ones in the enum. The enum
    /// says <c>NotFilled</c> for a run with an empty slot and the screen says "черновик",
    /// because that is the word for it.
    /// </para>
    /// </remarks>
    public static class ProjectStatusText
    {
        /// <summary>The badge for this state, as it appears on a card and in the filter.</summary>
        public static string Of(ProjectStatus status) => status switch
        {
            ProjectStatus.NotFilled => "Черновик",
            ProjectStatus.Ready => "В процессе сборки",
            ProjectStatus.SuccessfullyCompleted => "Готов к печати",
            _ => "Черновик"
        };
    }
}
