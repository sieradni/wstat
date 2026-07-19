using System.IO;
using System.Net;
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

    private readonly HttpListener _listener;
    private readonly IWindowTrackerService _tracker;
    private readonly SettingsModel _settings;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _disposed;
    private int _actualPort;

    public bool IsRunning => _listener.IsListening;

    public LocalHttpServer(IWindowTrackerService tracker, SettingsModel settings)
    {
        _tracker = tracker;
        _settings = settings;
        _listener = new HttpListener();
    }

    public void Start()
    {
        if (_disposed) return;

        var port = _settings.HttpPort;
        var maxAttempts = 10;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port + attempt}/");
                _listener.Start();
                _actualPort = port + attempt;
                _listenTask = ListenLoop(_cts.Token);
                LogWriter.Write("[HttpServer] Server started on 127.0.0.1:" + _actualPort);
                return;
            }
            catch (Exception ex)
            {
                LogWriter.Write("[HttpServer] Failed to bind port " + (port + attempt) + ": " + ex.Message);
            }
        }

        LogWriter.Write("[HttpServer] FAILED to start after " + maxAttempts + " port attempts");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleRequest(ctx, ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(RequestTimeout);
            ct = timeoutCts.Token;

            if (ctx.Request.HttpMethod == "POST" && ctx.Request.Url?.AbsolutePath == "/tab")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync(ct);
                var preview = body.Length > 200 ? body[..200] + "..." : body;
                LogWriter.Write("[HttpServer] Body (" + body.Length + " chars): " + preview);

                var payload = TryParseBody(body);
                if (payload != null)
                {
                    LogWriter.Write("[HttpServer] Calling SetBrowserTab(url=" + payload.Url + ", title=" + payload.Title + ")");
                    _tracker.SetBrowserTab(payload.Url ?? "", payload.Title ?? "");
                    ctx.Response.StatusCode = 200;
                }
                else
                {
                    LogWriter.Write("[HttpServer] Failed to deserialize body: " + body);
                    ctx.Response.StatusCode = 400;
                }
            }
            else
            {
                ctx.Response.StatusCode = 404;
            }

            var responseBody = JsonSerializer.Serialize(new
            {
                status = ctx.Response.StatusCode switch
                {
                    200 => "ok",
                    400 => "bad request",
                    _ => "not found"
                }
            });
            var buffer = Encoding.UTF8.GetBytes(responseBody);
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Close(buffer, willBlock: false);
        }
        catch (OperationCanceledException)
        {
            LogWriter.Write("[HttpServer] Request timed out");
        }
        catch (Exception ex)
        {
            LogWriter.Write("[HttpServer] HandleRequest error: " + ex.Message);
            try { ctx.Response?.Close(); } catch { }
        }
    }

    public void Stop()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
        try { _listener.Stop(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
        (_listener as IDisposable)?.Dispose();
    }

    internal sealed class TabPayload
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
    }

    internal static TabPayload? TryParseBody(string body)
    {
        if (string.IsNullOrEmpty(body))
            return null;

        try
        {
            return JsonSerializer.Deserialize<TabPayload>(body, JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
