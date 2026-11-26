namespace Archiva.Infrastructure.Data;

public sealed class GoogleSession
{
    public string Key { get; init; } = string.Empty;
    public byte[] Ticket { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
}
