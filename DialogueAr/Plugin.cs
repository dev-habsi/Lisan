using System;
using System.IO;
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
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;

    private const string CommandName = "/dialogar";

    public static Configuration Configuration { get; private set; } = null!;
    public static DialogueTtsService Tts { get; private set; } = null!;

    public TranslationCache Cache { get; }
    public LocalTranslator Translator { get; }
    public AddonDialogueWatcher Watcher { get; }

    public readonly WindowSystem WindowSystem = new("DialogueAr");
    private ConfigWindow ConfigWindow { get; }
    public DialogueOverlay Overlay { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Tts = new DialogueTtsService();

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
            HelpMessage = "Toggle DialogueAr. '/dialogar config' | '/dialogar lang <code>' | '/dialogar test' | '/dialogar stop'",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

        Overlay.IsOpen = Configuration.ShowOverlay;

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

            if (Configuration.TtsWarmupOnLoad && Configuration.EnableTts)
            {
                try { Tts.EnsureReady(); }
                catch (Exception ex) { Log.Warning(ex, "TTS warm-up failed"); }
            }
        });

        var lang = TargetLanguages.Current();
        Log.Information($"DialogueAr loaded. Target={lang.Code} ({lang.Name}). Run Models/setup-models.ps1 -Language {lang.Code} if the model is missing.");
    }

    public void Dispose()
    {
        Framework.Update -= Watcher.OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        Overlay.Dispose();
        Watcher.Dispose();
        Tts.Dispose();
        Translator.Dispose();
        Cache.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }

    /// <summary>Apply a new target language: restart worker, reload overlay font, drop stale line.</summary>
    public void ApplyLanguage()
    {
        Watcher.Clear();
        Tts.Stop();
        Translator.Restart();
        Overlay.ReloadFont();

        var lang = TargetLanguages.Current();
        Log.Information($"DialogueAr language → {lang.Code} ({lang.Name}). Model: {Translator.ModelDirectory}");

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
            case "stop":
                Tts.Stop();
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
            Log.Information($"DialogueAr language: {cur.Code} ({cur.Name}). Example: /dialogar lang fr");
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
            Log.Information("DialogueAr test: open Talk/BattleTalk first, then /dialogar test");
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
            Watcher.SetTestLineForSource(source, sample, translated);
            Tts.Speak(translated, ObjectTable.LocalPlayer?.Name.TextValue);
        });
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
