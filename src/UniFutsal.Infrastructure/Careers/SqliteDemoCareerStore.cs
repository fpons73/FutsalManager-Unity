using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using UniFutsal.Domain.Competitions;

namespace UniFutsal.Infrastructure.Careers
{
    public sealed class SqliteDemoCareerStore
    {
        public const int MaxClosedSeasons = 10;
        private const int SchemaVersion = 1;
        private const string EngineVersion = "h1-demo-match-v1";

        public DemoCompletedSeason? AdvanceToNewSave(string source, string destination)
        {
            var career = Load(source);
            var next = career.NextSeasonNumber;
            if (next > MaxClosedSeasons)
                return null;

            var advanced = career.CompleteSeason(next);
            SaveNew(destination, advanced);
            return advanced.CompletedSeasons[next - 1];
        }

        public DemoCareer RecoverToNewSave(string backupPath, string destination)
        {
            var recovered = Load(backupPath);
            SaveNew(destination, recovered);
            return recovered;
        }

        public void SaveNew(string path, DemoCareer career)
        {
            var closed = career.CompletedSeasons.Count;
            if (closed > MaxClosedSeasons)
                throw new ArgumentOutOfRangeException(nameof(career), "The demo save limit is ten closed seasons.");

            var destination = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(destination) ?? throw new ArgumentException("A file path is required.", nameof(path));
            var temporary = Path.Combine(directory, $".career-{Guid.NewGuid():N}.tmp");
            var ownsTemporary = false;
            try
            {
                using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    ownsTemporary = true;

                using (var connection = Open(temporary, SqliteOpenMode.ReadWrite))
                using (var transaction = connection.BeginTransaction())
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
                        CREATE TABLE career_save (
                            schema_version INTEGER NOT NULL,
                            engine_version TEXT NOT NULL,
                            seed TEXT NOT NULL,
                            closed_seasons INTEGER NOT NULL,
                            replay_hash TEXT NOT NULL
                        );
                        INSERT INTO career_save VALUES ($schema, $engine, $seed, $closed, $hash);";
                    command.Parameters.AddWithValue("$schema", SchemaVersion);
                    command.Parameters.AddWithValue("$engine", EngineVersion);
                    command.Parameters.AddWithValue("$seed", career.Seed.ToString(CultureInfo.InvariantCulture));
                    command.Parameters.AddWithValue("$closed", closed);
                    command.Parameters.AddWithValue("$hash", Fingerprint(career));
                    command.ExecuteNonQuery();
                    transaction.Commit();
                }

                Load(temporary);
                File.Move(temporary, destination);
                ownsTemporary = false;
            }
            finally
            {
                if (ownsTemporary)
                    File.Delete(temporary);
            }
        }

        public DemoCareer Load(string path)
        {
            var source = Path.GetFullPath(path);
            if (!File.Exists(source))
                throw new FileNotFoundException("The demo save does not exist.", source);

            try
            {
                using var connection = Open(source, SqliteOpenMode.ReadOnly);
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA trusted_schema = OFF;";
                command.ExecuteNonQuery();
                command.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                    throw new InvalidDataException("The demo save failed the SQLite integrity check.");

                command.CommandText = "SELECT schema_version, engine_version, seed, closed_seasons, replay_hash FROM career_save;";
                using var reader = command.ExecuteReader();
                if (!reader.Read() || !(reader.GetValue(0) is long version) || version != SchemaVersion ||
                    !(reader.GetValue(1) is string engine) || engine != EngineVersion ||
                    !(reader.GetValue(2) is string seedText) ||
                    !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ||
                    !(reader.GetValue(3) is long count) || count < 0 || count > MaxClosedSeasons ||
                    !(reader.GetValue(4) is string hash) || reader.Read())
                    throw new InvalidDataException("The demo save metadata is invalid or incompatible.");

                var career = new DemoCareer(seed);
                for (var number = 1; number <= count; number++)
                    career = career.CompleteSeason(number);

                if (hash != Fingerprint(career))
                    throw new InvalidDataException("The demo save replay does not match its recorded fingerprint.");
                return career;
            }
            catch (SqliteException exception)
            {
                throw new InvalidDataException("The demo save could not be read as a compatible SQLite database.", exception);
            }
        }

        private static SqliteConnection Open(string path, SqliteOpenMode mode)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Mode = mode, Pooling = false }.ToString());
            try
            {
                connection.Open();
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static string Fingerprint(DemoCareer career)
        {
            using var data = new MemoryStream();
            using (var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(career.Seed);
                writer.Write(career.CompletedSeasons.Count);
                foreach (var season in career.CompletedSeasons)
                {
                    writer.Write(season.Number);
                    writer.Write(season.Seed);
                    writer.Write(season.Result.Results.Count);
                    foreach (var match in season.Result.Results)
                    {
                        writer.Write(match.Fixture.Round);
                        writer.Write(match.Fixture.HomeClub);
                        writer.Write(match.Fixture.AwayClub);
                        writer.Write(match.HomeGoals);
                        writer.Write(match.AwayGoals);
                    }
                    writer.Write(season.Result.Standings.Count);
                    foreach (var row in season.Result.Standings)
                    {
                        writer.Write(row.Club);
                        writer.Write(row.Wins);
                        writer.Write(row.Draws);
                        writer.Write(row.Losses);
                        writer.Write(row.GoalsFor);
                        writer.Write(row.GoalsAgainst);
                    }
                }
            }

            data.Position = 0;
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(data));
        }
    }
}
