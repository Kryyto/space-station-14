using Content.Server.Chat.Translation;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Chat.Commands;

[AnyCommand]
internal sealed class SetLanguageCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _e = default!;

    public string Command => "setlang";
    public string Description => "Set your preferred chat translation language (e.g. setlang fr).";
    public string Help => "setlang <language_code>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length < 1)
        {
            shell.WriteLine($"Current language: {_e.System<ChatTranslationSystem>().LanguageManager.GetLanguage(player)}");
            return;
        }

        var code = args[0].Trim().ToLowerInvariant();
        if (code.Length is 0 or > 8)
        {
            shell.WriteError("Invalid language code.");
            return;
        }

        _e.System<ChatTranslationSystem>().LanguageManager.SetLanguage(player.UserId, code);
        shell.WriteLine($"Translation language set to: {code}");
    }
}
