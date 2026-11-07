using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaveIT.Deployment;

// Owned bounded original-byte input, exact fields and sanitized failures only.
internal static class ProtectedJsonInput
{
    internal const int MaximumBytes = 64 * 1024;
    internal static async Task<Dictionary<string,string>> ReadAsync(Stream input, IReadOnlySet<string> fields, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaximumBytes + 1];
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
            if (count is 0 or > MaximumBytes) throw new InvalidOperationException();
            _ = new UTF8Encoding(false, true).GetCharCount(buffer.AsSpan(0, count));
            using var document = JsonDocument.Parse(buffer.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 2 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!fields.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.String ||
                    !values.TryAdd(property.Name, property.Value.GetString()!)) throw new InvalidOperationException();
                var value = values[property.Name];
                if (string.IsNullOrWhiteSpace(value) || value.Contains('\0')) throw new InvalidOperationException();
            }
            if (values.Count != fields.Count) throw new InvalidOperationException();
            return values;
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
