using System.Buffers;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Klien protokol Blazor Server tanpa browser: memulai sirkuit lewat hub SignalR <c>/_blazor</c> dari penanda
/// prarender, lalu bersikap seperti browser yang idle: menerima batch render dan mengakuinya, serta menjawab
/// panggilan JS interop dari server (idle.js: start/idleSeconds/reset/stop). Cukup ringan untuk ratusan sirkuit
/// pada satu mesin. Belum mengirim event klik; yang diukur adalah sirkuit aktif beserta timer/detak sesinya.
/// </summary>
internal sealed partial class CircuitClient : IAsyncDisposable
{
    private readonly HubConnection _hub;
    private long _nextObjectId;
    private int _batches;
    private int _jsCalls;

    public CircuitClient(Uri baseUrl, CookieContainer cookies)
    {
        var builder = new HubConnectionBuilder();
        builder.Services.AddSingleton<IHubProtocol, BlazorPackProtocol>();
        if (Environment.GetEnvironmentVariable("CIRCUIT_DEBUG") == "1")
        {
            builder.ConfigureLogging(l => l.AddConsole().SetMinimumLevel(LogLevel.Debug));
        }

        _hub = builder
            .WithUrl(new Uri(baseUrl, "/_blazor"), o =>
            {
                o.Cookies = cookies;
                o.Transports = HttpTransportType.WebSockets;
                o.SkipNegotiation = true;
            })
            .Build();
        _hub.ServerTimeout = TimeSpan.FromMinutes(5);

        _hub.On("JS.RenderBatch", [typeof(long), typeof(byte[])], async args =>
        {
            Interlocked.Increment(ref _batches);
            await _hub.SendAsync("OnRenderCompleted", (long)args[0]!, (string?)null).ConfigureAwait(false);
        });

        _hub.On("JS.BeginInvokeJS", [typeof(long), typeof(string), typeof(string), typeof(int), typeof(long), typeof(int)], async args =>
        {
            Interlocked.Increment(ref _jsCalls);
            var taskId = (long)args[0]!;
            if (taskId == 0)
            {
                return; // fire-and-forget
            }

            var identifier = (string)args[1]!;
            var result = identifier switch
            {
                "import" => JsonSerializer.Serialize(new Dictionary<string, long> { ["__jsObjectId"] = Interlocked.Increment(ref _nextObjectId) }),
                "idleSeconds" => "0",
                _ => "null",
            };
            await _hub.InvokeAsync("EndInvokeJSFromDotNet", taskId, true, JsonSerializer.Serialize(new object[] { taskId, true, JsonDocument.Parse(result).RootElement })).ConfigureAwait(false);
        });
    }

    public int Batches => _batches;

    public int JsCalls => _jsCalls;

    public bool Closed { get; private set; }

    public async Task<string?> StartAsync(Uri baseUrl, string shellHtml, CancellationToken ct)
    {
        var markers = Markers(shellHtml);
        var state = AppState(shellHtml);
        _hub.Closed += _ =>
        {
            Closed = true;
            return Task.CompletedTask;
        };
        await _hub.StartAsync(ct).ConfigureAwait(false);
        var b = baseUrl.ToString();
        // Seperti blazor.web.js: StartCircuit membawa penanda komponen prarender; server langsung merender batch awal.
        return await _hub.InvokeAsync<string?>("StartCircuit", b, b, markers, state, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _hub.DisposeAsync().ConfigureAwait(false);

    private static string Markers(string html)
    {
        var list = new List<JsonElement>();
        foreach (Match m in MarkerRegex().Matches(html))
        {
            using var doc = JsonDocument.Parse(m.Groups[1].Value);
            if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "server")
            {
                list.Add(doc.RootElement.Clone());
            }
        }

        return JsonSerializer.Serialize(list);
    }

    private static string AppState(string html)
    {
        var m = StateRegex().Match(html);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex(@"<!--Blazor:(\{.*?\})-->", RegexOptions.Singleline)]
    private static partial Regex MarkerRegex();

    [GeneratedRegex(@"<!--Blazor-Server-Component-State:([^>]*?)-->", RegexOptions.Singleline)]
    private static partial Regex StateRegex();
}

/// <summary>BlazorPack = MessagePack hub protocol dengan nama "blazorpack" (sama seperti klien JS Blazor).</summary>
internal sealed class BlazorPackProtocol : IHubProtocol
{
    private readonly MessagePackHubProtocol _inner = new();

    public string Name => "blazorpack";

    public int Version => _inner.Version;

    public TransferFormat TransferFormat => _inner.TransferFormat;

    public bool IsVersionSupported(int version) => _inner.IsVersionSupported(version);

    public bool TryParseMessage(ref ReadOnlySequence<byte> input, IInvocationBinder binder, out HubMessage? message)
        => _inner.TryParseMessage(ref input, binder, out message);

    public void WriteMessage(HubMessage message, IBufferWriter<byte> output) => _inner.WriteMessage(message, output);

    public ReadOnlyMemory<byte> GetMessageBytes(HubMessage message) => _inner.GetMessageBytes(message);
}
