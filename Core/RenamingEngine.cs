using System;
using System.IO;

namespace PhotoBookRenamer.Core
{
    public static class RenamingEngine
    {
        public static string GenerateFileName(int bookIndex, int fileIndex)
        {
            if (bookIndex < 1 || bookIndex > 999)
                throw new ArgumentException("Book index must be between 1 and 999", nameof(bookIndex));
            
            if (fileIndex < 0 || fileIndex > 99)
                throw new ArgumentException("File index must be between 0 and 99", nameof(fileIndex));

            return $"{bookIndex:D3}-{fileIndex:D2}.jpg";
        }

        public static (int BookIndex, int FileIndex) ParseFileName(string fileName)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var parts = nameWithoutExt.Split('-');
            
            if (parts.Length != 2)
                throw new ArgumentException("Invalid file name format", nameof(fileName));

            if (int.TryParse(parts[0], out var bookIndex) && 
                int.TryParse(parts[1], out var fileIndex))
            {
                return (bookIndex, fileIndex);
            }

            throw new ArgumentException("Invalid file name format", nameof(fileName));
        }
    }
}

