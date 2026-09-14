using System;
using System.Threading.Tasks;
using DialogueAr.Services;
using DialogueAr.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DialogueAr;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = Brand.Command;
    private const string LegacyCommandName = Brand.LegacyCommand;

    public static Configuration Configuration { get; private set; } = null!;

    public TranslationCache Cache { get; }
    public LocalTranslator Translator { get; }
    public AddonDialogueWatcher Watcher { get; }

    public readonly WindowSystem WindowSystem = new(Brand.Name);
    private ConfigWindow ConfigWindow { get; }
    public DialogueOverlay Overlay { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.MigrateOverlayTheme();

        var pluginDir = PluginInterface.AssemblyLocation.Directory?.FullName
                        ?? AppContext.BaseDirectory;
        var cacheDir = PluginInterface.GetPluginConfigDirectory();

        Cache = new TranslationCache(cacheDir);
        Translator = new LocalTranslator(Cache, pluginDir, Configuration.ModelDirectory);
        Watcher = new AddonDialogueWatcher(Translator);
        Watcher.Register();
        Framework.Update += Watcher.OnFrameworkUpdate;

        ConfigWindow = new ConfigWindow(this);
        Overlay = new DialogueOverlay(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(Overlay);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Toggle {Brand.Name}. '{Brand.Command} config' | '{Brand.Command} lang fr' | '{Brand.Command} test'",
        });
        CommandManager.AddHandler(LegacyCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Same as {Brand.Command} (old name).",
            ShowInHelp = false,
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;
        // Story Talk is a cutscene: Dalamud hides plugin ImGui unless this is on.
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.DisableAutomaticUiHide = true;

        Overlay.IsOpen = true;

        Task.Run(() =>
        {
            if (Configuration.WarmupTranslatorOnLoad)
            {
                try
                {
                    Translator.EnsureStarted();
                    _ = Translator.Translate("Hello.");
                }
                catch (Exception ex) { Log.Warning(ex, "Translator warm-up failed"); }
            }
        });

        var lang = TargetLanguages.Current();
        Log.Information($"{Brand.Name} loaded. Target={lang.Code} ({lang.Name}). With love from {Brand.SiteLabel}. Run Models/setup-models.ps1 -Language {lang.Code} if the model is missing.");
    }

    public void Dispose()
    {
        Framework.Update -= Watcher.OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        Overlay.Dispose();
        Watcher.Dispose();
        Translator.Dispose();
        Cache.Dispose();
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(LegacyCommandName);
    }

    /// <summary>Apply a new target language: restart worker, reload overlay font, drop stale line.</summary>
    public void ApplyLanguage()
    {
        Watcher.Clear();
        Translator.Restart();
        Overlay.ReloadFont();

        var lang = TargetLanguages.Current();
        Log.Information($"{Brand.Name} language → {lang.Code} ({lang.Name}). Model: {Translator.ModelDirectory}");

        if (Configuration.WarmupTranslatorOnLoad)
        {
            Task.Run(() =>
            {
                try
                {
                    Translator.EnsureStarted();
                    _ = Translator.Translate("Hello.");
                }
                catch (Exception ex) { Log.Warning(ex, "Translator restart warm-up failed"); }
            });
        }
    }

    private void OnCommand(string command, string args)
    {
        var raw = args.Trim();
        var arg = raw.ToLowerInvariant();
        if (arg.StartsWith("lang", StringComparison.Ordinal))
        {
            var rest = raw.Length > 4 ? raw[4..].Trim() : "";
            SetLanguageFromCommand(rest);
            return;
        }

        switch (arg)
        {
            case "config":
            case "settings":
            case "cfg":
                ToggleConfigUi();
                break;
            case "test":
                RunTestTranslation();
                break;
            default:
                Configuration.Enabled = !Configuration.Enabled;
                Configuration.Save();
                break;
        }
    }

    private void SetLanguageFromCommand(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            var cur = TargetLanguages.Current();
            Log.Information($"{Brand.Name} language: {cur.Code} ({cur.Name}). Example: {Brand.Command} lang fr");
            return;
        }

        Configuration.TargetLanguage = TargetLanguages.NormalizeCode(code);
        Configuration.Save();
        ApplyLanguage();
    }

    public void RunTestTranslation()
    {
        if (!AddonDialogueWatcher.IsNamedAddonVisible("Talk")
            && !AddonDialogueWatcher.IsNamedAddonVisible("_BattleTalk")
            && !AddonDialogueWatcher.IsNamedAddonVisible("TalkSubtitle"))
        {
            Log.Information($"{Brand.Name} test: open Talk/BattleTalk first, then {Brand.Command} test");
            return;
        }

        const string sample = "The path ahead is dangerous. Stay close, and do not lose heart.";
        var source = AddonDialogueWatcher.IsNamedAddonVisible("Talk") ? "Talk"
            : AddonDialogueWatcher.IsNamedAddonVisible("_BattleTalk") ? "BattleTalk"
            : "TalkSubtitle";

        var busy = TargetLanguages.Current().BusyText;
        Watcher.SetTestLineForSource(source, sample, busy);
        Overlay.IsOpen = true;

        Task.Run(() =>
        {
            var translated = Translator.Translate(sample);
            _ = Framework.RunOnFrameworkThread(() =>
            {
                Watcher.SetTestLineForSource(source, sample, translated);
            });
        });
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
