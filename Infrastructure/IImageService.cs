using System.Threading.Tasks;
namespace PhotoBookRenamer.Infrastructure
{
    /// <summary>
    /// What a photograph says about itself. Reduced copies are a separate concern, behind
    /// <see cref="PhotoBook.Application.IThumbnailProvider"/>, because that is the part whose
    /// cost changes from one UI engine to another.
    /// </summary>
    public interface IImageService
    {
        /// <summary>Pixel size, read from the header. Zero when the file is missing or unreadable.</summary>
        Task<(int Width, int Height)> GetImageDimensionsAsync(string filePath);

        /// <summary>The photograph that is the cover of its folder, or null when there is none.</summary>
        Task<string?> DetectCoverAsync(string[] filePaths);
    }
}
