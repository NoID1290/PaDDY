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
        public bool IsNonDestructive { get; set; }
        public long TrimStartMs { get; set; }
        public long TrimEndMs { get; set; }
        public double GainDb { get; set; }
        public string PadColor { get; set; } = string.Empty;

        /// <summary>Id of the pad page this recording is pinned to (empty = unassigned).</summary>
        public string PadPage { get; set; } = string.Empty;

        /// <summary>Manual sort position within its panel/page (lower = earlier).</summary>
        public long SortOrder { get; set; }

        public double? LufsValue { get; set; }
        public string Transcription { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
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
            _db = new SqliteConnection($"Data Source={StorePath};Pooling=False");
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

            EnsurePadPageColumn();
            EnsureSortOrderColumn();
            EnsureNonDestructiveColumns();
            EnsurePadColorColumn();
            EnsureLufsAndSpeechColumns();
        }

        /// <summary>Adds the pad_page column to older databases that predate pad pages.</summary>
        private void EnsurePadPageColumn()
        {
            bool exists = false;
            using (var info = _db.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(recordings)";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "pad_page", StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (exists) return;

            using var alter = _db.CreateCommand();
            alter.CommandText = "ALTER TABLE recordings ADD COLUMN pad_page TEXT NOT NULL DEFAULT ''";
            alter.ExecuteNonQuery();
        }

        /// <summary>Adds the sort_order column to databases that predate manual ordering.</summary>
        private void EnsureSortOrderColumn()
        {
            bool exists = false;
            using (var info = _db.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(recordings)";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "sort_order", StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (exists) return;

            using var alter = _db.CreateCommand();
            alter.CommandText = "ALTER TABLE recordings ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0";
            alter.ExecuteNonQuery();
        }

        private void EnsureNonDestructiveColumns()
        {
            var cols = new List<string>();
            using (var info = _db.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(recordings)";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                {
                    cols.Add(reader.GetString(1).ToLowerInvariant());
                }
            }

            if (!cols.Contains("is_non_destructive"))
            {
                using var alter = _db.CreateCommand();
                alter.CommandText = "ALTER TABLE recordings ADD COLUMN is_non_destructive INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
            if (!cols.Contains("trim_start_ms"))
            {
                using var alter = _db.CreateCommand();
                alter.CommandText = "ALTER TABLE recordings ADD COLUMN trim_start_ms INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
            if (!cols.Contains("trim_end_ms"))
            {
                using var alter = _db.CreateCommand();
                alter.CommandText = "ALTER TABLE recordings ADD COLUMN trim_end_ms INTEGER NOT NULL DEFAULT 0";
                alter.ExecuteNonQuery();
            }
            if (!cols.Contains("gain_db"))
            {
                using var alter = _db.CreateCommand();
                alter.CommandText = "ALTER TABLE recordings ADD COLUMN gain_db REAL NOT NULL DEFAULT 0.0";
                alter.ExecuteNonQuery();
            }
        }

        private void EnsurePadColorColumn()
        {
            bool exists = false;
            using (var info = _db.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(recordings)";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), "pad_color", StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (exists) return;

            using var alter = _db.CreateCommand();
            alter.CommandText = "ALTER TABLE recordings ADD COLUMN pad_color TEXT NOT NULL DEFAULT ''";
            alter.ExecuteNonQuery();
        }

        private void EnsureLufsAndSpeechColumns()
        {
            var cols = new List<string>();
            using (var info = _db.CreateCommand())
            {
                info.CommandText = "PRAGMA table_info(recordings)";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                {
                    cols.Add(reader.GetString(1).ToLowerInvariant());
                }
            }

            if (!cols.Contains("lufs_value"))
            {
                using var alter = _db.CreateCommand();
                alter.CommandText = "ALTER TABLE recordings ADD COLUMN lufs_value REAL NULL";
                alter.ExecuteNonQuery();
            }
            if (!cols.Contains("transcription"))
            {
                using var alter = _db.CreateCommand();
                alter.CommandText = "ALTER TABLE recordings ADD COLUMN transcription TEXT NOT NULL DEFAULT ''";
                alter.ExecuteNonQuery();
            }
            if (!cols.Contains("tags"))
            {
                using var alter = _db.CreateCommand();
                alter.CommandText = "ALTER TABLE recordings ADD COLUMN tags TEXT NOT NULL DEFAULT ''";
                alter.ExecuteNonQuery();
            }
        }

        public void UpdateLufs(string id, double lufs)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET lufs_value=@lufs WHERE id=@id";
            cmd.Parameters.AddWithValue("@lufs", lufs);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void UpdateTranscription(string id, string transcription, string tags)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET transcription=@tr, tags=@tg WHERE id=@id";
            cmd.Parameters.AddWithValue("@tr", transcription ?? string.Empty);
            cmd.Parameters.AddWithValue("@tg", tags ?? string.Empty);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ── Write operations ───────────────────────────────────────────────────

        public string Add(string displayName, string codec, TimeSpan duration, DateTime createdAt, byte[] audioData, bool isNonDestructive = false, long trimStartMs = 0, long trimEndMs = 0, double gainDb = 0.0)
        {
            string id = Guid.NewGuid().ToString("N");
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO recordings(id, display_name, codec, duration_ms, created_at, is_favorite, audio_data, is_non_destructive, trim_start_ms, trim_end_ms, gain_db)
                VALUES(@id, @dn, @codec, @dur, @cat, 0, @data, @nd, @tstart, @tend, @gain)
                """;
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@dn", displayName);
            cmd.Parameters.AddWithValue("@codec", codec);
            cmd.Parameters.AddWithValue("@dur", (long)duration.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@cat", createdAt.ToString("O"));
            cmd.Parameters.AddWithValue("@data", audioData);
            cmd.Parameters.AddWithValue("@nd", isNonDestructive ? 1L : 0L);
            cmd.Parameters.AddWithValue("@tstart", trimStartMs);
            cmd.Parameters.AddWithValue("@tend", trimEndMs);
            cmd.Parameters.AddWithValue("@gain", gainDb);
            cmd.ExecuteNonQuery();
            return id;
        }

        public void UpdateNonDestructiveSettings(string id, bool isNonDestructive, long trimStartMs, long trimEndMs, double gainDb, long durationMs)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                UPDATE recordings 
                SET is_non_destructive = @nd, trim_start_ms = @tstart, trim_end_ms = @tend, gain_db = @gain, duration_ms = @dur
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@nd", isNonDestructive ? 1L : 0L);
            cmd.Parameters.AddWithValue("@tstart", trimStartMs);
            cmd.Parameters.AddWithValue("@tend", trimEndMs);
            cmd.Parameters.AddWithValue("@gain", gainDb);
            cmd.Parameters.AddWithValue("@dur", durationMs);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void UpdateDuration(string id, TimeSpan duration)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET duration_ms = @dur WHERE id = @id";
            cmd.Parameters.AddWithValue("@dur", (long)duration.TotalMilliseconds);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
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

        public void SetPadColor(string id, string hexColor)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET pad_color=@pc WHERE id=@id";
            cmd.Parameters.AddWithValue("@pc", hexColor ?? string.Empty);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Assigns the recording to a pad page (empty string clears the assignment).</summary>
        public void SetPadPage(string id, string padPageId)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET pad_page=@pp WHERE id=@id";
            cmd.Parameters.AddWithValue("@pp", padPageId ?? string.Empty);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Clears the pad-page assignment for all recordings pinned to a deleted page.</summary>
        public void ClearPadPage(string padPageId)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET pad_page='' WHERE pad_page=@pp";
            cmd.Parameters.AddWithValue("@pp", padPageId);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Sets the manual sort position for a single recording.</summary>
        public void SetSortOrder(string id, long order)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE recordings SET sort_order=@so WHERE id=@id";
            cmd.Parameters.AddWithValue("@so", order);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Persists manual order for a list of recording ids (index becomes sort_order).</summary>
        public void SetSortOrders(IReadOnlyList<string> orderedIds)
        {
            using var tx = _db.BeginTransaction();
            for (int i = 0; i < orderedIds.Count; i++)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = "UPDATE recordings SET sort_order=@so WHERE id=@id";
                cmd.Parameters.AddWithValue("@so", (long)i);
                cmd.Parameters.AddWithValue("@id", orderedIds[i]);
                cmd.Transaction = tx;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
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
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM recordings WHERE id=@id";
            var param = cmd.Parameters.Add("@id", SqliteType.Text);

            foreach (var id in ids)
            {
                CleanupTempFile(id);
                param.Value = id;
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
                SELECT id, display_name, codec, duration_ms, created_at, is_favorite, pad_page, sort_order, is_non_destructive, trim_start_ms, trim_end_ms, gain_db, pad_color, lufs_value, transcription, tags
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
                    IsFavorite = reader.GetInt64(5) != 0,
                    PadPage = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    SortOrder = reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    IsNonDestructive = reader.IsDBNull(8) ? false : (reader.GetInt64(8) != 0),
                    TrimStartMs = reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
                    TrimEndMs = reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
                    GainDb = reader.IsDBNull(11) ? 0.0 : reader.GetDouble(11),
                    PadColor = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                    LufsValue = reader.IsDBNull(13) ? null : reader.GetDouble(13),
                    Transcription = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                    Tags = reader.IsDBNull(15) ? string.Empty : reader.GetString(15)
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

        public int GetCount()
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM recordings";
            return Convert.ToInt32(cmd.ExecuteScalar());
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
