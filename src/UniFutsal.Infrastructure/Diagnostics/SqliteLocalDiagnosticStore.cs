using System;
using System.IO;
using Microsoft.Data.Sqlite;
using UniFutsal.Application.Diagnostics;
using UniFutsal.Domain.Diagnostics;

namespace UniFutsal.Infrastructure.Diagnostics
{
    public sealed class SqliteLocalDiagnosticStore : ILocalDiagnosticStore
    {
        private readonly string _databasePath;
        private readonly string _connectionString;

        public SqliteLocalDiagnosticStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Database path is required.", nameof(databasePath));

            _databasePath = databasePath;
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();
        }

        public void Save(DiagnosticRecord record)
        {
            EnsureParentDirectoryExists();

            using (var connection = OpenConnection())
            {
                EnsureSchema(connection);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT OR REPLACE INTO diagnostic_records (id, engine_version, recorded_utc, note)
                        VALUES ($id, $engineVersion, $recordedUtc, $note);";
                    command.Parameters.AddWithValue("$id", record.Id);
                    command.Parameters.AddWithValue("$engineVersion", record.EngineVersion);
                    command.Parameters.AddWithValue("$recordedUtc", record.RecordedUtcIso);
                    command.Parameters.AddWithValue("$note", record.Note);
                    command.ExecuteNonQuery();
                }
            }
        }

        public DiagnosticRecord? LoadById(string id)
        {
            using (var connection = OpenConnection())
            {
                EnsureSchema(connection);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT id, engine_version, recorded_utc, note
                        FROM diagnostic_records
                        WHERE id = $id;";
                    command.Parameters.AddWithValue("$id", id);

                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            return null;
                        }

                        return new DiagnosticRecord(
                            id: reader.GetString(0),
                            engineVersion: reader.GetString(1),
                            recordedUtcIso: reader.GetString(2),
                            note: reader.GetString(3));
                    }
                }
            }
        }

        public bool VerifyIntegrity()
        {
            using (var connection = OpenConnection())
            {
                using (var fkCmd = connection.CreateCommand())
                {
                    fkCmd.CommandText = "PRAGMA foreign_keys;";
                    var fkValue = Convert.ToInt64(fkCmd.ExecuteScalar());
                    if (fkValue != 1)
                    {
                        return false;
                    }
                }

                using (var integrityCmd = connection.CreateCommand())
                {
                    integrityCmd.CommandText = "PRAGMA integrity_check;";
                    var result = integrityCmd.ExecuteScalar() as string;
                    return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void EnsureParentDirectoryExists()
        {
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA foreign_keys = ON;";
                pragma.ExecuteNonQuery();
            }

            return connection;
        }

        private static void EnsureSchema(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS diagnostic_records (
                        id TEXT PRIMARY KEY,
                        engine_version TEXT NOT NULL,
                        recorded_utc TEXT NOT NULL,
                        note TEXT NOT NULL
                    );";
                command.ExecuteNonQuery();
            }
        }
    }
}
