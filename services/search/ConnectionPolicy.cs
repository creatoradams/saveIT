using System.Net;
using Npgsql;

namespace SaveIT.Search;

public static class ConnectionPolicy
{
    public static string RuntimeConnection(string connection)
    {
        var c = new NpgsqlConnectionStringBuilder(connection);
        if (c.Options is not (null or "" or " ")) throw new InvalidOperationException("Arbitrary database startup options are unsupported.");
        // Null falls back to PGOPTIONS; an empty string round-trips back to null in
        // Npgsql. One explicit whitespace character supplies no GUCs and survives
        // connection-string serialization, suppressing ambient role/session settings.
        c.Options = " ";
        RejectRequiredGss(c);
        // Npgsql attempts GSS before TLS and may return before VerifyFull is reached.
        // SaveIT supports certificate TLS, not an alternative ambient GSS transport.
        c.GssEncryptionMode = GssEncryptionMode.Disable;
        c.SearchPath = "public";
        return c.ConnectionString;
    }
    public static string WorkerConnection(string connection, bool production)
    {
        var c = new NpgsqlConnectionStringBuilder(RuntimeConnection(connection));
        if (production)
        {
            c.Timeout = c.Timeout == 0 ? 5 : Math.Min(c.Timeout, 5);
            c.CommandTimeout = c.CommandTimeout == 0 ? 5 : Math.Min(c.CommandTimeout, 5);
            // Npgsql defines zero as an infinite wait for cancellation acknowledgement.
            c.CancellationTimeout = c.CancellationTimeout == 0 ? 2000 : Math.Min(c.CancellationTimeout, 2000);
        }
        return c.ConnectionString;
    }
    public static void Validate(string connection, bool allowLoopback = false)
    {
        var c = new NpgsqlConnectionStringBuilder(connection);
        RejectRequiredGss(c);
        var local = c.Host == "localhost" || (IPAddress.TryParse(c.Host, out var address) && IPAddress.IsLoopback(address));
        if (allowLoopback && local) return;
        if (c.SslMode != SslMode.VerifyFull)
            throw new InvalidOperationException("Deployment database connections require TLS certificate and hostname verification.");
    }
    private static void RejectRequiredGss(NpgsqlConnectionStringBuilder connection)
    {
        if (connection.ContainsKey("GSS Encryption Mode") && connection.GssEncryptionMode == GssEncryptionMode.Require)
            throw new InvalidOperationException("Required GSS transport is unsupported; configure certificate-verified TLS.");
    }
}
