using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Chat.Translation;

/// <summary>
///     Stores per-player preferred language codes (e.g. "fr", "ru", "en").
///     Falls back to the server default when a player has no preference set.
/// </summary>
public sealed class PlayerLanguageManager
{
    private readonly Dictionary<NetUserId, string> _languages = new();

    /// <summary>The language code used when a player has no explicit preference.</summary>
    public string DefaultLanguage { get; set; } = "en";

    public void SetLanguage(NetUserId userId, string languageCode)
    {
        _languages[userId] = languageCode.ToLowerInvariant().Trim();
    }

    public string GetLanguage(NetUserId userId)
    {
        return _languages.TryGetValue(userId, out var lang) ? lang : DefaultLanguage;
    }

    public string GetLanguage(ICommonSession session)
    {
        return GetLanguage(session.UserId);
    }

    public void RemovePlayer(NetUserId userId)
    {
        _languages.Remove(userId);
    }
}
