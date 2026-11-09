using Npgsql;
using NpgsqlTypes;

namespace SaveIT.Search;

public sealed class ComputeUnavailableException : Exception;

// Immutable process-loaded launch binding. Never refresh from a request/reconnect.
public static class ComputeAdmission
{
    public static Guid? ReadGeneration(string? value, bool required)
    {
        if (value is null && !required) return null;
        if (!Guid.TryParseExact(value,"D",out var generation) || generation == Guid.Empty || value != generation.ToString("D"))
            throw new InvalidOperationException("A canonical protected compute generation is required.");
        return generation;
    }

    public static async Task<bool> CheckAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid? generation, CancellationToken ct, bool startup = false)
    {
        await using var command = new NpgsqlCommand(startup ? "SELECT public.compute_startup_v1(@g)" : "SELECT public.compute_enter_v1(@g)",connection,transaction);
        command.CommandTimeout = 4;
        command.Parameters.AddWithValue("g",NpgsqlDbType.Uuid,(object?)generation ?? DBNull.Value);
        return await command.ExecuteScalarAsync(ct) is true;
    }
}
