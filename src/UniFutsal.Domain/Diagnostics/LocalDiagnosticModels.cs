using System;

namespace UniFutsal.Domain.Diagnostics
{
    public enum LocalDiagnosticStatus
    {
        Available = 1,
        Unavailable = 2
    }

    public sealed class DiagnosticRecord
    {
        public string Id { get; }
        public string EngineVersion { get; }
        public string RecordedUtcIso { get; }
        public string Note { get; }

        public DiagnosticRecord(string id, string engineVersion, string recordedUtcIso, string note)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Id is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(engineVersion)) throw new ArgumentException("EngineVersion is required.", nameof(engineVersion));
            if (string.IsNullOrWhiteSpace(recordedUtcIso)) throw new ArgumentException("RecordedUtcIso is required.", nameof(recordedUtcIso));

            Id = id;
            EngineVersion = engineVersion;
            RecordedUtcIso = recordedUtcIso;
            Note = note ?? string.Empty;
        }
    }

    public sealed class LocalDiagnosticResult
    {
        public LocalDiagnosticStatus Status { get; }
        public DiagnosticRecord? Record { get; }
        public string ErrorCode { get; }
        public bool IsAvailable => Status == LocalDiagnosticStatus.Available;

        private LocalDiagnosticResult(LocalDiagnosticStatus status, DiagnosticRecord? record, string errorCode)
        {
            Status = status;
            Record = record;
            ErrorCode = errorCode ?? string.Empty;
        }

        public static LocalDiagnosticResult Success(DiagnosticRecord record)
        {
            return new LocalDiagnosticResult(LocalDiagnosticStatus.Available, record, string.Empty);
        }

        public static LocalDiagnosticResult Failure(string errorCode)
        {
            return new LocalDiagnosticResult(LocalDiagnosticStatus.Unavailable, null, errorCode);
        }
    }
}
