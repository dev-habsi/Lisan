# Contributing to Lisan

Thanks for helping. Issues and pull requests are welcome at https://github.com/dev-habsi/Lisan.

## Build from source

Requirements: Windows, .NET 10 SDK, XIVLauncher with Dalamud installed and launched at least once (the SDK references `%AppData%\XIVLauncher\addon\Hooks\dev`), Python 3.10+ on PATH.

```powershell
git clone https://github.com/dev-habsi/Lisan.git
cd Lisan
.\Install-Lisan.ps1          # pip packages + Arabic pack + Release build + Dev Plugin registration
```

Or by hand:

```powershell
cd Lisan
dotnet build -c Release -p:Platform=x64
```

Output: `Lisan\bin\x64\Release\` (add it as a Dev Plugin in Dalamud Settings → Experimental). The release zip and manifest are in `Lisan\bin\x64\Release\Lisan\`.

Language packs for development can live in `Lisan\Models\<folder>` (ignored by git) or in the plugin config folder (`%AppData%\XIVLauncher\pluginConfigs\Lisan\Models`), which is where the in-game Setup tab puts them.

## Project layout

```
Lisan/
├── Plugin.cs                 # Dalamud entry, commands, hotkeys, language apply
├── Configuration.cs          # settings + migrations (bump Version for new migrations)
├── TargetLanguages.cs        # built-in language catalog (code, repo, folder, token, font)
├── Services/
│   ├── AddonDialogueWatcher.cs   # reads Talk/BattleTalk/… text nodes, writes translations back
│   ├── LocalTranslator.cs        # Python worker process, JSON lines over stdin/stdout
│   ├── SetupRunner.cs            # runs setup-models.ps1 from the Setup tab
│   ├── TranslationCache.cs       # per-language cache in the config folder
│   ├── DialogueHistory.cs        # last 250 lines, export
│   ├── DialogueDisplay.cs        # Arabic shaping + wrapping for the overlay
│   └── SpeechService.cs          # optional Windows Arabic TTS
├── Windows/                  # ConfigWindow, HistoryWindow, DialogueOverlay
├── Models/
│   ├── translate_worker.py   # the translator (transformers + torch, CPU)
│   └── setup-models.ps1      # pip + Hugging Face download + font
├── Fonts/                    # bundled Noto Naskh Arabic (+ OFL.txt)
└── images/icon.png
```

## Adding a built-in language

Keep three places in sync:

1. `TargetLanguages.cs` → a `Lang(...)` row: code, name, Hugging Face repo, folder (last segment of the repo id), optional Opus target token (`>>xxx<<` when the model card says "a sentence initial language token is required"), font + glyph ranges if the game font cannot draw the script.
2. `setup-models.ps1` → the `$Packs` hashtable row (repo, folder, font file + URL).
3. `README.md` → the languages table.

Check the repo exists first: `https://huggingface.co/api/models/Helsinki-NLP/<repo>` must return 200. Several `opus-mt-en-xx` pairs do not exist; multilingual models (`opus-mt-en-mul`, `opus-mt-en-sla`, …) with a target token are the fallback.

## Pull requests

- One topic per PR. Keep the diff small; match the existing style (file-scoped namespaces, `var`, explicit `Plugin.Log` logging).
- Anything that reads or writes game memory stays in `AddonDialogueWatcher.cs` and must check `IsVisible` first.
- Do not commit model weights, fonts other than the bundled Arabic one, or build output. `.gitignore` already blocks them.
- Run `dotnet build -c Release -p:Platform=x64` with 0 warnings before pushing. CI builds every PR.

## Releasing

1. Bump `<Version>` in `Lisan/Lisan.csproj` and add a section to `CHANGELOG.md`.
2. Commit to `main`, then tag: `git tag v1.2.3 && git push origin main v1.2.3`.
3. The **Release** workflow builds against the current Dalamud, publishes `latest.zip` on the GitHub release, and commits an updated `pluginmaster.json` to `main`. Users on the custom repo get the update automatically.

When Dalamud bumps its API level, bump `Dalamud.NET.Sdk/XX.0.0` in the csproj and fix compile errors first; `pluginmaster.json` picks up the new `DalamudApiLevel` from the build.
