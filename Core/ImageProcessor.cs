using System;
using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PhotoBookRenamer.Core
{
    public static class ImageProcessor
    {
        public static async Task<(int Width, int Height)> GetDimensionsAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var image = Image.Load(filePath);
                    return (image.Width, image.Height);
                }
                catch
                {
                    return (0, 0);
                }
            });
        }

        public static async Task<string> CreateThumbnailAsync(string sourcePath, string thumbnailPath, int maxSize = 200)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var thumbDir = Path.GetDirectoryName(thumbnailPath);
                    if (!string.IsNullOrEmpty(thumbDir) && !Directory.Exists(thumbDir))
                    {
                        Directory.CreateDirectory(thumbDir);
                    }

                    using var image = Image.Load(sourcePath);
                    var ratio = Math.Min((double)maxSize / image.Width, (double)maxSize / image.Height);
                    var newWidth = (int)(image.Width * ratio);
                    var newHeight = (int)(image.Height * ratio);

                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(newWidth, newHeight),
                        Mode = ResizeMode.Max
                    }));

                    image.SaveAsJpeg(thumbnailPath);
                    return thumbnailPath;
                }
                catch
                {
                    return string.Empty;
                }
            });
        }
    }
}





