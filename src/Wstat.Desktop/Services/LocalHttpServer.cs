using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Wstat.Desktop.Common;
using Wstat.Desktop.Models;

namespace Wstat.Desktop.Services;

public class LocalHttpServer : ILocalHttpServer, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly IWindowTrackerService _tracker;
    private readonly SettingsModel _settings;
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _disposed;

    public LocalHttpServer(IWindowTrackerService tracker, SettingsModel settings)
    {
        _tracker = tracker;
        _settings = settings;
        _listener = new TcpListener(System.Net.IPAddress.Loopback, _settings.HttpPort);
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            _listenTask = ListenLoop(_cts.Token);
            LogWriter.Write("[HttpServer] Server started on 127.0.0.1:" + _settings.HttpPort);
        }
        catch (Exception ex)
        {
            LogWriter.Write("[HttpServer] FAILED to start: " + ex.Message);
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                LogWriter.Write("[HttpServer] Connection from " + client.Client.RemoteEndPoint);
                _ = HandleClient(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeout);

            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var requestLine = await reader.ReadLineAsync(timeoutCts.Token);
                LogWriter.Write("[HttpServer] Request: " + (requestLine ?? "(empty)"));

                if (string.IsNullOrEmpty(requestLine)) return;

                var contentLength = 0;
                string? line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(timeoutCts.Token)))
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
                    var totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        var read = await reader.ReadAsync(buffer.AsMemory(totalRead, contentLength - totalRead), timeoutCts.Token);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    body = new string(buffer, 0, totalRead);
                    var preview = body.Length > 200 ? body[..200] + "..." : body;
                    LogWriter.Write("[HttpServer] Body (" + totalRead + " chars): " + preview);
                }

                var responseBody = "{\"status\":\"ok\"}";
                var statusLine = "HTTP/1.1 200 OK\r\n";

                if (requestLine.StartsWith("POST ") && requestLine.Contains("/tab"))
                {
                    var payload = JsonSerializer.Deserialize<TabPayload>(body, JsonOpts);
                    if (payload != null)
                    {
                        LogWriter.Write("[HttpServer] Calling SetBrowserTab(url=" + payload.Url + ", title=" + payload.Title + ")");
                        _tracker.SetBrowserTab(payload.Url ?? "", payload.Title ?? "");
                    }
                    else
                    {
                        LogWriter.Write("[HttpServer] Failed to deserialize body: " + body);
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
                await stream.WriteAsync(headerBytes, timeoutCts.Token);
                await stream.WriteAsync(responseBytes, timeoutCts.Token);
                await stream.FlushAsync(timeoutCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            LogWriter.Write("[HttpServer] Request timed out");
        }
        catch (Exception ex)
        {
            LogWriter.Write("[HttpServer] HandleClient error: " + ex.Message);
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

    private class TabPayload
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
    }
}
