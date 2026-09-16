using System;
using System.IO;
using System.Threading.Tasks;
using Lisan.Services;
using Lisan.Windows;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
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
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;

    private const string CommandName = Brand.Command;
    private const string LegacyCommandName = Brand.LegacyCommand;

    public static Configuration Configuration { get; private set; } = null!;
    public static DialogueHistory History { get; private set; } = null!;

    public TranslationCache Cache { get; }
    public LocalTranslator Translator { get; }
    public static SpeechService Tts { get; private set; } = null!;
    public AddonDialogueWatcher Watcher { get; }
    public ChatTranslator Chat { get; }
    private ChatWindow ChatWindow { get; }

    public readonly WindowSystem WindowSystem = new(Brand.Name);
    private ConfigWindow ConfigWindow { get; }
    private HistoryWindow HistoryWindow { get; }
    public DialogueOverlay Overlay { get; }

    private static string? pendingClipboard;
    private bool prevHotCopy;
    private bool prevHotHist;
    private bool prevHotSpeak;
    private bool prevHotStop;
    private bool prevHotChat;

    public Plugin()
    {
        MigrateLegacyConfig();
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Migrate();

        var pluginDir = PluginInterface.AssemblyLocation.Directory?.FullName
                        ?? AppContext.BaseDirectory;
        var cacheDir = PluginInterface.GetPluginConfigDirectory();

        Cache = new TranslationCache(cacheDir);
        History = new DialogueHistory(cacheDir);
        Translator = new LocalTranslator(Cache, pluginDir, Configuration.ModelDirectory);
        Tts = new SpeechService();
        Watcher = new AddonDialogueWatcher(Translator);
        Watcher.Register();
        Chat = new ChatTranslator(Translator);
        Chat.Register();
        Framework.Update += OnFrameworkUpdate;

        ConfigWindow = new ConfigWindow(this);
        HistoryWindow = new HistoryWindow(this);
        ChatWindow = new ChatWindow(this);
        Overlay = new DialogueOverlay(this);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(HistoryWindow);
        WindowSystem.AddWindow(ChatWindow);
        WindowSystem.AddWindow(Overlay);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Toggle {Brand.Name}. {Brand.Command} config | chat | history | copy | speak | stop | lang ar",
        });
        CommandManager.AddHandler(LegacyCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Same as {Brand.Command} (old name).",
            ShowInHelp = false,
        });

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;
        // Story Talk is a cutscene: Dalamud hides plugin ImGui unless this is on.
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.DisableAutomaticUiHide = true;

        Overlay.IsOpen = true;
        if (Configuration.TranslateChat)
            ChatWindow.IsOpen = true;

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
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;

        WindowSystem.RemoveAllWindows();
        Overlay.Dispose();
        ChatWindow.Dispose();
        Watcher.Dispose();
        Chat.Dispose();
        Tts.Dispose();
        Translator.Dispose();
        History.Dispose();
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
        ChatWindow.ReloadFont();

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
            case "history":
            case "hist":
            case "log":
                ToggleHistoryUi();
                break;
            case "chat":
                ToggleChatUi();
                break;
            case "copy":
                CopyLastTranslation();
                break;
            case "speak":
            case "replay":
            case "tts":
                if (!Configuration.EnableTts)
                    Log.Information($"{Brand.Name}: enable TTS in config first.");
                else
                    Tts.Replay();
                break;
            case "stop":
                Tts.Stop();
                break;
            case "export":
            {
                var file = History.WriteExportFile();
                Log.Information(file is null
                    ? $"{Brand.Name}: could not export history."
                    : $"{Brand.Name} history exported: {file}");
                break;
            }
            case "help":
                Log.Information($"{Brand.Name}: {Brand.Command} | config | chat | history | copy | speak | stop | export | lang ar | test");
                break;
            case "status":
            {
                var cur = TargetLanguages.Current();
                Log.Information($"{Brand.Name}: {(Configuration.Enabled ? "on" : "off")} · {cur.Code} ({cur.Name}) · TTS {(Configuration.EnableTts ? Tts.Status : "off")} · history {History.Count}");
                break;
            }
            case "test":
                RunTestTranslation();
                break;
            default:
                Configuration.Enabled = !Configuration.Enabled;
                Configuration.Save();
                if (!Configuration.Enabled)
                    Tts.Stop();
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
            && !AddonDialogueWatcher.IsNamedAddonVisible("CutSceneSelectString")
            && !AddonDialogueWatcher.IsNamedAddonVisible("SelectYesno"))
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
                History.Add(source, "", sample, translated);
                Tts.SpeakDialogue(translated);
            });
        });
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();

    public void ToggleHistoryUi() => HistoryWindow.Toggle();

    public void ToggleChatUi() => ChatWindow.Toggle();

    public void ShowChatUi() => ChatWindow.IsOpen = true;

    public static void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        pendingClipboard = text;
    }

    public void CopyLastTranslation()
    {
        var text = History.Last?.Translation;
        if (string.IsNullOrWhiteSpace(text))
        {
            Log.Information($"{Brand.Name}: no translation to copy yet.");
            return;
        }

        CopyText(text);
        Log.Information($"{Brand.Name}: copied last translation.");
    }

    private void OnDraw()
    {
        var clip = pendingClipboard;
        if (clip is not null)
        {
            pendingClipboard = null;
            try { ImGui.SetClipboardText(clip); }
            catch (Exception ex) { Log.Warning(ex, "Clipboard copy failed"); }
        }

        WindowSystem.Draw();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        Watcher.OnFrameworkUpdate(framework);
        PollHotkeys();
    }

    private void PollHotkeys()
    {
        if (!Configuration.EnableHotkeys)
        {
            prevHotCopy = prevHotHist = prevHotSpeak = prevHotStop = prevHotChat = false;
            return;
        }

        var ctrl = Down(VirtualKey.CONTROL) || Down(VirtualKey.LCONTROL) || Down(VirtualKey.RCONTROL);
        var alt = Down(VirtualKey.MENU) || Down(VirtualKey.LMENU) || Down(VirtualKey.RMENU);
        if (!ctrl || !alt)
        {
            prevHotCopy = prevHotHist = prevHotSpeak = prevHotStop = prevHotChat = false;
            return;
        }

        Edge(VirtualKey.C, ref prevHotCopy, CopyLastTranslation);
        Edge(VirtualKey.H, ref prevHotHist, ToggleHistoryUi);
        Edge(VirtualKey.L, ref prevHotChat, ToggleChatUi);
        Edge(VirtualKey.S, ref prevHotSpeak, () =>
        {
            if (Configuration.EnableTts)
                Tts.Replay();
        });
        Edge(VirtualKey.X, ref prevHotStop, Tts.Stop);
    }

    private static bool Down(VirtualKey key)
    {
        try { return KeyState[key]; }
        catch { return false; }
    }

    private static void Edge(VirtualKey key, ref bool prev, Action act)
    {
        var now = Down(key);
        if (now && !prev)
            act();
        prev = now;
    }

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
