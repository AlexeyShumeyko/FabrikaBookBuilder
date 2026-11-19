namespace PhotoBook.Application.Sites
{
    /// <summary>
    /// Keeps the signed-in session between runs of the program.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The program asks for the password once. Closing it and opening it again must not ask
    /// again while the platform's seven-day token is alive, otherwise every start looks like a
    /// lost session to the customer.
    /// </para>
    /// <para>
    /// The password never reaches this port. Only what the platform issued is stored, and
    /// <see cref="Load"/> answers nothing for a session the platform would already refuse, so a
    /// stale token cannot be mistaken for a live one.
    /// </para>
    /// </remarks>
    public interface ISiteSessionStore
    {
        /// <summary>The stored session, or null when there is none or it cannot be used.</summary>
        SiteSession? Load();

        /// <summary>Stores the session, replacing whatever was there.</summary>
        void Save(SiteSession session);

        /// <summary>Forgets the stored session; safe to call when there is none.</summary>
        void Clear();
    }
}
