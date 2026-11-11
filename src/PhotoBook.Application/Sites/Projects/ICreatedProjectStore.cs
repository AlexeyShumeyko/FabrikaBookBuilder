using System;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>Remembers the projects the site already created, by <see cref="GroupKey"/>.</summary>
    /// <remarks>
    /// A file on the customer's profile rather than memory: a crash, a closed program or a
    /// dropped connection must not turn the next attempt into a second project on the site.
    /// </remarks>
    public interface ICreatedProjectStore
    {
        /// <summary>The project id remembered for a key, or null when the key is unknown.</summary>
        long? Find(string key);

        /// <summary>Remembers a created project; fails when it could not be written down.</summary>
        Result Remember(string key, long projectId);
    }
}
