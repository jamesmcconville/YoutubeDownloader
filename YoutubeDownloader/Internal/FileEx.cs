﻿using System.IO;
using System.Linq;

namespace YoutubeDownloader.Internal
{
    internal static class FileEx
    {
        public static string MakeSafeFileName(string fileName) =>
            Path.GetInvalidFileNameChars().Aggregate(fileName, (current, invalidChar) => current.Replace(invalidChar, '_'));

        public static string MakeSafeDirectoryName(string directoryName) =>
            MakeSafeFileName(directoryName).Replace('.', '_');

        public static string MakeUniqueFilePath(string baseFilePath, int maxAttempts = 100)
        {
            // Check if base file path exists
            if (!File.Exists(baseFilePath))
                return baseFilePath;

            // Get file path components
            var baseDirPath = Path.GetDirectoryName(baseFilePath);
            var baseFileNameWithoutExtension = Path.GetFileNameWithoutExtension(baseFilePath);
            var baseFileExtension = Path.GetExtension(baseFilePath);

            // Try with incrementing suffixes
            for (var i = 1; i <= maxAttempts; i++)
            {
                // Assemble file path
                var filePath = $"{baseFileNameWithoutExtension} ({i}){baseFileExtension}";
                if (!string.IsNullOrWhiteSpace(baseDirPath))
                    filePath = Path.Combine(baseDirPath, filePath);

                // Check if file exists
                if (!File.Exists(filePath))
                    return filePath;
            }

            // If number of attempts exceeded, just return original path
            return baseFilePath;
        }

        public static void CreateEmptyFile(string filePath) => File.WriteAllText(filePath, "");

        public static void CreateDirectoriesForFile(string filePath)
        {
            var dirPath = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dirPath))
                Directory.CreateDirectory(dirPath);
        }
    }
}