using System;
using System.Collections.Generic;
using System.Linq;
using PhotoBook.Application.Sites.Books;

namespace PhotoBook.Application.Sites.Projects
{
    /// <summary>What happened to one group's project.</summary>
    public enum GroupCreationState
    {
        /// <summary>The site created it now.</summary>
        Created,

        /// <summary>It was created earlier and the local record knew it.</summary>
        AlreadyCreated,

        /// <summary>The site refused, or the connection broke.</summary>
        Failed
    }

    /// <summary>One group's outcome, with the project id when there is one.</summary>
    /// <param name="Number">The group's place in the order, starting at one.</param>
    /// <param name="Group">The books the group holds.</param>
    /// <param name="State">Whether it was created, known already, or failed.</param>
    /// <param name="ProjectId">The site's project id, or zero when there is none.</param>
    /// <param name="Error">Why it failed, or what the customer should know about a success.</param>
    public sealed record GroupCreation(
        int Number,
        OrderGroup Group,
        GroupCreationState State,
        long ProjectId,
        string? Error)
    {
        /// <summary>Whether the site holds the project now.</summary>
        public bool HasProject => State != GroupCreationState.Failed;
    }

    /// <summary>Every group of an order after the creation pass, including the failures.</summary>
    /// <remarks>
    /// Partial success is reported rather than rolled back: a project the site already holds
    /// cannot be un-created here, and deleting it to reach a clean state would risk more than
    /// it repairs. What matters is that the customer sees which projects exist and which do
    /// not, and that a retry creates only the missing ones.
    /// </remarks>
    public sealed record ProjectCreationPlan(IReadOnlyList<GroupCreation> Groups)
    {
        /// <summary>Whether every group has a project on the site.</summary>
        public bool IsComplete => Groups.Count > 0 && Groups.All(group => group.HasProject);

        /// <summary>Groups the site did not accept.</summary>
        public IReadOnlyList<GroupCreation> Failures =>
            Groups.Where(group => !group.HasProject).ToList();

        /// <summary>Why the order is not complete, group by group.</summary>
        public IReadOnlyList<string> Problems =>
            Failures
                .Select(group => "Группа " + group.Number + ": " + (group.Error ?? "проект не создан."))
                .ToList();
    }
}
