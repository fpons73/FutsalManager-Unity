using System;
using System.Collections.Generic;
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
        private const int RoundCheckpointSchemaVersion = 1;
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

            PublishNew(path, command =>
            {
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
            }, temporary => Load(temporary));
        }

        public void SaveRoundCheckpointNew(string path, DemoRoundSeason season)
        {
            PublishNew(path, command =>
            {
                command.CommandText = @"
                        CREATE TABLE round_checkpoint (
                            schema_version INTEGER NOT NULL,
                            engine_version TEXT NOT NULL,
                            seed TEXT NOT NULL,
                            completed_rounds INTEGER NOT NULL,
                            replay_hash TEXT NOT NULL
                        );
                        INSERT INTO round_checkpoint VALUES ($schema, $engine, $seed, $rounds, $hash);";
                command.Parameters.AddWithValue("$schema", RoundCheckpointSchemaVersion);
                command.Parameters.AddWithValue("$engine", EngineVersion);
                command.Parameters.AddWithValue("$seed", season.Seed.ToString(CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$rounds", season.CompletedRounds);
                command.Parameters.AddWithValue("$hash", Fingerprint(season));
            }, temporary => LoadRoundCheckpoint(temporary));
        }

        public DemoCareer Load(string path) => ReadVerified(path,
            "SELECT schema_version, engine_version, seed, closed_seasons, replay_hash FROM career_save;", reader =>
            {
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
            });

        public DemoRoundSeason LoadRoundCheckpoint(string path) => ReadVerified(path,
            "SELECT schema_version, engine_version, seed, completed_rounds, replay_hash FROM round_checkpoint;", reader =>
            {
                if (!reader.Read() || !(reader.GetValue(0) is long version) || version != RoundCheckpointSchemaVersion ||
                    !(reader.GetValue(1) is string engine) || engine != EngineVersion ||
                    !(reader.GetValue(2) is string seedText) ||
                    !ulong.TryParse(seedText, NumberStyles.None, CultureInfo.InvariantCulture, out var seed) ||
                    !(reader.GetValue(3) is long count) || count < 0 || count > int.MaxValue ||
                    !(reader.GetValue(4) is string hash) || reader.Read())
                    throw new InvalidDataException("The round checkpoint metadata is invalid or incompatible.");

                DemoRoundSeason season;
                try
                {
                    season = DemoRoundSeason.Resume(seed, (int)count);
                }
                catch (ArgumentOutOfRangeException exception)
                {
                    throw new InvalidDataException("The checkpoint is outside the demo calendar.", exception);
                }

                if (hash != Fingerprint(season))
                    throw new InvalidDataException("The round checkpoint replay does not match its recorded fingerprint.");
                return season;
            });

        private static void PublishNew(string path, Action<SqliteCommand> configure, Action<string> verify)
        {
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
                    configure(command);
                    command.ExecuteNonQuery();
                    transaction.Commit();
                }

                verify(temporary);
                File.Move(temporary, destination);
                ownsTemporary = false;
            }
            finally
            {
                if (ownsTemporary)
                    File.Delete(temporary);
            }
        }

        private static T ReadVerified<T>(string path, string query, Func<SqliteDataReader, T> read)
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

                command.CommandText = query;
                using var reader = command.ExecuteReader();
                return read(reader);
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

        private static string Fingerprint(DemoCareer career) => ComputeFingerprint(writer =>
        {
            writer.Write(career.Seed);
            writer.Write(career.CompletedSeasons.Count);
            foreach (var season in career.CompletedSeasons)
            {
                writer.Write(season.Number);
                writer.Write(season.Seed);
                WriteResults(writer, season.Result.Results, season.Result.Standings);
            }
        });

        private static string Fingerprint(DemoRoundSeason season) => ComputeFingerprint(writer =>
        {
            writer.Write(season.Seed);
            writer.Write(season.CompletedRounds);
            WriteResults(writer, season.Results, season.Standings);
        });

        private static void WriteResults(BinaryWriter writer, IReadOnlyList<DemoFixtureResult> results,
            IReadOnlyList<DemoStanding> standings)
        {
            writer.Write(results.Count);
            foreach (var match in results)
            {
                writer.Write(match.Fixture.Round);
                writer.Write(match.Fixture.HomeClub);
                writer.Write(match.Fixture.AwayClub);
                writer.Write(match.HomeGoals);
                writer.Write(match.AwayGoals);
            }
            writer.Write(standings.Count);
            foreach (var row in standings)
            {
                writer.Write(row.Club);
                writer.Write(row.Wins);
                writer.Write(row.Draws);
                writer.Write(row.Losses);
                writer.Write(row.GoalsFor);
                writer.Write(row.GoalsAgainst);
            }
        }

        private static string ComputeFingerprint(Action<BinaryWriter> write)
        {
            using var data = new MemoryStream();
            using (var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true))
                write(writer);
            data.Position = 0;
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(data));
        }
    }
}
