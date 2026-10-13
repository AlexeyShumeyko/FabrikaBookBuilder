namespace PhotoBook.Application
{
    /// <summary>
    /// When a queued action should run, in terms every engine can honour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WPF calls these priorities, and Avalonia has priorities of its own with the same
    /// shapes - one higher, one lower, one "whenever you are idle". Naming them here keeps
    /// the meaning ("after layout has happened") and drops the type that implements it.
    /// </para>
    /// <para>
    /// <see cref="Normal"/> is the one to use unless something is genuinely being measured:
    /// priority is not a hint about importance, it is a statement about when a thing must
    /// happen relative to the others queued on the same thread.
    /// </para>
    /// </remarks>
    public enum UiDispatchPriority
    {
        /// <summary>With the rest of the ordinary work. The default, and almost always right.</summary>
        Normal,

        /// <summary>
        /// Just after layout. Use it to look at something that only exists once sizes have
        /// been computed - which is how the open-time measurements know when to look.
        /// </summary>
        Render,

        /// <summary>
        /// Once the queue above has drained. Used to measure the idle cost of a screen.
        /// </summary>
        Background,

        /// <summary>
        /// Last, when the thread has nothing left to do.
        /// </summary>
        ContextIdle
    }
}
