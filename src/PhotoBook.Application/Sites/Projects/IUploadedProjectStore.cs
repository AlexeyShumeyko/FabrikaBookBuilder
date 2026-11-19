using System;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>Remembers the project a group was last uploaded to, by <see cref="GroupKey"/>.</summary>
    /// <remarks>
    /// The created-project record is dropped as soon as a run finishes, because the next send is
    /// a deliberate second copy and must make a new project. This record is the other half: it
    /// keeps the number of the finished project so the order page can say that a group has
    /// already been uploaded and leave it out of the coming run by default. Sending it again is
    /// still allowed - the customer ticks it back in.
    /// </remarks>
    public interface IUploadedProjectStore
    {
        /// <summary>The project a group was last uploaded to, or null when it never was.</summary>
        long? FindUploaded(string key);

        /// <summary>Remembers a finished upload; fails when it could not be written down.</summary>
        Result RememberUploaded(string key, long projectId);
    }
}
