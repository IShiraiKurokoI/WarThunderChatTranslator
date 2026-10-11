#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;

namespace WarThunderChatTranslator.Tests.TestSupport;

internal sealed record MockRequest(string Method, string Path, string Query, string? Body);

/// <summary>
/// An in-process HTTP server at the HttpMessageHandler boundary: exactly the GPT-SoVITS API
/// contract is served, without TCP sockets, Python, CUDA, weights, or audio model files.
/// Its response hooks exercise failures, cancellation and asynchronous request interleaving.
/// </summary>
internal sealed class MockGptSoVitsHttpServer : HttpMessageHandler
{
    private readonly ConcurrentQueue<MockRequest> _requests = new();
    public IReadOnlyList<MockRequest> Requests => _requests.ToArray();
    public Func<MockRequest, CancellationToken, Task<HttpResponseMessage>>? Responder { get; set; }
    public bool AdvertiseTtsEndpoint { get; set; } = true;
    public byte[] AudioBytes { get; set; } = CreateWave();

    public int Count(string path) => _requests.Count(request => request.Path == path);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var route = new MockRequest(request.Method.Method, request.RequestUri!.AbsolutePath,
            request.RequestUri.Query, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        _requests.Enqueue(route);
        if (Responder is not null) return await Responder(route, cancellationToken);
        return route.Path switch
        {
            "/openapi.json" when route.Method == "GET" => Json(AdvertiseTtsEndpoint ? "{\"paths\":{\"/tts\":{}}}" : "{\"paths\":{}}"),
            "/set_gpt_weights" when route.Method == "GET" => Json("{\"message\":\"ok\"}"),
            "/set_sovits_weights" when route.Method == "GET" => Json("{\"message\":\"ok\"}"),
            "/tts" when route.Method == "POST" => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(AudioBytes)
            },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"message\":\"unknown endpoint\"}", Encoding.UTF8, "application/json")
            }
        };
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static byte[] CreateWave()
    {
        // Minimal deterministic PCM WAV payload. No model/audio runtime is loaded.
        var bytes = new byte[48];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BitConverter.GetBytes(40).CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes, 8);
        BitConverter.GetBytes(16).CopyTo(bytes, 16);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 20);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 22);
        BitConverter.GetBytes(8000).CopyTo(bytes, 24);
        BitConverter.GetBytes(16000).CopyTo(bytes, 28);
        BitConverter.GetBytes((short)2).CopyTo(bytes, 32);
        BitConverter.GetBytes((short)16).CopyTo(bytes, 34);
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        BitConverter.GetBytes(4).CopyTo(bytes, 40);
        return bytes;
    }
}
