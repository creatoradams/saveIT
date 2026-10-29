using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace SaveIT.Search;

public sealed record SearchHit(Guid Id, string Type, Guid OrganizationId, string OrganizationName,
    Guid AssetId, string Title, string Preview, int Version, DateTimeOffset UpdatedAt);
public sealed record SearchPage(IReadOnlyList<SearchHit> Items, string? NextCursor);
public sealed class SearchValidationException : Exception;
public sealed class SearchUnavailableException : Exception;

// Candidates are hints. All returned fields and authorization come from current core rows.
public static class SearchReader
{
    public const int PageSize = 25;
    private sealed record Cursor(float Score, string Type, Guid Id, string Fingerprint);

    public static async Task<SearchPage> FindAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid tenantId, Guid membershipId, string subject, string? query, Guid? organizationId, string? type,
        string? after, CancellationToken cancellationToken)
    {
        var text = query?.Trim() ?? "";
        if (text.Length is < 2 or > 120 || text.Any(char.IsControl) || text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 12
            || (type is not null and not "asset" and not "document")) throw new SearchValidationException();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId}|{membershipId}|{text}|{organizationId}|{type}")));
        Cursor? cursor = null;
        if (after is not null)
        {
            try
            {
                if (after.Length > 512) throw new SearchValidationException();
                cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(after));
                if (cursor is null || cursor.Fingerprint != fingerprint || !float.IsFinite(cursor.Score) || cursor.Score < 0
                    || cursor.Type is not ("asset" or "document") || cursor.Id == Guid.Empty) throw new SearchValidationException();
            }
            catch (Exception e) when (e is FormatException or JsonException) { throw new SearchValidationException(); }
        }
        try
        {
            await using var command = new NpgsqlCommand("""
                WITH q AS (SELECT plainto_tsquery('simple', @query) AS value), visible AS (
                    SELECT s."Id", s."RecordType", s."OrganizationId", o."Name" AS "OrganizationName",
                           s."AssetId", s."Title", left(s."Body", 240) AS "Preview", s."Version", s."UpdatedAt",
                           ts_rank(p."SearchVector", q.value) AS score
                    FROM "SearchEntries" p
                    JOIN "CoreSearchSources" s ON s."TenantId"=p."TenantId" AND s."RecordType"=p."RecordType"
                        AND s."Id"=p."Id" AND s."OrganizationId"=p."OrganizationId" AND s."AssetId"=p."AssetId"
                        AND s."Version"=p."Version" AND s."Title"=p."Title" AND s."Body"=p."Body"
                    JOIN "Organizations" o ON o."TenantId"=s."TenantId" AND o."Id"=s."OrganizationId"
                    JOIN "OrganizationGrants" g ON g."TenantId"=s."TenantId" AND g."OrganizationId"=s."OrganizationId"
                    JOIN "Memberships" m ON m."TenantId"=g."TenantId" AND m."Id"=g."MembershipId"
                    CROSS JOIN q
                    WHERE s."TenantId"=@tenant AND m."Id"=@membership AND m."Subject"=@subject AND m."Active"
                        AND g."Role" IN ('Viewer','Editor') AND p."SearchVector" @@ q.value
                        AND (@organization IS NULL OR s."OrganizationId"=@organization)
                        AND (@type IS NULL OR s."RecordType"=@type)
                )
                SELECT * FROM visible
                WHERE NOT @has_cursor OR score < @score OR (score = @score AND
                    ("RecordType" > @cursor_type OR ("RecordType" = @cursor_type AND "Id" > @cursor_id)))
                ORDER BY score DESC, "RecordType", "Id"
                LIMIT 26
                """, connection, transaction);
            command.Parameters.AddWithValue("query", text);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("membership", membershipId);
            command.Parameters.AddWithValue("subject", subject);
            command.Parameters.AddWithValue("organization", NpgsqlDbType.Uuid, (object?)organizationId ?? DBNull.Value);
            command.Parameters.AddWithValue("type", NpgsqlDbType.Text, (object?)type ?? DBNull.Value);
            command.Parameters.AddWithValue("has_cursor", cursor is not null);
            command.Parameters.AddWithValue("score", cursor?.Score ?? 0f);
            command.Parameters.AddWithValue("cursor_type", cursor?.Type ?? "");
            command.Parameters.AddWithValue("cursor_id", cursor?.Id ?? Guid.Empty);
            var found = new List<(SearchHit Hit, float Score)>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                found.Add((new(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3),
                    reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetFieldValue<DateTimeOffset>(8)), reader.GetFloat(9)));
            var next = found.Count > PageSize ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                new Cursor(found[PageSize - 1].Score, found[PageSize - 1].Hit.Type, found[PageSize - 1].Hit.Id, fingerprint))) : null;
            return new(found.Take(PageSize).Select(x => x.Hit).ToList(), next);
        }
        catch (NpgsqlException) { throw new SearchUnavailableException(); }
    }
}
