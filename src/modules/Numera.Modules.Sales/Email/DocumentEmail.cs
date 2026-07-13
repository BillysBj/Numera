using System.ComponentModel.DataAnnotations.Schema;

using Microsoft.EntityFrameworkCore;

using Numera.Platform.Db;

namespace Numera.Modules.Sales.Email;

/// <summary>
/// A send record for e-mailing a finalized <see cref="SalesDocument"/>'s rendered PDF to the
/// customer (Phase-4 DOCS-03). The tenant-visible status of a dispatch: to whom, current
/// <see cref="EmailStatus"/>, how many attempts, the last error, and when it was sent.
/// Hangfire's built-in retry handles transient SMTP faults; the send job updates this row and
/// flips <c>SalesDocument.SentAt</c> (a whitelisted lifecycle column) on first success
/// (RESEARCH.md Q5).
/// </summary>
/// <remarks>
/// <see cref="ITenantEntity"/> gives it a DB RLS policy (hand-written in the migration) + the
/// tenant query filter; the entity self-describes via attributes so <c>NumeraDbContext</c> is
/// not edited. <see cref="DocumentId"/> is a plain Guid FK to <c>sales_documents.id</c> — no
/// navigation, persist via <c>db.Add(...)</c>.
/// </remarks>
[Table("document_email")]
[Index(nameof(TenantId), nameof(DocumentId))]
public sealed class DocumentEmail : ITenantEntity
{
    /// <summary>UUIDv7 primary key (time-ordered; maps to PG18 <c>uuidv7()</c>).</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <inheritdoc />
    public Guid TenantId { get; init; }

    /// <summary>The finalized document being sent (FK → sales_documents.id, provenance only).</summary>
    public Guid DocumentId { get; set; }

    /// <summary>The recipient e-mail address the PDF is dispatched to. Required.</summary>
    public required string ToAddress { get; set; }

    /// <summary>The e-mail subject (bilingual, resolved by document language). Nullable.</summary>
    public string? Subject { get; set; }

    /// <summary>Current send state.</summary>
    public EmailStatus Status { get; set; } = EmailStatus.Queued;

    /// <summary>Number of send attempts (incremented per Hangfire retry).</summary>
    public int AttemptCount { get; set; }

    /// <summary>The last SMTP/error message on a failed attempt. Nullable.</summary>
    public string? LastError { get; set; }

    /// <summary>When the message was successfully sent. Nullable until <see cref="EmailStatus.Sent"/>.</summary>
    public DateTimeOffset? SentAt { get; set; }

    /// <summary>When the send record was created (enqueued).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
