using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MusikArchivApp.Data
{
    /// <summary>
    /// Zentraler Datenordner: portable (exe/data/) oder %AppData%/MusikArchivApp/.
    /// </summary>
    public static class AppPaths
    {
        private const string AppFolderName = "MusikArchivApp";
        private const string DatabaseFileName = "musikarchiv.db";
        private static string? dataRoot;

        public static string GetDataRoot()
        {
            if (dataRoot != null)
            {
                return dataRoot;
            }

            dataRoot = ResolveDataRoot();
            Directory.CreateDirectory(dataRoot);
            return dataRoot;
        }

        public static bool IsPortableMode()
        {
            var exeDir = AppContext.BaseDirectory;
            return File.Exists(Path.Combine(exeDir, "portable.flag"))
                || Directory.Exists(Path.Combine(exeDir, "data"));
        }

        public static string GetDatabasePath()
        {
            return Path.Combine(GetDataRoot(), DatabaseFileName);
        }

        public static string GetBackupsDirectory()
        {
            var dir = Path.Combine(GetDataRoot(), "backups");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetCacheDirectory()
        {
            var dir = Path.Combine(GetDataRoot(), "cache");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetPreviewCacheDirectory()
        {
            var dir = Path.Combine(GetCacheDirectory(), "preview");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetWebViewDirectory()
        {
            var dir = Path.Combine(GetCacheDirectory(), "WebView2");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetColumnConfigPath()
        {
            return Path.Combine(GetDataRoot(), "column_config.json");
        }

        public static string GetNotenDirectory()
        {
            var dir = Path.Combine(GetDataRoot(), "Noten");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetPieceNotenDirectory(long pieceId, string title)
        {
            return Path.Combine(GetNotenDirectory(), SheetMusicPaths.BuildPhysicalFolderName(pieceId, title));
        }

        public static void TryDeletePieceNotenDirectory(long pieceId, string title)
        {
            TryDeleteAllPieceNotenData(pieceId);
        }

        public static void TryDeleteAllPieceNotenData(long pieceId, IEnumerable<string>? storedPaths = null)
        {
            if (storedPaths != null)
            {
                foreach (var storedPath in storedPaths)
                {
                    if (string.IsNullOrWhiteSpace(storedPath))
                    {
                        continue;
                    }

                    var fullPath = SheetMusicPaths.ResolveStoredPath(storedPath);
                    TryDeleteFile(fullPath);
                    TryDeleteEmptyDirectory(Path.GetDirectoryName(fullPath));
                }
            }

            var notenDir = GetNotenDirectory();
            if (!Directory.Exists(notenDir))
            {
                return;
            }

            foreach (var dir in Directory.GetDirectories(notenDir, $"{pieceId}_*"))
            {
                TryDeleteDirectory(dir);
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore locked files
            }
        }

        private static void TryDeleteEmptyDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // ignore locked directories
            }
        }

        private static void TryDeleteDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // ignore locked directories
            }
        }

        public static void TryDeleteAllNotenDirectories()
        {
            var dir = Path.Combine(GetDataRoot(), "Noten");
            if (!Directory.Exists(dir))
            {
                return;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // ignore locked files
            }
        }

        /// <summary>Früherer Name – zeigt jetzt auf GetDataRoot().</summary>
        public static string GetAppDataDirectory() => GetDataRoot();

        private static string ResolveDataRoot()
        {
            var exeDir = AppContext.BaseDirectory;
            var portableFlag = Path.Combine(exeDir, "portable.flag");
            var portableData = Path.Combine(exeDir, "data");

            if (File.Exists(portableFlag) || Directory.Exists(portableData))
            {
                Directory.CreateDirectory(portableData);
                MigrateAppDataToPortableIfNeeded(portableData);
                MigrateLegacyExeDatabaseIfNeeded(portableData);
                return portableData;
            }

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);
            Directory.CreateDirectory(appDataRoot);
            MigrateLegacyExeDatabaseIfNeeded(appDataRoot);
            return appDataRoot;
        }

        private static void MigrateAppDataToPortableIfNeeded(string portableData)
        {
            var portableDb = Path.Combine(portableData, DatabaseFileName);
            if (File.Exists(portableDb))
            {
                return;
            }

            var appDataRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);
            if (!Directory.Exists(appDataRoot))
            {
                return;
            }

            CopyDirectoryContents(appDataRoot, portableData);
        }

        private static void MigrateLegacyExeDatabaseIfNeeded(string targetRoot)
        {
            var targetDb = Path.Combine(targetRoot, DatabaseFileName);
            if (File.Exists(targetDb))
            {
                return;
            }

            var legacyDb = Path.Combine(AppContext.BaseDirectory, DatabaseFileName);
            if (File.Exists(legacyDb))
            {
                File.Copy(legacyDb, targetDb);
            }
        }

        private static void CopyDirectoryContents(string sourceDir, string targetDir)
        {
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(sourceDir, sourcePath);
                var targetPath = Path.Combine(targetDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (!File.Exists(targetPath))
                {
                    File.Copy(sourcePath, targetPath);
                }
            }
        }
    }
}
