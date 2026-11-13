using System;
using System.Collections.Generic;
using PhotoBook.Application.Sites;
using PhotoBook.Application.Sites.Completion;
using PhotoBook.Application.Sites.Projects;

namespace PhotoBook.Application.Tests.Sites.Completion
{
    /// <summary>
    /// The roadmap asks for a link into the right project on the right site, so the rule is
    /// pinned for both sites rather than for whichever one was tried first.
    /// </summary>
    public class ProjectLinkTests
    {
        [Fact]
        public void PointAtTheProjectOnTheSiteOfTheSession()
        {
            Assert.Equal(
                "https://fabrika-fotoknigi.com/layflat?projectId=527733",
                ProjectLink.Of(SiteRegion.Com, Project(SiteRegion.Com, "/layflat", 527733)));

            Assert.Equal(
                "https://fabrika-fotoknigi.by/layflat?projectId=527733",
                ProjectLink.Of(SiteRegion.By, Project(SiteRegion.By, "/layflat", 527733)));
        }

        [Fact]
        public void PutTheSlashInThemselvesRatherThanDoublingIt()
        {
            Assert.Equal(
                "https://fabrika-fotoknigi.com/layflat?projectId=42",
                ProjectLink.Of(SiteRegion.Com, Project(SiteRegion.Com, "layflat", 42)));
        }

        [Fact]
        public void GiveNoLinkRatherThanAGuessWhenTheSiteNamesNoProductPage()
        {
            Assert.Equal(string.Empty, ProjectLink.Of(SiteRegion.Com, Project(SiteRegion.Com, string.Empty, 42)));
            Assert.Equal(string.Empty, ProjectLink.Of(SiteRegion.Com, Project(SiteRegion.Com, "/layflat", 0)));
        }

        private static SiteProjectSnapshot Project(SiteRegion region, string productUrl, long id) =>
            new(id, 4494, string.Empty, productUrl, true, Array.Empty<SiteProjectItem>());
    }
}
