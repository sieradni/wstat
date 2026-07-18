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
    private readonly CancellationTokenSource _cts = new();
    private Task? _listenTask;
    private bool _disposed;

    public LocalHttpServer(IWindowTrackerService tracker, SettingsModel settings)
    {
        _tracker = tracker;
        _settings = settings;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_settings.HttpPort}/");
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

                var payload = JsonSerializer.Deserialize<TabPayload>(body, JsonOpts);
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
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
        (_listener as IDisposable)?.Dispose();
    }

    private sealed class TabPayload
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
    }
}
