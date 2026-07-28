namespace Numera.Modules.Crm;

/// <summary>
/// Document/communication language for a <see cref="BusinessPartner"/>. Drives the
/// language of future generated documents (invoices, dunning) — v1 supports the two
/// shipped UI locales.
/// </summary>
public enum PartnerLanguage
{
    /// <summary>German (Deutsch) — the default.</summary>
    De = 1,

    /// <summary>English.</summary>
    En = 2,
}

/// <summary>
/// The kind of event recorded on a partner's activity timeline
/// (<see cref="PartnerActivity"/>). Append-by-convention; no updates.
/// </summary>
public enum PartnerActivityType
{
    /// <summary>The partner master record was created.</summary>
    PartnerCreated = 1,

    /// <summary>The partner master record was updated.</summary>
    PartnerUpdated = 2,

    /// <summary>The partner was archived (soft-deleted).</summary>
    Archived = 3,

    /// <summary>A previously archived partner was reactivated.</summary>
    Unarchived = 4,

    /// <summary>A note was added to the partner.</summary>
    NoteAdded = 5,
}

/// <summary>Completion state of a partner task.</summary>
public enum PartnerTaskStatus
{
    /// <summary>The task still requires action.</summary>
    Open = 0,

    /// <summary>The task has been completed.</summary>
    Done = 1,
}
