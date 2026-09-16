using System;
using System.IO;
using System.Threading.Tasks;
using Lisan.Services;
using Lisan.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Lisan;

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
        MigrateLegacyConfig();
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();

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
            HelpMessage = $"Toggle {Brand.Name}. '{Brand.Command} config' | '{Brand.Command} lang ar' | '{Brand.Command} test'",
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
            Log.Information($"{Brand.Name} language: {cur.Code} ({cur.Name}). Example: {Brand.Command} lang ar");
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
            && !AddonDialogueWatcher.IsNamedAddonVisible("TalkSubtitle")
            && !AddonDialogueWatcher.IsNamedAddonVisible("SelectString")
            && !AddonDialogueWatcher.IsNamedAddonVisible("SelectIconString")
            && !AddonDialogueWatcher.IsNamedAddonVisible("CutSceneSelectString"))
        {
            Log.Information($"{Brand.Name} test: open Talk/BattleTalk/choices first, then {Brand.Command} test");
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

    /// <summary>
    /// Copy settings JSON and cache from the old DialogueAr config dir if Lisan has none yet.
    /// </summary>
    private static void MigrateLegacyConfig()
    {
        try
        {
            var newDir = PluginInterface.GetPluginConfigDirectory();
            var root = Path.GetDirectoryName(newDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(root))
                return;

            var newJson = Path.Combine(root, $"{Brand.InternalName}.json");
            var oldJson = Path.Combine(root, $"{Brand.LegacyInternalName}.json");
            if (File.Exists(oldJson) && !File.Exists(newJson))
                File.Copy(oldJson, newJson);

            var oldDir = Path.Combine(root, Brand.LegacyInternalName);
            if (!Directory.Exists(oldDir))
                return;

            Directory.CreateDirectory(newDir);
            foreach (var file in Directory.EnumerateFiles(oldDir))
            {
                var dest = Path.Combine(newDir, Path.GetFileName(file));
                if (!File.Exists(dest))
                    File.Copy(file, dest);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Could not migrate {Brand.LegacyInternalName} config to {Brand.InternalName}");
        }
    }
}
