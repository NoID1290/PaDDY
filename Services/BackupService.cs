using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PaDDY.Helpers;

namespace PaDDY.Services
{
    /// <summary>
    /// Handles backup and restore of user data.
    /// Produces an encrypted proprietary .PADBACK file.
    /// </summary>
    public class BackupService
    {
        public string UsrDataPath { get; set; } = AppDataPaths.RecordingStorePath;
        public string UsrDataSettings { get; set; } = AppDataPaths.SettingsPath;
        public string UsrDataEffectSettings { get; set; } = AppDataPaths.EffectSettingsPath;


        private static readonly byte[] BackupKey =
        {
            0x42, 0x11, 0x98, 0x73, 0xA5, 0xC1, 0x2E, 0x7F,
            0x55, 0x19, 0x8D, 0x34, 0x6A, 0xBB, 0xC7, 0x12,
            0x21, 0x93, 0x4D, 0xE8, 0x5C, 0x77, 0xAF, 0x03,
            0x91, 0xD4, 0x62, 0x18, 0x3B, 0xCE, 0xF5, 0x80
        };

        private static readonly byte[] BackupIV =
        {
            0x12, 0x34, 0x56, 0x78,
            0x90, 0xAB, 0xCD, 0xEF,
            0x11, 0x22, 0x33, 0x44,
            0x55, 0x66, 0x77, 0x88
        };

        private const string FileSignature = "PADBACK1";

        private bool InitBackup()
        {
            try
            {
                bool hasAnyFile = false;

                if (File.Exists(UsrDataPath))
                {
                    hasAnyFile = true;
                    TryCheckpointRecordingDatabase();
                }
                else
                {
                    Console.WriteLine("Warning: Recording store not found; it will be skipped.");
                }

                if (File.Exists(UsrDataSettings))
                {
                    hasAnyFile = true;
                }
                else
                {
                    Console.WriteLine("Warning: Settings file not found; it will be skipped.");
                }

                if (File.Exists(UsrDataEffectSettings))
                {
                    hasAnyFile = true;
                }
                else
                {
                    Console.WriteLine("Warning: Effect settings file not found; it will be skipped.");
                }

                if (!hasAnyFile)
                    throw new InvalidOperationException("No user data files were found to back up.");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing backup: {ex.Message}");
                return false;
            }
        }

