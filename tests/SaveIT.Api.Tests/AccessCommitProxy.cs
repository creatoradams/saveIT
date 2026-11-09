using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace SaveIT.Api.Tests;

// Synthetic loopback PostgreSQL protocol fault injector. It forwards real authentication
// and queries, but loses one COMMIT acknowledgement AFTER PostgreSQL reports the commit.
// Never used by the application or with real credentials; SSL is disabled only on this test hop.
internal sealed class AccessCommitProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly List<Task> clients = [];
    private readonly string host;
    private readonly int port;
    private readonly int cutCommit;
    private readonly bool holdRollback;
    private int rollbacks;
    private readonly Task accepting;
    private int commits;
    public TaskCompletionSource CommitLost { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource RollbackObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseRollback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Connection { get; }

    public AccessCommitProxy(string connection, int cutCommit, bool holdRollback = false)
    {
        var settings = new NpgsqlConnectionStringBuilder(connection); host = settings.Host!; port = settings.Port; this.cutCommit = cutCommit;
        this.holdRollback = holdRollback;
        listener.Start(); settings.Host = "127.0.0.1"; settings.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        settings.SslMode = SslMode.Disable; settings.GssEncryptionMode = GssEncryptionMode.Disable;
        settings.ApplicationName = "SaveIT.SyntheticCommitProxy"; Connection = settings.ConnectionString;
        accepting = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token); clients.Add(RelayAsync(client));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
    }
    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var server = new TcpClient())
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            try
            {
                await server.ConnectAsync(host, port, cancel.Token);
                var incoming = client.GetStream(); var outgoing = server.GetStream();
                var front = incoming.CopyToAsync(outgoing, cancel.Token);
                var back = BackAsync(outgoing, incoming, cancel.Token);
                await Task.WhenAny(front, back); cancel.Cancel(); client.Close(); server.Close();
                try { await Task.WhenAll(front, back); } catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
    }
    private async Task BackAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var header = new byte[5];
        while (true)
        {
            await source.ReadExactlyAsync(header, ct); var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
            if (length is < 4 or > 1_048_576) throw new IOException("Unexpected synthetic protocol frame.");
            var body = new byte[length - 4]; await source.ReadExactlyAsync(body, ct);
            if (header[0] == (byte)'C' && body.AsSpan().SequenceEqual("COMMIT\0"u8) && Interlocked.Increment(ref commits) == cutCommit)
            { CommitLost.TrySetResult(); return; }
            if (holdRollback && header[0] == (byte)'C' && body.AsSpan().SequenceEqual("ROLLBACK\0"u8) && Interlocked.Increment(ref rollbacks)==1)
            { RollbackObserved.TrySetResult(); await ReleaseRollback.Task.WaitAsync(ct); }
            await destination.WriteAsync(header, ct); await destination.WriteAsync(body, ct);
        }
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); listener.Stop(); await accepting; await Task.WhenAll(clients); stopping.Dispose();
    }
}
