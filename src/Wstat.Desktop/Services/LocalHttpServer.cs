using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Wstat.Desktop.Services;

public class LocalHttpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly WindowTrackerService _tracker;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public LocalHttpServer(WindowTrackerService tracker)
    {
        _tracker = tracker;
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://127.0.0.1:12345/");
    }

    public void Start()
    {
        try
        {
            _listener.Start();
            _ = ListenLoop(_cts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HttpServer] Failed to start: {ex.Message}");
        }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(ct);
                _ = HandleRequest(context);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/tab")
            {
                using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();

                var payload = JsonSerializer.Deserialize<TabPayload>(body);
                if (payload != null)
                {
                    _tracker.SetBrowserTab(payload.Url ?? "", payload.Title ?? "");
                    System.Diagnostics.Debug.WriteLine($"[HttpServer] Tab: {payload.Title} ({payload.Url})");
                }

                response.StatusCode = 200;
                var buffer = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
            }
            else
            {
                response.StatusCode = 404;
            }

            response.OutputStream.Close();
        }
        catch
        {
            // Silently handle client disconnect errors
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

    private class TabPayload
    {
        public string? Url { get; set; }
        public string? Title { get; set; }
    }
}
