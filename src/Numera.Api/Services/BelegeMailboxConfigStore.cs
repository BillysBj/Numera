using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using Numera.Platform.Db;

namespace Numera.Api.Services;

/// <summary>
/// The effective configuration of the shared receipt mailbox (one IMAP inbox + catch-all domain
/// operated by the platform; each tenant gets a <c>belege-{token}@domain</c> alias). The password
/// is stored encrypted (<see cref="PasswordCipher"/>); callers decrypt via
/// <see cref="BelegeMailboxPasswordProtector"/> only when they actually need to connect.
/// </summary>
public sealed record BelegeMailboxConfig(
    string Host,
    int Port,
    bool UseSsl,
    string Username,
    string? PasswordCipher,
    string Domain,
    bool IsActive,
    DateTimeOffset? UpdatedAt);

/// <summary>DataProtection purpose for the shared IMAP password; never log its inputs/outputs.</summary>
public sealed class BelegeMailboxPasswordProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("PlatformBelegeMailbox.ImapPassword");

    /// <summary>Encrypts a plaintext IMAP password for storage.</summary>
    public string Protect(string password) => _protector.Protect(password);

    /// <summary>Decrypts a stored IMAP password ciphertext.</summary>
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}

/// <summary>
/// Reads and writes the single <c>platform_belege_mailbox</c> row. The table is tenant-independent
/// (the mailbox is shared across tenants), carries NO RLS and is deliberately NOT mapped in
/// <see cref="NumeraDbContext"/> (so the per-tenant export never leaks it) — access is raw SQL on
/// the app connection, which holds the default DML grant. A single row is enforced by a boolean
/// primary key.
/// </summary>
public sealed class BelegeMailboxConfigStore(NumeraDbContext db)
{
    private readonly NumeraDbContext _db = db;

    /// <summary>Returns the stored configuration, or null if the operator has not configured it.</summary>
    public async Task<BelegeMailboxConfig?> GetAsync(CancellationToken ct)
    {
        await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "SELECT host, port, use_ssl, username, password_cipher, domain, is_active, updated_at " +
                "FROM platform_belege_mailbox LIMIT 1";
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            return new BelegeMailboxConfig(
                Host: reader.GetString(0),
                Port: reader.GetInt32(1),
                UseSsl: reader.GetBoolean(2),
                Username: reader.GetString(3),
                PasswordCipher: await reader.IsDBNullAsync(4, ct).ConfigureAwait(false) ? null : reader.GetString(4),
                Domain: reader.GetString(5),
                IsActive: reader.GetBoolean(6),
                UpdatedAt: await reader.IsDBNullAsync(7, ct).ConfigureAwait(false)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(7));
        }
        finally
        {
            await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Upserts the single configuration row. <paramref name="passwordCipher"/> is written as-is
    /// (already encrypted); pass null to keep the existing stored password.
    /// </summary>
    public async Task UpsertAsync(
        string host,
        int port,
        bool useSsl,
        string username,
        string? passwordCipher,
        string domain,
        bool isActive,
        CancellationToken ct)
    {
        await _db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            await using var command = _db.Database.GetDbConnection().CreateCommand();
            command.CommandText =
                "INSERT INTO platform_belege_mailbox " +
                "(singleton, host, port, use_ssl, username, password_cipher, domain, is_active, updated_at) " +
                "VALUES (true, @host, @port, @use_ssl, @username, @password_cipher, @domain, @is_active, now()) " +
                "ON CONFLICT (singleton) DO UPDATE SET " +
                "host = EXCLUDED.host, port = EXCLUDED.port, use_ssl = EXCLUDED.use_ssl, " +
                "username = EXCLUDED.username, domain = EXCLUDED.domain, is_active = EXCLUDED.is_active, " +
                "updated_at = now(), " +
                // Keep the existing password when the caller supplies null (no change submitted).
                "password_cipher = COALESCE(EXCLUDED.password_cipher, platform_belege_mailbox.password_cipher)";
            command.Parameters.Add(new NpgsqlParameter<string>("host", host));
            command.Parameters.Add(new NpgsqlParameter<int>("port", port));
            command.Parameters.Add(new NpgsqlParameter<bool>("use_ssl", useSsl));
            command.Parameters.Add(new NpgsqlParameter<string>("username", username));
            command.Parameters.Add(new NpgsqlParameter("password_cipher", (object?)passwordCipher ?? DBNull.Value));
            command.Parameters.Add(new NpgsqlParameter<string>("domain", domain));
            command.Parameters.Add(new NpgsqlParameter<bool>("is_active", isActive));
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
