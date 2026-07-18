using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using Xunit;
using Wstat.Desktop.Models;
using Wstat.Desktop.Services;

namespace Wstat.Desktop.Tests;

public class LocalHttpServerTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ResponseBody
    {
        public string? Status { get; set; }
    }

    [Fact]
    public void TryParseBody_valid_json_returns_payload()
    {
        var body = """{"url": "https://example.com", "title": "Example Page"}""";

        var result = LocalHttpServer.TryParseBody(body);

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://example.com");
        result.Title.Should().Be("Example Page");
    }

    [Fact]
    public void TryParseBody_missing_url_returns_payload_with_null_url()
    {
        var body = """{"title": "Only Title"}""";

        var result = LocalHttpServer.TryParseBody(body);

        result.Should().NotBeNull();
        result!.Url.Should().BeNull();
        result.Title.Should().Be("Only Title");
    }

    [Fact]
    public void TryParseBody_missing_title_returns_payload_with_null_title()
    {
        var body = """{"url": "https://example.com"}""";

        var result = LocalHttpServer.TryParseBody(body);

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://example.com");
        result.Title.Should().BeNull();
    }

    [Fact]
    public void TryParseBody_empty_string_returns_null()
    {
        LocalHttpServer.TryParseBody("").Should().BeNull();
        LocalHttpServer.TryParseBody(null!).Should().BeNull();
    }

    [Fact]
    public void TryParseBody_malformed_json_returns_null()
    {
        var result = LocalHttpServer.TryParseBody("not json {{{");

        result.Should().BeNull();
    }

    [Fact]
    public async Task Post_valid_tab_payload_calls_SetBrowserTab_and_returns_200()
    {
        var port = GetAvailablePort();
        var tracker = Substitute.For<IWindowTrackerService>();
        var settings = new SettingsModel { HttpPort = port };
        using var server = new LocalHttpServer(tracker, settings);
        server.Start();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var payload = """{"url": "https://example.com", "title": "Example Page"}""";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://127.0.0.1:{port}/tab", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonSerializer.Deserialize<ResponseBody>(await response.Content.ReadAsStringAsync(), JsonOpts);
        body!.Status.Should().Be("ok");
        tracker.Received(1).SetBrowserTab("https://example.com", "Example Page");
    }

    [Fact]
    public async Task Post_to_wrong_path_returns_404()
    {
        var port = GetAvailablePort();
        var tracker = Substitute.For<IWindowTrackerService>();
        var settings = new SettingsModel { HttpPort = port };
        using var server = new LocalHttpServer(tracker, settings);
        server.Start();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var payload = """{"url": "https://example.com", "title": "Example"}""";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://127.0.0.1:{port}/wrong-path", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = JsonSerializer.Deserialize<ResponseBody>(await response.Content.ReadAsStringAsync(), JsonOpts);
        body!.Status.Should().Be("not found");
        tracker.DidNotReceiveWithAnyArgs().SetBrowserTab(default!, default!);
    }

    [Fact]
    public async Task Get_request_returns_404()
    {
        var port = GetAvailablePort();
        var tracker = Substitute.For<IWindowTrackerService>();
        var settings = new SettingsModel { HttpPort = port };
        using var server = new LocalHttpServer(tracker, settings);
        server.Start();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = await client.GetAsync($"http://127.0.0.1:{port}/tab");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        tracker.DidNotReceiveWithAnyArgs().SetBrowserTab(default!, default!);
    }

    [Fact]
    public async Task Post_malformed_body_returns_400()
    {
        var port = GetAvailablePort();
        var tracker = Substitute.For<IWindowTrackerService>();
        var settings = new SettingsModel { HttpPort = port };
        using var server = new LocalHttpServer(tracker, settings);
        server.Start();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var content = new StringContent("not json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://127.0.0.1:{port}/tab", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        tracker.DidNotReceiveWithAnyArgs().SetBrowserTab(default!, default!);
    }

    [Fact]
    public async Task Post_empty_body_returns_400()
    {
        var port = GetAvailablePort();
        var tracker = Substitute.For<IWindowTrackerService>();
        var settings = new SettingsModel { HttpPort = port };
        using var server = new LocalHttpServer(tracker, settings);
        server.Start();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://127.0.0.1:{port}/tab", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stop_then_restart_works()
    {
        var port = GetAvailablePort();
        var tracker = Substitute.For<IWindowTrackerService>();
        var settings = new SettingsModel { HttpPort = port };
        using var server = new LocalHttpServer(tracker, settings);
        server.Start();

        server.Stop();
        server.Start();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var payload = """{"url": "https://example.com", "title": "Ex"}""";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://127.0.0.1:{port}/tab", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        tracker.Received(1).SetBrowserTab("https://example.com", "Ex");
    }

    [Fact]
    public async Task Sets_url_with_non_http_prefix_still_calls_tracker()
    {
        var port = GetAvailablePort();
        var tracker = Substitute.For<IWindowTrackerService>();
        var settings = new SettingsModel { HttpPort = port };
        using var server = new LocalHttpServer(tracker, settings);
        server.Start();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var payload = """{"url": "about:config", "title": "Config"}""";
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"http://127.0.0.1:{port}/tab", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        tracker.Received(1).SetBrowserTab("about:config", "Config");
    }

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}