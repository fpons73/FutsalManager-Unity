using System;
using System.IO;
using UniFutsal.Application.Diagnostics;
using UniFutsal.Domain.Diagnostics;
using UniFutsal.Infrastructure.Diagnostics;
using Xunit;

namespace UniFutsal.Tests
{
    public sealed class LocalDiagnosticTests : IDisposable
    {
        private readonly string _tempDir;

        public LocalDiagnosticTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "UniFutsalTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public void Execute_WithValidSqliteDatabase_SavesLoadsAndReturnsAvailable()
        {
            var dbPath = Path.Combine(_tempDir, "diagnostic.db");
            var fixedTime = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
            var store = new SqliteLocalDiagnosticStore(dbPath);
            var useCase = new RunLocalDiagnosticUseCase(store, () => fixedTime, () => "diag-001");

            var result = useCase.Execute("0.1.0-h0");

            Assert.True(result.IsAvailable);
            Assert.Equal(LocalDiagnosticStatus.Available, result.Status);
            Assert.NotNull(result.Record);
            Assert.Equal("diag-001", result.Record.Id);
            Assert.Equal("0.1.0-h0", result.Record.EngineVersion);
            Assert.Equal(fixedTime.ToString("O"), result.Record.RecordedUtcIso);
            Assert.True(File.Exists(dbPath));
        }

        [Fact]
        public void VerifyIntegrity_OnCreatedDatabase_ReturnsTrue()
        {
            var dbPath = Path.Combine(_tempDir, "integrity.db");
            var store = new SqliteLocalDiagnosticStore(dbPath);
            store.Save(new DiagnosticRecord("diag-002", "0.1.0-h0", "2026-11-22T12:00:00Z", "ok"));

            var isHealthy = store.VerifyIntegrity();

            Assert.True(isHealthy);
        }

        [Fact]
        public void Execute_WhenStoragePathIsADirectory_ReturnsUnavailableWithoutThrowing()
        {
            var store = new SqliteLocalDiagnosticStore(_tempDir);
            var useCase = new RunLocalDiagnosticUseCase(store, () => DateTime.UtcNow, () => "diag-fail");

            var result = useCase.Execute("0.1.0-h0");

            Assert.False(result.IsAvailable);
            Assert.Equal(LocalDiagnosticStatus.Unavailable, result.Status);
            Assert.Equal("DIAGNOSTIC_STORAGE_ERROR", result.ErrorCode);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
    }
}
