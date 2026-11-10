using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaveIT.Deployment;

// Owned bounded original-byte input, exact fields and sanitized failures only.
internal static class ProtectedJsonInput
{
    internal const int MaximumBytes = 64 * 1024;
    internal static Task<Dictionary<string,string>> ReadAsync(Stream input, IReadOnlySet<string> fields, CancellationToken cancellationToken) =>
        ReadAsync(input, 2, root =>
        {
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!fields.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.String ||
                    !values.TryAdd(property.Name, property.Value.GetString()!)) throw new InvalidOperationException();
                var value = values[property.Name];
                if (string.IsNullOrWhiteSpace(value) || value.Contains('\0')) throw new InvalidOperationException();
            }
            if (values.Count != fields.Count) throw new InvalidOperationException();
            return values;
        }, cancellationToken);

    // The callback must finish before the original byte buffer is cleared.
    internal static async Task<T> ReadAsync<T>(Stream input, int maximumDepth, Func<JsonElement, T> validate, CancellationToken cancellationToken, int maximumBytes = MaximumBytes)
    {
        if(maximumBytes is <1 or >MaximumBytes)throw new InvalidOperationException("SaveIT protected input is invalid.");
        var buffer = new byte[maximumBytes + 1];
        try
        {
            var count = 0;
            while (count < buffer.Length)
            {
                var pendingRead = input.ReadAsync(buffer.AsMemory(count), cancellationToken).AsTask();
                int read;
                try { read = await pendingRead.WaitAsync(cancellationToken); }
                catch
                {
                    // Console/foreign streams may finish a non-cancellable native
                    // read after WaitAsync expires. Clear again after that write.
                    if (!pendingRead.IsCompleted) _ = ClearAfterReadAsync(pendingRead, buffer);
                    throw;
                }
                if (read == 0) break;
                count += read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (count==0 || count>maximumBytes) throw new InvalidOperationException();
            _ = new UTF8Encoding(false, true).GetCharCount(buffer.AsSpan(0, count));
            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = maximumDepth });
            var result = validate(document.RootElement);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception) { throw new InvalidOperationException("SaveIT protected input is invalid."); }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    private static async Task ClearAfterReadAsync(Task<int> read, byte[] buffer)
    {
        try { await read.ConfigureAwait(false); }
        catch { /* Observe late input failures without exposing their details. */ }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

}
