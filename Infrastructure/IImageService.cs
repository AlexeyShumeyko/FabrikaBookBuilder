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
    }
}





