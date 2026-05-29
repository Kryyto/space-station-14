using Content.Shared.Chat;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Chat.Translation;

/// <summary>
///     Raised by ChatSystem just before IC voice-range messages are dispatched per-recipient.
///     Handlers may replace <see cref="Recipients"/> entries with language-specific variants,
///     or simply read them for translation fan-out.
/// </summary>
[ByRefEvent]
public struct BeforeVoiceRangeSendEvent
{
    /// <summary>Raw (untransformed) message text — translate THIS, not the wrapped version.</summary>
    public string Message;

    /// <summary>Pre-built wrapped message (markup + name). Used as template base.</summary>
    public string WrappedMessage;

    public EntityUid Source;
    public ChatChannel Channel;

    /// <summary>
    ///     Each entry maps a recipient session to the specific (message, wrappedMessage) they will receive.
    ///     Translation hooks may modify these per-session values before the final send.
    /// </summary>
    public Dictionary<ICommonSession, (string Message, string WrappedMessage, bool HideChat)> Recipients;

    /// <summary>
    ///     Callback that re-wraps a translated message using the same markup template as the original.
    ///     Signature: (translatedText) → wrappedMessage
    /// </summary>
    public Func<string, string> RewrapCallback;
}

/// <summary>
///     Raised by RadioSystem just before a radio message is dispatched.
/// </summary>
[ByRefEvent]
public struct BeforeRadioSendEvent
{
    public string Message;
    public string WrappedMessage;
    public EntityUid MessageSource;

    /// <summary>Maps recipient NetChannel to the message they will receive.</summary>
    public Dictionary<INetChannel, (string Message, string WrappedMessage)> Recipients;

    public Func<string, string> RewrapCallback;
}
