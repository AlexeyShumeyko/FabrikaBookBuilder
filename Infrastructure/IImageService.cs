using System.Threading.Tasks;

namespace PhotoBookRenamer.Infrastructure
{
    public interface IImageService
    {
        Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath);
        Task<string> CreateThumbnailAsync(string sourcePath, string thumbnailPath, int maxSize = 200);
        Task<string?> DetectCoverAsync(string[] filePaths);
        Task LoadThumbnailsAsync(System.Collections.Generic.IEnumerable<string> filePaths);
        string GetFilePathHash(string filePath);

        /// <summary>
        /// Where the thumbnail of a photo lives, or null when there is none yet. The one
        /// place that knows the naming: the editor used to rebuild the same path inline in
        /// three places, which is how a slot can end up showing nothing while a perfectly
        /// good thumbnail sits on disk under a name nobody looked for.
        /// </summary>
        string? GetThumbnailPath(string filePath);
    }
}





