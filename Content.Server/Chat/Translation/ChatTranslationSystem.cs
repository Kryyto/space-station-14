using Content.Server.Chat.Managers;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Chat.Translation;

/// <summary>
///     Orchestrates per-player chat translation.
///     - Listens to <see cref="BeforeVoiceRangeSendEvent"/> and <see cref="BeforeRadioSendEvent"/>
///       raised by the patched ChatSystem / RadioSystem.
///     - Groups recipients by target language, fires one translation request per group,
///       then sends the translated text once the fire-and-recall result arrives.
///     - Drains pending callbacks once per game tick via <see cref="Update"/>.
/// </summary>
public sealed class ChatTranslationSystem : EntitySystem
{
    [Dependency] private IChatManager _chatManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    public PlayerLanguageManager LanguageManager { get; } = new();
    private TranslationService _translation = default!;
    private ISawmill _log = default!;

    public override void Initialize()
    {
        base.Initialize();

        _log = Logger.GetSawmill("translation");
        _translation = new TranslationService(_log)
        {
            ApiKey = _cfg.GetCVar(CCVars.TranslationDeepLApiKey),
            DeepLUrl = _cfg.GetCVar(CCVars.TranslationDeepLUrl),
        };

        _cfg.OnValueChanged(CCVars.TranslationDeepLApiKey, v => _translation.ApiKey = v);
        _cfg.OnValueChanged(CCVars.TranslationDeepLUrl, v => _translation.DeepLUrl = v);

        SubscribeLocalEvent<BeforeVoiceRangeSendEvent>(OnBeforeVoiceRange);
        SubscribeLocalEvent<BeforeRadioSendEvent>(OnBeforeRadio);

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
        _translation.Dispose();
        base.Shutdown();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
            LanguageManager.RemovePlayer(args.Session.UserId);
    }

    /// <summary>Drain fire-and-recall callbacks once per tick.</summary>
    public override void Update(float frameTime)
    {
        _translation.DrainCallbacks();
    }

    // ── Voice-range (say / whisper / LOOC / emote) ───────────────────────────

    private void OnBeforeVoiceRange(ref BeforeVoiceRangeSendEvent ev)
    {
        var sourceLang = ResolveSenderLanguage(ev.Source);
        var groups = GroupByLanguage(ev.Recipients.Keys);
        _log.Debug($"[Translation] VoiceRange event: {ev.Recipients.Count} recipients, srcLang={sourceLang}, groups=[{string.Join(",", groups.Keys)}]");

        // Snapshot channel / source / rewrap before the dict gets cleared.
        var channel = ev.Channel;
        var source = ev.Source;
        var message = ev.Message;
        var rewrap = ev.RewrapCallback;

        foreach (var (targetLang, sessions) in groups)
        {
            // Snapshot per-session hide-chat flag for async callback.
            var sessionData = new List<(ICommonSession Session, bool HideChat)>();
            foreach (var s in sessions)
            {
                if (ev.Recipients.TryGetValue(s, out var d))
                    sessionData.Add((s, d.HideChat));
            }

            _log.Debug($"[Translation] Requesting {sourceLang}→{targetLang} for {sessionData.Count} sessions: \"{message}\"");
            _translation.RequestTranslation(message, sourceLang, targetLang, translated =>
            {
                _log.Debug($"[Translation] Got result {sourceLang}→{targetLang}: \"{translated}\"");
                var translatedWrapped = rewrap(translated);
                foreach (var (session, hideChat) in sessionData)
                {
                    _chatManager.ChatMessageToOne(
                        channel,
                        translated,
                        translatedWrapped,
                        source,
                        hideChat,
                        session.Channel);
                }
            });
        }

        // Mark all recipients as handled; ChatSystem will skip its own send.
        ev.Recipients.Clear();
    }

    // ── Radio ─────────────────────────────────────────────────────────────────

    private void OnBeforeRadio(ref BeforeRadioSendEvent ev)
    {
        var sourceLang = ResolveSenderLanguage(ev.MessageSource);
        var messageSource = ev.MessageSource;
        var message = ev.Message;
        var rewrap = ev.RewrapCallback;

        // Map INetChannel → language preference
        var channelToSession = new Dictionary<INetChannel, ICommonSession>();
        foreach (var session in _playerManager.Sessions)
            channelToSession[session.Channel] = session;

        var groups = new Dictionary<string, List<INetChannel>>();
        foreach (var netChannel in ev.Recipients.Keys)
        {
            var lang = channelToSession.TryGetValue(netChannel, out var s)
                ? LanguageManager.GetLanguage(s)
                : LanguageManager.DefaultLanguage;

            if (!groups.TryGetValue(lang, out var list))
                groups[lang] = list = new List<INetChannel>();
            list.Add(netChannel);
        }

        // Snapshot before clear
        foreach (var (targetLang, channels) in groups)
        {
            var capturedChannels = new List<INetChannel>(channels);

            _translation.RequestTranslation(message, sourceLang, targetLang, translated =>
            {
                var translatedWrapped = rewrap(translated);
                _chatManager.ChatMessageToMany(
                    ChatChannel.Radio,
                    translated,
                    translatedWrapped,
                    messageSource,
                    hideChat: false,
                    recordReplay: false,
                    clients: capturedChannels);
            });
        }

        ev.Recipients.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ResolveSenderLanguage(EntityUid source)
    {
        if (TryComp<ActorComponent>(source, out var actor))
            return LanguageManager.GetLanguage(actor.PlayerSession);
        return LanguageManager.DefaultLanguage;
    }

    private Dictionary<string, List<ICommonSession>> GroupByLanguage(IEnumerable<ICommonSession> sessions)
    {
        var groups = new Dictionary<string, List<ICommonSession>>();
        foreach (var session in sessions)
        {
            var lang = LanguageManager.GetLanguage(session);
            if (!groups.TryGetValue(lang, out var list))
                groups[lang] = list = new List<ICommonSession>();
            list.Add(session);
        }
        return groups;
    }
}
