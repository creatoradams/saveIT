using Npgsql;

namespace SaveIT.Search;

public sealed record SearchBatchResult(int Completed, int Retried, int DeadLettered);

// A dedicated consumer owns projections/receipts. Core writes occur only in the API.
// Each invalidation rehydrates current source state, so duplicates and event order are safe.
public sealed class SearchProcessor(string connectionString, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private sealed record Event(Guid Id, Guid OrganizationId, Guid TargetId, string Type, int SchemaVersion);
    private sealed record Source(Guid Id, Guid OrganizationId, Guid AssetId, string Type, string Title,
        string Body, int Version, DateTimeOffset UpdatedAt);

    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
    }

    public async Task<SearchBatchResult> ProcessBatchAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var completed = 0; var retried = 0; var dead = 0;
        for (var i = 0; i < 100; i++)
        {
            Event? current = null;
            await using var transaction = await BeginTenantAsync(connection, tenantId, cancellationToken);
            if (!await LockAsync(connection, transaction, tenantId, false, cancellationToken)) break;
            await using (var command = new NpgsqlCommand("""
                SELECT e."Id",e."OrganizationId",e."TargetId",e."Type",e."SchemaVersion"
                FROM "SearchDeliveries" d JOIN "OutboxEvents" e
                  ON d."TenantId"=e."TenantId" AND d."EventId"=e."Id"
                WHERE d."TenantId"=@tenant AND d."CompletedAt" IS NULL AND d."DeadLetteredAt" IS NULL
                  AND d."NextAttemptAt" <= @now
                ORDER BY d."NextAttemptAt",d."EventId" LIMIT 1
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("tenant", tenantId);
                command.Parameters.AddWithValue("now", clock.GetUtcNow());
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken)) current = new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetInt32(4));
            }
            if (current is null) break;
            try
            {
                var isDeadLetter = false;
                var type = current.Type switch { "Asset.Created" or "Asset.Revised" or "Asset.Restored" => "asset", "Document.Created" or "Document.Revised" or "Document.Restored" => "document", _ => null };
                // Explicitly acknowledge core relationship events; they have no searchable text and
                // must not delete or create asset/document projections. Other schemas/types still fail.
                var metadataOnly = current.Type is "AssetRelationship.Created" or "AssetRelationship.Removed" or "Location.Created" or "Location.Revised" or "Contact.Created" or "Contact.Revised" or "Organization.Created" or "Organization.Revised" or "AssetType.Created" or "AssetType.Revised";
                if (current.SchemaVersion != 1 || (type is null && !metadataOnly))
                {
                    await WriteFailureAsync(connection, transaction, tenantId, current.Id, "unsupported_event", true, cancellationToken);
                    isDeadLetter = true;
                }
                else if (type is not null)
                {
                    var source = await ReadSourceAsync(connection, transaction, tenantId, current.TargetId, type, cancellationToken);
                    if (source is not null && source.OrganizationId != current.OrganizationId)
                    {
                        await WriteFailureAsync(connection, transaction, tenantId, current.Id, "scope_mismatch", true, cancellationToken);
                        isDeadLetter = true;
                    }
                    else
                    {
                        await ProjectAsync(connection, transaction, tenantId, current.TargetId, type, source, cancellationToken);
                    }
                }
                if (!isDeadLetter)
                {
                    await using var receipt = new NpgsqlCommand("""
                        INSERT INTO "SearchDeliveries" ("TenantId","EventId","Attempts","NextAttemptAt","CompletedAt")
                        VALUES (@tenant,@event,1,@now,@now)
                        ON CONFLICT ("TenantId","EventId") DO UPDATE SET "Attempts"="SearchDeliveries"."Attempts"+1,
                          "CompletedAt"=@now,"DeadLetteredAt"=NULL,"ErrorCode"=NULL
                        """, connection, transaction);
                    receipt.Parameters.AddWithValue("tenant", tenantId);
                    receipt.Parameters.AddWithValue("event", current.Id);
                    receipt.Parameters.AddWithValue("now", clock.GetUtcNow());
                    await receipt.ExecuteNonQueryAsync(cancellationToken);
                }
                await transaction.CommitAsync(cancellationToken);
                if (isDeadLetter) dead++; else completed++;
            }
            catch (NpgsqlException) when (!cancellationToken.IsCancellationRequested)
            {
                await transaction.RollbackAsync(cancellationToken);
                // Record retries separately; the failed projection/receipt transaction was rolled back.
                await using var failureTransaction = await BeginTenantAsync(connection, tenantId, cancellationToken);
                await LockAsync(connection, failureTransaction, tenantId, true, cancellationToken);
                var isDead = await WriteFailureAsync(connection, failureTransaction, tenantId, current.Id, "projection_database_failure", false, cancellationToken);
                await failureTransaction.CommitAsync(cancellationToken);
                if (isDead) dead++; else retried++;
            }
        }
        return new(completed, retried, dead);
    }

    public async Task<int> RebuildAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await BeginTenantAsync(connection, tenantId, cancellationToken);
        await LockAsync(connection, transaction, tenantId, true, cancellationToken);
        await using (var clear = new NpgsqlCommand("DELETE FROM \"SearchEntries\" WHERE \"TenantId\"=@tenant", connection, transaction))
        {
            clear.Parameters.AddWithValue("tenant", tenantId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var rebuild = new NpgsqlCommand("""
            INSERT INTO "SearchEntries" ("TenantId","RecordType","Id","OrganizationId","AssetId","Title","Body","Version","UpdatedAt","IndexedAt")
            SELECT "TenantId","RecordType","Id","OrganizationId","AssetId","Title","Body","Version","UpdatedAt",@now
            FROM "CoreSearchSources" WHERE "TenantId"=@tenant
            """, connection, transaction);
        rebuild.Parameters.AddWithValue("tenant", tenantId);
        rebuild.Parameters.AddWithValue("now", clock.GetUtcNow());
        var count = await rebuild.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count;
    }

    public async Task<bool> ReplayAsync(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await BeginTenantAsync(connection, tenantId, cancellationToken);
        await LockAsync(connection, transaction, tenantId, true, cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE "SearchDeliveries" SET "Attempts"=0,"NextAttemptAt"=CURRENT_TIMESTAMP,
                "CompletedAt"=NULL,"DeadLetteredAt"=NULL,"ErrorCode"=NULL
            WHERE "TenantId"=@tenant AND "EventId"=@event
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("event", eventId);
        var count = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return count > 0;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try { await connection.OpenAsync(cancellationToken); await DatabaseRoleGuard.ValidateSearchWorkerAsync(connection, cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<NpgsqlTransaction> BeginTenantAsync(NpgsqlConnection connection, Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A configured tenant is required.", nameof(tenantId));
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT set_config('app.tenant_id',@tenant,true)", connection, transaction);
            command.Parameters.AddWithValue("tenant", tenantId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private static async Task<bool> LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, bool wait, CancellationToken cancellationToken)
    {
        var function = wait ? "pg_advisory_xact_lock" : "pg_try_advisory_xact_lock";
        await using var command = new NpgsqlCommand($"SELECT {function}(hashtextextended(@key,0))", connection, transaction);
        command.Parameters.AddWithValue("key", "saveit-search-v1:" + tenantId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return wait || result is true;
    }

    private static async Task<Source?> ReadSourceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid targetId, string type, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT "Id","OrganizationId","AssetId","RecordType","Title","Body","Version","UpdatedAt"
            FROM "CoreSearchSources" WHERE "TenantId"=@tenant AND "Id"=@id AND "RecordType"=@type
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("id", targetId);
        command.Parameters.AddWithValue("type", type);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), reader.GetInt32(6), reader.GetFieldValue<DateTimeOffset>(7)) : null;
    }

    private async Task ProjectAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid targetId, string type, Source? source, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            await using var remove = new NpgsqlCommand("DELETE FROM \"SearchEntries\" WHERE \"TenantId\"=@tenant AND \"Id\"=@id AND \"RecordType\"=@type", connection, transaction);
            remove.Parameters.AddWithValue("tenant", tenantId); remove.Parameters.AddWithValue("id", targetId); remove.Parameters.AddWithValue("type", type);
            await remove.ExecuteNonQueryAsync(cancellationToken);
            return;
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO "SearchEntries" ("TenantId","RecordType","Id","OrganizationId","AssetId","Title","Body","Version","UpdatedAt","IndexedAt")
            VALUES (@tenant,@type,@id,@org,@asset,@title,@body,@version,@updated,@now)
            ON CONFLICT ("TenantId","RecordType","Id") DO UPDATE SET
              "OrganizationId"=EXCLUDED."OrganizationId","AssetId"=EXCLUDED."AssetId","Title"=EXCLUDED."Title",
              "Body"=EXCLUDED."Body","Version"=EXCLUDED."Version","UpdatedAt"=EXCLUDED."UpdatedAt","IndexedAt"=EXCLUDED."IndexedAt"
            WHERE "SearchEntries"."Version" <= EXCLUDED."Version"
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId); command.Parameters.AddWithValue("type", source.Type);
        command.Parameters.AddWithValue("id", source.Id); command.Parameters.AddWithValue("org", source.OrganizationId);
        command.Parameters.AddWithValue("asset", source.AssetId); command.Parameters.AddWithValue("title", source.Title);
        command.Parameters.AddWithValue("body", source.Body); command.Parameters.AddWithValue("version", source.Version);
        command.Parameters.AddWithValue("updated", source.UpdatedAt); command.Parameters.AddWithValue("now", clock.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> WriteFailureAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId,
        Guid eventId, string errorCode, bool permanent, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO "SearchDeliveries" ("TenantId","EventId","Attempts","NextAttemptAt","DeadLetteredAt","ErrorCode")
            VALUES (@tenant,@event,1,@now + interval '2 seconds',CASE WHEN @permanent THEN @now END,@error)
            ON CONFLICT ("TenantId","EventId") DO UPDATE SET "Attempts"="SearchDeliveries"."Attempts"+1,
              "NextAttemptAt"=@now + make_interval(secs => LEAST(60,power(2,"SearchDeliveries"."Attempts"+1))) + @jitter * interval '1 millisecond',
              "DeadLetteredAt"=CASE WHEN @permanent OR "SearchDeliveries"."Attempts"+1 >= 5 THEN @now END,"ErrorCode"=@error
            WHERE "SearchDeliveries"."CompletedAt" IS NULL AND "SearchDeliveries"."DeadLetteredAt" IS NULL
            RETURNING "DeadLetteredAt" IS NOT NULL
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", tenantId); command.Parameters.AddWithValue("event", eventId);
        command.Parameters.AddWithValue("now", clock.GetUtcNow()); command.Parameters.AddWithValue("permanent", permanent);
        command.Parameters.AddWithValue("jitter", Random.Shared.Next(0, 250)); command.Parameters.AddWithValue("error", errorCode);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
