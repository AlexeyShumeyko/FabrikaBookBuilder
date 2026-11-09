namespace PhotoBook.Application
{
    /// <summary>
    /// Which button a three-way question was answered with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This lived in the WPF shell's <c>ConfirmDialog.xaml.cs</c>, which put a decision in
    /// a file that describes a window. The answer to a question is not a window: it is the
    /// same fact in any engine, and the shell that asks the question should not have to name
    /// the type of the dialog that will collect the answer.
    /// </para>
    /// <para>
    /// Three values rather than a bool, because these questions genuinely have three
    /// answers and the third one - "stay here" - is the one a two-valued version has to
    /// smuggle in as a magic string.
    /// </para>
    /// </remarks>
    public enum ConfirmOutcome
    {
        /// <summary>The primary, right-most, coloured button.</summary>
        Primary,

        /// <summary>The secondary button, offered only by a three-way question.</summary>
        Secondary,

        /// <summary>Cancel, Escape, or the window's close button.</summary>
        Cancelled
    }
}
