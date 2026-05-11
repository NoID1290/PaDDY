using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using PaDDY.Helpers;

namespace PaDDY.Services
{
    public class RecordingRecord
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Codec { get; init; } = string.Empty;
        public long DurationMs { get; init; }
        public DateTime CreatedAt { get; init; }
        public bool IsFavorite { get; set; }
    }

    /// <summary>
    /// Persistent recording storage backed by a SQLite database (recordings.dat).
    /// Audio bytes are stored as BLOBs; temp files are materialised on demand
    /// under %TEMP%\paddy-tmp\ for playback and editing.
    /// </summary>
    public sealed class RecordingStore : IDisposable
    {
        // ── Paths ──────────────────────────────────────────────────────────────
        public static readonly string StorePath =
            AppDataPaths.RecordingStorePath;

        public static readonly string TempDir =
            AppDataPaths.PlaybackTempDir;

        // Hidden folder used by AudioCaptureService while a clip is being written.
        public static readonly string InternalTempRecDir =
            AppDataPaths.InternalRecordingTempDir;

        // ── SQLite connection ──────────────────────────────────────────────────
        private readonly SqliteConnection _db;
        private bool _disposed;

        public RecordingStore()
        {
            AppDataPaths.EnsureAppDataRoot();
            MigrateLegacyStore();
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            _db = new SqliteConnection($"Data Source={StorePath}");
            _db.Open();
            Initialize();
        }

        private static void MigrateLegacyStore()
        {
            if (File.Exists(StorePath))
                return;

            // Try migrating from exe-directory location (very old installs).
            if (AppDataPaths.TryMigrateLegacyFile(AppDataPaths.LegacyRecordingStorePath, StorePath))
            {
                TryCopyCompanionFile("-wal", AppDataPaths.LegacyRecordingStorePath);
                TryCopyCompanionFile("-shm", AppDataPaths.LegacyRecordingStorePath);
                return;
            }

            // Try migrating from old %LocalAppData%\PaDDY location.
            if (AppDataPaths.TryMigrateLegacyFile(AppDataPaths.LegacyAppDataRecordingStorePath, StorePath))
            {
                TryCopyCompanionFile("-wal", AppDataPaths.LegacyAppDataRecordingStorePath);
                TryCopyCompanionFile("-shm", AppDataPaths.LegacyAppDataRecordingStorePath);
            }
        }

        private static void TryCopyCompanionFile(string suffix, string legacyBase)
        {
            try
            {
                string legacyCompanion = legacyBase + suffix;
                string targetCompanion = StorePath + suffix;
                if (File.Exists(targetCompanion) || !File.Exists(legacyCompanion))
                    return;

                File.Copy(legacyCompanion, targetCompanion, overwrite: false);
            }
            catch
            {
                // Best-effort migration only.
            }
        }

        private void Initialize()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS recordings (
                    id          TEXT    PRIMARY KEY,
                    display_name TEXT   NOT NULL,
                    codec        TEXT   NOT NULL,
                    duration_ms  INTEGER NOT NULL,
                    created_at   TEXT   NOT NULL,
                    is_favorite  INTEGER NOT NULL DEFAULT 0,
                    audio_data   BLOB   NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_fav     ON recordings(is_favorite);
                CREATE INDEX IF NOT EXISTS idx_created ON recordings(created_at DESC);
                """;
            cmd.ExecuteNonQuery();
        }

        // ── Write operations ───────────────────────────────────────────────────

        public string Add(string displayName, string codec, TimeSpan duration, DateTime createdAt, byte[] audioData)
        {
            string id = Guid.NewGuid().ToString("N");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO recordings(id, display_name, codec, duration_ms, created_at, is_favorite, audio_data)
                VALUES(@id, @dn, @codec, @dur, @cat, 0, @data)
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@dn", displayName);
            cmd.Parameters.AddWithValue("@codec", codec);
            cmd.Parameters.AddWithValue("@dur", (long)duration.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@cat", createdAt.ToString("O"));
            cmd.Parameters.AddWithValue("@data", audioData);
            cmd.ExecuteNonQuery();
            return id;
        }

        public void SetDisplayName(string id, string newDisplayName)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET display_name=@dn WHERE id=@id";
            cmd.Parameters.AddWithValue("@dn", newDisplayName);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void SetFavorite(string id, bool isFavorite)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET is_favorite=@fav WHERE id=@id";
            cmd.Parameters.AddWithValue("@fav", isFavorite ? 1L : 0L);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void UpdateAudioData(string id, byte[] audioData)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET audio_data=@data WHERE id=@id";
            cmd.Parameters.AddWithValue("@data", audioData);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void Delete(string id)
        {
            CleanupTempFile(id);
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "DELETE FROM recordings WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAll(IEnumerable<string> ids)
        {
            using var tx = _db.BeginTransaction();
            foreach (var id in ids)
            {
                CleanupTempFile(id);
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "DELETE FROM recordings WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // ── Read operations ────────────────────────────────────────────────────

        public List<RecordingRecord> GetAll()
        {
            var list = new List<RecordingRecord>();
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT id, display_name, codec, duration_ms, created_at, is_favorite
                FROM recordings
                ORDER BY created_at DESC
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new RecordingRecord
                {
                    Id = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Codec = reader.GetString(2),
                    DurationMs = reader.GetInt64(3),
                    CreatedAt = DateTime.Parse(reader.GetString(4)),
                    IsFavorite = reader.GetInt64(5) != 0
                });
            }
            return list;
        }

        public byte[]? GetBytes(string id)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT audio_data FROM recordings WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            var result = cmd.ExecuteScalar();
            return result is byte[] bytes ? bytes : null;
        }

        // ── Temp-file materialisation ──────────────────────────────────────────

        /// <summary>
        /// Extracts recording bytes to %TEMP%\paddy-tmp\{id}.{codec} and returns the path.
        /// Idempotent: returns existing path if the file already exists.
        /// </summary>
        public string MaterializeToTemp(string id, string codec)
        {
            Directory.CreateDirectory(TempDir);
            string tempPath = Path.Combine(TempDir, $"{id}.{codec}");
            if (File.Exists(tempPath)) return tempPath;

            var bytes = GetBytes(id);
            if (bytes == null)
                throw new InvalidOperationException($"Recording '{id}' not found in store.");

            File.WriteAllBytes(tempPath, bytes);
            return tempPath;
        }

        private void CleanupTempFile(string id)
        {
            if (!Directory.Exists(TempDir)) return;
            foreach (var f in Directory.EnumerateFiles(TempDir, $"{id}.*"))
            {
                try { File.Delete(f); } catch { }
            }
        }

        /// <summary>
        /// Deletes all materialised temp files. Call on startup and shutdown.
        /// </summary>
        public void CleanupAllTempFiles()
        {
            if (!Directory.Exists(TempDir)) return;
            try { Directory.Delete(TempDir, recursive: true); } catch { }
        }

        // Delete the folder used for in-progress recordings, which may contain orphaned temp files if the app crashed during recording. Call on startup and shutdown.
        public void CleanupInternalTempRecordings()
        {
            if (!Directory.Exists(InternalTempRecDir)) return;
            try { Directory.Delete(InternalTempRecDir, recursive: true); } catch { }
        }

        // ── Storage info ───────────────────────────────────────────────────────

        public long GetStoreSizeBytes()
        {
            if (!File.Exists(StorePath)) return 0L;
            return new FileInfo(StorePath).Length;
        }

        /// <summary>
        /// Checkpoints the WAL file and runs VACUUM to reclaim disk space freed by deleted
        /// BLOB rows. Blocking — call from a background thread.
        /// </summary>
        public void Compact()
        {
            try
            {
                using var chk = _db.CreateCommand();
                chk.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                chk.ExecuteNonQuery();

                using var vac = _db.CreateCommand();
                vac.CommandText = "VACUUM";
                vac.ExecuteNonQuery();
            }
            catch { /* best-effort; don't surface compaction errors to callers */ }
        }

        // ── Dispose ────────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _db.Close();
            _db.Dispose();
        }
    }
}
