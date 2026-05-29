using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Robust.Shared.Log;

namespace Content.Server.Chat.Translation;

/// <summary>
///     Async translation client that calls the local LibreTranslate sidecar.
///     Fire-and-recall pattern: results are queued back and drained each game tick.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ISawmill _log;

    /// <summary>Base URL of the LibreTranslate sidecar (e.g. "http://localhost:5000").</summary>
    public string SidecarUrl { get; set; } = "http://localhost:5000";

    /// <summary>Hard timeout per HTTP call. Async so does not block the game loop.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a cached translation remains valid.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    ///     SS14 terms that should not be translated.
    ///     Passed as <c>dontTranslate</c> to LibreTranslate.
    /// </summary>
    public IReadOnlyList<string> ProtectedTerms { get; set; } = new[]
    {
        "atmos", "sec", "CE", "AI", "HoP", "HoS", "CMO", "RD", "NT",
        "Nanotrasen", "syndicate", "nukies", "borgs", "borg",
    };

    // Cache: key = (hash, sourceLang, targetLang)
    private readonly Dictionary<(string Hash, string Src, string Tgt), CacheEntry> _cache = new();

    // Pending fire-and-recall callbacks, drained each tick by ChatTranslationSystem.Update()
    private readonly ConcurrentQueue<Action> _pendingCallbacks = new();

    public TranslationService(ISawmill log)
    {
        _log = log;
        _http = new HttpClient { Timeout = RequestTimeout };
    }

    /// <summary>
    ///     Request a translation. If the result is already cached, <paramref name="callback"/> is
    ///     enqueued immediately with the cached value. Otherwise the HTTP call is fired on the thread
    ///     pool and the callback is enqueued when it completes (or on timeout/error, with the original).
    /// </summary>
    public void RequestTranslation(string text, string sourceLang, string targetLang, Action<string> callback)
    {
        if (sourceLang == targetLang)
        {
            _pendingCallbacks.Enqueue(() => callback(text));
            return;
        }

        var hash = ComputeHash(text);
        var key = (hash, sourceLang, targetLang);

        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired(CacheTtl))
        {
            _pendingCallbacks.Enqueue(() => callback(entry.TranslatedText));
            return;
        }

        // Fire on thread pool; result re-enters via _pendingCallbacks
        _ = Task.Run(async () =>
        {
            var result = await CallSidecarAsync(text, sourceLang, targetLang);
            _cache[key] = new CacheEntry(result, DateTime.UtcNow);
            _pendingCallbacks.Enqueue(() => callback(result));
        });
    }

    /// <summary>Drain all pending callbacks accumulated since the last call. Call this once per game tick.</summary>
    public void DrainCallbacks()
    {
        while (_pendingCallbacks.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _log.Error($"[Translation] Callback threw: {ex}");
            }
        }
    }

    private async Task<string> CallSidecarAsync(string text, string sourceLang, string targetLang)
    {
        try
        {
            var payload = new TranslateRequest
            {
                Text = text,
                Source = sourceLang,
                Target = targetLang,
                DontTranslate = ProtectedTerms,
            };

            using var cts = new CancellationTokenSource(RequestTimeout);
            var response = await _http.PostAsJsonAsync($"{SidecarUrl}/translate", payload, cts.Token);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TranslateResponse>(cancellationToken: cts.Token);
            return result?.TranslatedText ?? text;
        }
        catch (OperationCanceledException)
        {
            _log.Warning($"[Translation] Timeout translating '{sourceLang}'→'{targetLang}'");
        }
        catch (Exception ex)
        {
            _log.Warning($"[Translation] Error: {ex.Message}");
        }

        return text; // fallback: original
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16]; // first 8 bytes is plenty for a key
    }

    public void Dispose() => _http.Dispose();

    // ── DTOs ─────────────────────────────────────────────────────────────────

    private sealed class TranslateRequest
    {
        [JsonPropertyName("q")] public string Text { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "en";
        [JsonPropertyName("target")] public string Target { get; set; } = "en";
        [JsonPropertyName("dontTranslate")] public IReadOnlyList<string>? DontTranslate { get; set; }
    }

    private sealed class TranslateResponse
    {
        [JsonPropertyName("translatedText")] public string? TranslatedText { get; set; }
    }

    private readonly record struct CacheEntry(string TranslatedText, DateTime CachedAt)
    {
        public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - CachedAt > ttl;
    }
}
