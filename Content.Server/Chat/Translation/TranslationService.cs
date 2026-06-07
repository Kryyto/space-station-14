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
///     Async translation client that calls the DeepL API directly.
///     Fire-and-recall pattern: results are queued back and drained each game tick.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ISawmill _log;

    /// <summary>
    ///     DeepL API key. Set this before the first translation request.
    ///     Use the Free endpoint (api-free.deepl.com) for free-tier keys,
    ///     or the Pro endpoint (api.deepl.com) for paid keys.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    ///     DeepL API base URL.
    ///     Free tier:  https://api-free.deepl.com/v2
    ///     Pro tier:   https://api.deepl.com/v2
    /// </summary>
    public string DeepLUrl { get; set; } = "https://api-free.deepl.com/v2";

    /// <summary>Hard timeout per HTTP call. Async so does not block the game loop.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a cached translation remains valid.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(10);

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
            var result = await CallDeepLAsync(text, sourceLang, targetLang);
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

    private async Task<string> CallDeepLAsync(string text, string sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            _log.Warning("[Translation] DeepL API key is not set — returning original text.");
            return text;
        }

        try
        {
            // DeepL expects uppercase language codes (e.g. "FR", "EN-US").
            var payload = new DeepLRequest
            {
                Text = new[] { text },
                SourceLang = sourceLang.ToUpperInvariant(),
                TargetLang = targetLang.ToUpperInvariant(),
            };

            using var cts = new CancellationTokenSource(RequestTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{DeepLUrl}/translate");
            request.Headers.Add("Authorization", $"DeepL-Auth-Key {ApiKey}");
            request.Content = JsonContent.Create(payload);

            var response = await _http.SendAsync(request, cts.Token);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<DeepLResponse>(cancellationToken: cts.Token);
            return result?.Translations?[0].Text ?? text;
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

    private sealed class DeepLRequest
    {
        [JsonPropertyName("text")] public string[] Text { get; set; } = Array.Empty<string>();
        [JsonPropertyName("source_lang")] public string SourceLang { get; set; } = "EN";
        [JsonPropertyName("target_lang")] public string TargetLang { get; set; } = "EN";
    }

    private sealed class DeepLResponse
    {
        [JsonPropertyName("translations")] public DeepLTranslation[]? Translations { get; set; }
    }

    private sealed class DeepLTranslation
    {
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("detected_source_language")] public string? DetectedSourceLanguage { get; set; }
    }

    private readonly record struct CacheEntry(string TranslatedText, DateTime CachedAt)
    {
        public bool IsExpired(TimeSpan ttl) => DateTime.UtcNow - CachedAt > ttl;
    }
}
