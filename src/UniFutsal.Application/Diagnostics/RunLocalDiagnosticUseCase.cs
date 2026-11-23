using System;
using UniFutsal.Domain.Diagnostics;

namespace UniFutsal.Application.Diagnostics
{
    public interface ILocalDiagnosticStore
    {
        void Save(DiagnosticRecord record);
        DiagnosticRecord? LoadById(string id);
        bool VerifyIntegrity();
    }

    public sealed class RunLocalDiagnosticUseCase
    {
        private readonly ILocalDiagnosticStore _store;
        private readonly Func<DateTime> _utcClock;
        private readonly Func<string> _idGenerator;

        public RunLocalDiagnosticUseCase(
            ILocalDiagnosticStore store,
            Func<DateTime> utcClock,
            Func<string> idGenerator)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _utcClock = utcClock ?? throw new ArgumentNullException(nameof(utcClock));
            _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        }

        public LocalDiagnosticResult Execute(string engineVersion)
        {
            try
            {
                var record = new DiagnosticRecord(
                    id: _idGenerator(),
                    engineVersion: engineVersion,
                    recordedUtcIso: _utcClock().ToString("O"),
                    note: "h0_local_storage_check");

                _store.Save(record);
                var loaded = _store.LoadById(record.Id);

                if (loaded == null ||
                    loaded.Id != record.Id ||
                    loaded.EngineVersion != record.EngineVersion ||
                    loaded.RecordedUtcIso != record.RecordedUtcIso ||
                    loaded.Note != record.Note)
                {
                    return LocalDiagnosticResult.Failure("DIAGNOSTIC_MISMATCH");
                }

                if (!_store.VerifyIntegrity())
                {
                    return LocalDiagnosticResult.Failure("DIAGNOSTIC_INTEGRITY_FAILED");
                }

                return LocalDiagnosticResult.Success(loaded);
            }
            catch (Exception)
            {
                return LocalDiagnosticResult.Failure("DIAGNOSTIC_STORAGE_ERROR");
            }
        }
    }
}
