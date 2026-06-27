using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Wstat.Desktop.Services;

public class LocalHttpServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string LogPath = CreateLogPath();

    private readonly TcpListener _listener;
    private readonly WindowTrackerService _tracker;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _disposed;

    public LocalHttpServer(WindowTrackerService tracker)
    {
        _tracker = tracker;
        _listener = new TcpListener(System.Net.IPAddress.Loopback, 12345);
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            _listenTask = ListenLoop(_cts.Token);
            WriteLog("Server started on 127.0.0.1:12345");
        }
        catch (Exception ex)
        {
            WriteLog($"FAILED to start: {ex.Message}");
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                WriteLog($"Connection from {client.Client.RemoteEndPoint}");
                _ = HandleClient(client, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var requestLine = await reader.ReadLineAsync(ct);
                WriteLog($"Request: {requestLine ?? "(empty)"}");

                if (string.IsNullOrEmpty(requestLine)) return;

                var contentLength = 0;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(ct)))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int.TryParse(line.AsSpan("Content-Length:".Length), out contentLength);
                    }
                }

                var body = "";
                if (contentLength > 0)
                {
                    var buffer = new char[contentLength];
                    var read = await reader.ReadAsync(buffer.AsMemory(0, contentLength), ct);
                    body = new string(buffer, 0, read);
                    WriteLog($"Body ({read} chars): {body[..Math.Min(body.Length, 200)]}");
                }

                var responseBody = "{\"status\":\"ok\"}";
                var statusLine = "HTTP/1.1 200 OK\r\n";

                if (requestLine.StartsWith("POST ") && requestLine.Contains("/tab"))
                {
                    var payload = JsonSerializer.Deserialize<TabPayload>(body, JsonOpts);
                    if (payload != null)
                    {
                        WriteLog($"Calling SetBrowserTab(url={payload.Url}, title={payload.Title})");
                        _tracker.SetBrowserTab(payload.Url ?? "", payload.Title ?? "");
                    }
                    else
                    {
                        WriteLog($"Failed to deserialize body: {body}");
                    }
                }
                else
                {
                    statusLine = "HTTP/1.1 404 Not Found\r\n";
                    responseBody = "{\"status\":\"not found\"}";
                }

                var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                var response = $"{statusLine}Content-Type: application/json\r\nContent-Length: {responseBytes.Length}\r\nConnection: close\r\n\r\n";
                var headerBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(headerBytes, ct);
                await stream.WriteAsync(responseBytes, ct);
                await stream.FlushAsync(ct);
            }
        }
        catch (Exception ex)
        {
            WriteLog($"HandleClient error: {ex.Message}");
        }
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
    }

    private static string CreateLogPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "wstat");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "trace.log");
    }

    private static void WriteLog(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} [HttpServer] {message}\n");
        }
        catch { }
    }

    private class TabPayload
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
    }
}
