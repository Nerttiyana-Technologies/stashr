using Stashr.Core.Model;

namespace Stashr.Core.Audit;

/// <summary>
/// A destination for audit events (file, syslog, database) — ADR-0005. Multiple sinks may be
/// active. By default the engine is fail-closed: if a required sink cannot record, the request
/// does not complete.
/// </summary>
public interface IAuditSink
{
    /// <summary>True if a failure to write to this sink must fail the request (fail-closed).</summary>
    bool Required { get; }

    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
}
