using System.Security.Cryptography;
using System.Text;
using Archiva.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Web.Authentication;

internal sealed class GoogleTicketStore(
    IServiceScopeFactory scopes,
    IDataProtectionProvider protection,
    TimeProvider clock
) : ITicketStore
{
    private readonly IDataProtector _protector = protection.CreateProtector(
        "Archiva.GoogleTicket.v1"
    );

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db
            .GoogleSessions.Where(session => session.ExpiresAt <= clock.GetUtcNow())
            .ExecuteDeleteAsync();
        db.GoogleSessions.Add(
            new GoogleSession
            {
                Key = Hash(key),
                Ticket = _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
                ExpiresAt = ticket.Properties.ExpiresUtc!.Value,
            }
        );
        await db.SaveChangesAsync();
        return key;
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = clock.GetUtcNow();
        var hash = Hash(key);
        var session = await db
            .GoogleSessions.AsNoTracking()
            .SingleOrDefaultAsync(session => session.Key == hash && session.ExpiresAt > now);
        if (session is null)
            return null;

        try
        {
            return TicketSerializer.Default.Deserialize(_protector.Unprotect(session.Ticket));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hash = Hash(key);
        var bytes = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
        var expiresAt = ticket.Properties.ExpiresUtc!.Value;
        var now = clock.GetUtcNow();
        // A concurrent logout or expired session must never be recreated.
        await db
            .GoogleSessions.Where(session => session.Key == hash && session.ExpiresAt > now)
            .ExecuteUpdateAsync(fields =>
                fields.SetProperty(s => s.Ticket, bytes).SetProperty(s => s.ExpiresAt, expiresAt)
            );
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hash = Hash(key);
        await db.GoogleSessions.Where(session => session.Key == hash).ExecuteDeleteAsync();
    }

    private static string Hash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
