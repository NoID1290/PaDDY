using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PaDDY.Services;

public class TcpIpcServer : IDisposable
{
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<Guid, TcpClient> _clients = new();

    public event EventHandler<string>? MessageReceived;
    public event EventHandler<int>? ConnectionCountChanged;
    public int ConnectionCount => _clients.Count;

    public TcpIpcServer(int port = 12900)
    {
        _port = port;
    }

    public void Start()
    {
        if (_listener != null) return;

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();

        Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener = null;

        foreach (var client in _clients.Values)
        {
            try { client.Close(); } catch { }
        }
        _clients.Clear();
    }

    public async Task BroadcastAsync(string jsonMessage)
    {
        var buffer = Encoding.UTF8.GetBytes(jsonMessage + "\n");
        foreach (var client in _clients.Values)
        {
            if (!client.Connected) continue;
            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(buffer, 0, buffer.Length);
                await stream.FlushAsync();
            }
            catch
            {
                // Client probably disconnected, it will be cleaned up in the read loop
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                var id = Guid.NewGuid();
                if (_clients.TryAdd(id, client))
                {
                    ConnectionCountChanged?.Invoke(this, _clients.Count);
                }

                _ = Task.Run(() => HandleClientAsync(id, client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private async Task HandleClientAsync(Guid id, TcpClient client, CancellationToken ct)
    {
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!ct.IsCancellationRequested && client.Connected)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break; // Disconnected

                if (!string.IsNullOrWhiteSpace(line))
                {
                    MessageReceived?.Invoke(this, line);
                }
            }
        }
        catch (Exception)
        {
            // Ignore connection errors
        }
        finally
        {
            if (_clients.TryRemove(id, out _))
            {
                ConnectionCountChanged?.Invoke(this, _clients.Count);
            }
            try { client.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
