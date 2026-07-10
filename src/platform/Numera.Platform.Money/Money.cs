using NodaMoney;

namespace Numera.Platform.Money;

/// <summary>
/// A decimal-backed monetary value: an exact <see cref="decimal"/> amount plus
/// an ISO-4217 currency. This is the single money primitive for the whole
/// system — no <c>float</c>/<c>double</c> ever touches a monetary quantity.
/// </summary>
/// <remarks>
/// <para>
/// Storage note for later phases (no schema is created here): monetary amounts
/// map to Postgres <c>numeric(19,4)</c> and unit prices to <c>numeric(19,6)</c>.
/// The in-memory representation stays a raw <see cref="decimal"/> so it maps
/// losslessly onto those column types.
/// </para>
/// <para>
/// Currency identity is validated through NodaMoney's ISO-4217 registry, giving
/// minor-unit awareness for display/formatting, while the arithmetic itself
/// stays on raw <see cref="decimal"/> for exactness and control over rounding.
/// </para>
/// </remarks>
public readonly record struct Money
{
    /// <summary>The exact monetary amount. Never a binary floating-point type.</summary>
    public decimal Amount { get; }

    /// <summary>The ISO-4217 alphabetic currency code (e.g. <c>EUR</c>).</summary>
    public string CurrencyCode { get; }

    /// <summary>
    /// Creates a money value. Currency defaults to <c>EUR</c> and is validated
    /// against the ISO-4217 registry.
    /// </summary>
    /// <param name="amount">The exact decimal amount.</param>
    /// <param name="currencyCode">ISO-4217 alphabetic code; defaults to <c>EUR</c>.</param>
    /// <exception cref="ArgumentException">The currency code is not a known ISO-4217 currency.</exception>
    public Money(decimal amount, string currencyCode = "EUR")
    {
        // Validate through NodaMoney's registry (throws for unknown codes) but
        // keep the canonical uppercase code as our own state.
        var currency = Currency.FromCode(currencyCode);
        Amount = amount;
        CurrencyCode = currency.Code;
    }

    /// <summary>Adds two amounts of the same currency.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.CurrencyCode);
    }

    /// <summary>Subtracts two amounts of the same currency.</summary>
    /// <exception cref="InvalidOperationException">The currencies differ.</exception>
    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.CurrencyCode);
    }

    /// <summary>Scales an amount by a decimal factor (e.g. a quantity or rate).</summary>
    public static Money operator *(Money value, decimal factor)
        => new(value.Amount * factor, value.CurrencyCode);

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.CurrencyCode, right.CurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot combine money in different currencies: '{left.CurrencyCode}' and '{right.CurrencyCode}'.");
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{Amount} {CurrencyCode}";
}
