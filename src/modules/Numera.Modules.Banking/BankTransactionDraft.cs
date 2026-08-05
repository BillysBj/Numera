namespace Numera.Modules.Banking;

/// <summary>Provider/import-neutral bank transaction before deduplication and persistence.</summary>
public sealed record BankTransactionDraft(
    string? ProviderId,
    decimal Amount,
    DateOnly ValueDate,
    DateOnly? BookingDate,
    string? Purpose,
    string? CounterpartyName,
    string? CounterpartyIban,
    string? EndToEndId,
    BankTransactionSource Source);
