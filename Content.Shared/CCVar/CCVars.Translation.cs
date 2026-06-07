using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     DeepL API authentication key.
    ///     Free-tier keys use api-free.deepl.com, paid keys use api.deepl.com.
    ///     Leave empty to disable automatic chat translation.
    /// </summary>
    public static readonly CVarDef<string> TranslationDeepLApiKey =
        CVarDef.Create("translation.deepl_api_key", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    ///     DeepL API base URL.
    ///     Free tier:  https://api-free.deepl.com/v2
    ///     Pro tier:   https://api.deepl.com/v2
    /// </summary>
    public static readonly CVarDef<string> TranslationDeepLUrl =
        CVarDef.Create("translation.deepl_url", "https://api-free.deepl.com/v2", CVar.SERVERONLY);
}