        private void TryCheckpointRecordingDatabase()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={UsrDataPath};Mode=ReadOnly;Cache=Shared");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                command.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to checkpoint recording database before backup: {ex.Message}");
            }
        }

        private string CreateRecordingDatabaseBackupFile()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"PaDDY_RecordingsBackup_{Guid.NewGuid():N}.db");

            try
            {
                var sourceBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = UsrDataPath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Shared
                };

                var destinationBuilder = new SqliteConnectionStringBuilder
                {
                    DataSource = tempPath
                };

                using var source = new SqliteConnection(sourceBuilder.ConnectionString);
                using var destination = new SqliteConnection(destinationBuilder.ConnectionString);

                source.Open();
                destination.Open();

                source.BackupDatabase(destination);
                return tempPath;
            }
            catch
            {
                if (File.Exists(tempPath))
                    TryDeleteFile(tempPath);
                throw;
            }
        }

        private static void DeleteRecordingCompanionFiles(string basePath)
        {
            TryDeleteFile(basePath + "-wal");
            TryDeleteFile(basePath + "-shm");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }

        /// <summary>
        /// Creates an encrypted .PADBACK file.
        /// </summary>
        public bool CreateBackup(string backupFilePath)
        {
            try
            {
                if (!InitBackup())
                    return false;

                if (!backupFilePath.EndsWith(".PADBACK", StringComparison.OrdinalIgnoreCase))
                    backupFilePath += ".PADBACK";

                using var zipMemory = new MemoryStream();

                string? tempRecordingBackup = null;
                try
                {
                    using (var archive = new ZipArchive(zipMemory, ZipArchiveMode.Create, true))
                    {
                        if (File.Exists(UsrDataPath))
                        {
                            tempRecordingBackup = CreateRecordingDatabaseBackupFile();
                            var recordingEntry = archive.CreateEntry(Path.GetFileName(UsrDataPath));
                            using (var sourceStream = new FileStream(tempRecordingBackup, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var entryStream = recordingEntry.Open())
                            {
                                sourceStream.CopyTo(entryStream);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Skipping missing recording store file during backup.");
                        }

                        if (File.Exists(UsrDataSettings))
                        {
                            var settingsEntry = archive.CreateEntry(Path.GetFileName(UsrDataSettings));
                            using var sourceStream = new FileStream(UsrDataSettings, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var entryStream = settingsEntry.Open();
                            sourceStream.CopyTo(entryStream);
                        }

                        if (File.Exists(UsrDataEffectSettings))
                        {
                            var effectEntry = archive.CreateEntry(Path.GetFileName(UsrDataEffectSettings));
                            using var sourceStream = new FileStream(UsrDataEffectSettings, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var entryStream = effectEntry.Open();
                            sourceStream.CopyTo(entryStream);
                        }
                    }
                }
                finally
                {
                    if (!string.IsNullOrEmpty(tempRecordingBackup))
                        TryDeleteFile(tempRecordingBackup);
                }

                zipMemory.Position = 0;

                using var output = File.Create(backupFilePath);

                // Write proprietary header/signature
                using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(FileSignature);
                }

                using var aes = Aes.Create();
                aes.Key = BackupKey;
                aes.IV = BackupIV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var cryptoStream =
                    new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);

                zipMemory.CopyTo(cryptoStream);
                cryptoStream.FlushFinalBlock();

                Console.WriteLine($"Backup created successfully: {backupFilePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Backup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restores an encrypted .PADBACK file.
        /// </summary>
        public bool RestoreBackup(string backupFilePath)
        {
            string? tempDirectory = null;

            try
            {
                if (!File.Exists(backupFilePath))
                    throw new FileNotFoundException("Backup file not found.", backupFilePath);

                using var input = File.OpenRead(backupFilePath);

                using (var reader = new BinaryReader(input, System.Text.Encoding.UTF8, true))
                {
                    string signature = reader.ReadString();

                    if (signature != FileSignature)
                        throw new InvalidDataException("Invalid PADBACK file format.");
                }

                using var aes = Aes.Create();
                aes.Key = BackupKey;
                aes.IV = BackupIV;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using var cryptoStream =
                    new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);

                using var zipMemory = new MemoryStream();
                cryptoStream.CopyTo(zipMemory);

                zipMemory.Position = 0;

                tempDirectory = Path.Combine(
                    Path.GetTempPath(),
                    $"PADBACK_{Guid.NewGuid():N}");

                Directory.CreateDirectory(tempDirectory);

                using (var archive = new ZipArchive(zipMemory, ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(tempDirectory, true);
                }

                string recordingFile =
                    Path.Combine(tempDirectory, Path.GetFileName(UsrDataPath));

                string recordingWalFile =
                    Path.Combine(tempDirectory, Path.GetFileName(UsrDataPath) + "-wal");

                string recordingShmFile =
                    Path.Combine(tempDirectory, Path.GetFileName(UsrDataPath) + "-shm");

                string settingsFile =
                    Path.Combine(tempDirectory, Path.GetFileName(UsrDataSettings));

                string effectSettingsFile =
                    Path.Combine(tempDirectory, Path.GetFileName(UsrDataEffectSettings));

                Directory.CreateDirectory(Path.GetDirectoryName(UsrDataPath)!);
                DeleteRecordingCompanionFiles(UsrDataPath);

                if (File.Exists(recordingFile))
                    File.Copy(recordingFile, UsrDataPath, true);

                if (File.Exists(recordingWalFile))
                    File.Copy(recordingWalFile, UsrDataPath + "-wal", true);

                if (File.Exists(recordingShmFile))
                    File.Copy(recordingShmFile, UsrDataPath + "-shm", true);

                if (File.Exists(settingsFile))
                    File.Copy(settingsFile, UsrDataSettings, true);

                if (File.Exists(effectSettingsFile))
                    File.Copy(effectSettingsFile, UsrDataEffectSettings, true);

                Console.WriteLine("Backup restored successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Restore failed: {ex.Message}");
                return false;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempDirectory) &&
                    Directory.Exists(tempDirectory))
                {
                    try
                    {
                        Directory.Delete(tempDirectory, true);
                    }
                    catch
                    {
                        // Ignore cleanup failures.
                    }
                }
            }
        }

        /// <summary>
        /// Checks SQLite integrity.
        /// </summary>
        public bool ValidateDatabase()
        {
            try
            {
                if (!File.Exists(UsrDataPath))
                    return false;

                using var connection =
                    new SqliteConnection($"Data Source={UsrDataPath}");

                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA integrity_check;";

                string result =
                    command.ExecuteScalar()?.ToString() ?? string.Empty;

                return result.Equals("ok", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database validation failed: {ex.Message}");
                return false;
            }
        }
    }
}