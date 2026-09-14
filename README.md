# Lisan

**Lisan** (لسان) means tongue, and language. Offline **English → any language** dialogue for Final Fantasy XIV.

Made with love at **[habsi.net](https://habsi.net)**.

Hooks **Talk**, **BattleTalk**, and **TalkSubtitle**, translates locally with **Helsinki-NLP Opus-MT** (nothing leaves your PC), and writes the line onto the balloon.

Default language is **Arabic**. Pick Japanese, Chinese, or any built-in pack in **General → Target language**.

**Current version:** 0.4.0  
**Dalamud:** API 15 (`.NET 10`)  
**Author:** [ALHABSI](https://habsi.net)

---

## Make it fully working

**One click** (does Python packages, language model + font, Release build, copies old DialogueAr settings, registers the Dalamud Dev Plugin):

```powershell
cd D:\Dev\Lisan
.\Install-Lisan.ps1
```

Or double-click **`Install-Lisan.cmd`**. Other language: `.\Install-Lisan.ps1 -Language ja`. Redo model: `-ForceModel`.

Then log in with Dalamud, reload plugins if the game was already open, `/lisan config`. Game client language = **English**.

You still need **FFXIV + XIVLauncher + Dalamud (API 15)** and **Python 3.10+** (PATH) on the machine. The installer checks those and stops with a link if they are missing.

Manual steps below if you do not want the installer.

---

### 1. Game + Dalamud

1. Install **FFXIV**, **XIVLauncher**, and **Dalamud**.
2. Confirm Dalamud is **API 15** / .NET 10 (plugin installer → Settings → Dalamud version).
3. You must be able to log in and see the Dalamud plugin installer.

### 2. Python 3 (translator engine)

1. Install **Python 3.10+** from https://www.python.org/downloads/  
   Tick **Add python.exe to PATH**.
2. Open a new PowerShell and check:

```powershell
python --version
```

If that fails, try `py -3 --version`. The plugin looks for `python` then `py`.

### 3. Build the plugin

```powershell
cd D:\Dev\Lisan\Lisan
dotnet build -c Release -p:Platform=x64
```

Output folder (this is the Dev Plugin path):

`D:\Dev\Lisan\Lisan\bin\x64\Release`

### 4. Download a language model + font

From the **source** `Models` folder (not only the bin copy — setup writes next to the script, then you rebuild or copy):

```powershell
cd D:\Dev\Lisan\Lisan\Models
.\setup-models.ps1 -Language list          # see every built-in code
.\setup-models.ps1 -Language ar            # Arabic (default)
.\setup-models.ps1 -Language ja            # Japanese (repo is opus-mt-en-jap)
```

What the script does:

1. Installs `torch` (CPU), `transformers`, `sentencepiece`, `huggingface_hub`.
2. Downloads the Hugging Face repo into `Models\<folder>\` (needs internet **once**).
3. Downloads an overlay font into `Fonts\` when the script needs one (Arabic, CJK, Hindi, Thai, Persian).

After download, **rebuild** so `bin\x64\Release\Models\...` and `Fonts\...` are current:

```powershell
cd D:\Dev\Lisan\Lisan
dotnet build -c Release -p:Platform=x64
```

Or copy `Models\<folder>` and `Fonts\` into the Release output yourself.

**Unknown language** (not in the list):

```powershell
.\setup-models.ps1 -Language xx -Repo Helsinki-NLP/opus-mt-en-xx
```

Then in-game pick **Custom model folder**, optional Opus token (`>>cmn_Hans<<` for some Chinese/TC models), RTL if needed, and point Translator at that folder.

### 5. Load it in Dalamud

If you previously loaded **DialogueAr**:

1. Unload the old plugin.
2. Remove the old Dev Plugin path (`...\DialogueAr\bin\x64\Release`).
3. **Before the first Lisan load**, copy settings and cache (the old `pluginConfigs\DialogueAr` folder is otherwise ignored):

```powershell
$cfg = "$env:APPDATA\XIVLauncher\pluginConfigs"
Copy-Item "$cfg\DialogueAr.json" "$cfg\Lisan.json" -Force -ErrorAction SilentlyContinue
Copy-Item "$cfg\DialogueAr" "$cfg\Lisan" -Recurse -Force
```

Models already live in the repo, so you should not need to re-download Arabic. The plugin also copies missing DialogueAr settings/cache into Lisan on first load.

Then:

1. Dalamud Settings → **Experimental** / **Dev Plugins**.
2. Add path: `D:\Dev\Lisan\Lisan\bin\x64\Release`
3. Enable **Lisan** and click **Reload**.
4. `/lisan config` → **General** → **Target language** = the code you downloaded.
5. **Translator** tab must say `Ready (opus-mt-…)` after the first line or after **Apply language / restart worker**.

`/lisan` is unchanged; `/dialogar` still works as an alias.

If status is `Model missing`, the folder with `config.json` + weights is not next to the loaded DLL. Run setup again and rebuild, or set **Custom model folder** to the absolute path of `Models\opus-mt-en-de` (etc.).

### 6. In-game balloon

1. Talk to an NPC (game client language should be **English** — that is the source text).
2. Translation writes onto the Talk / BattleTalk balloon.
3. First line can take 10–30s (Python + model warm-up). Later lines and cache hits are fast.
4. Scripts the game font cannot draw (Arabic, CJK, …) use the plugin font on the same balloon.

**Tofu / empty boxes** for Japanese, Chinese, Korean, Arabic, Hindi, Thai: font file missing. Re-run `setup-models.ps1 -Language <code>` and reload the plugin (font is loaded at apply/reload).

### 7. Switch language later

```powershell
cd D:\Dev\Lisan\Lisan\Models
.\setup-models.ps1 -Language de
```

Rebuild (or copy the new `Models` folder into Release), then in-game:

- `/lisan lang de`  
  or General tab dropdown → **Apply language / restart worker**

Cache is stored **per language**. Old Arabic cache stays valid.

---

## Built-in languages

| Code | Language | Hugging Face repo | Extra font |
|------|----------|-------------------|------------|
| `ar` | Arabic | `Helsinki-NLP/opus-mt-en-ar` | Noto Naskh Arabic |
| `de` | German | `Helsinki-NLP/opus-mt-en-de` | — |
| `es` | Spanish | `Helsinki-NLP/opus-mt-en-es` | — |
| `it` | Italian | `Helsinki-NLP/opus-mt-en-it` | — |
| `pt` | Portuguese | `Helsinki-NLP/opus-mt-tc-big-en-pt` | — |
| `nl` | Dutch | `Helsinki-NLP/opus-mt-en-nl` | — |
| `pl` | Polish | `Helsinki-NLP/opus-mt-en-pl` | — |
| `ru` | Russian | `Helsinki-NLP/opus-mt-en-ru` | — |
| `uk` | Ukrainian | `Helsinki-NLP/opus-mt-en-uk` | — |
| `tr` | Turkish | `Helsinki-NLP/opus-mt-en-tr` | — |
| `sv` | Swedish | `Helsinki-NLP/opus-mt-en-sv` | — |
| `da` | Danish | `Helsinki-NLP/opus-mt-en-da` | — |
| `fi` | Finnish | `Helsinki-NLP/opus-mt-en-fi` | — |
| `cs` | Czech | `Helsinki-NLP/opus-mt-en-cs` | — |
| `ro` | Romanian | `Helsinki-NLP/opus-mt-en-ro` | — |
| `hu` | Hungarian | `Helsinki-NLP/opus-mt-en-hu` | — |
| `el` | Greek | `Helsinki-NLP/opus-mt-en-el` | — |
| `bg` | Bulgarian | `Helsinki-NLP/opus-mt-en-bg` | — |
| `ja` | Japanese | `Helsinki-NLP/opus-mt-en-jap` | Noto Sans CJK JP |
| `zh` | Chinese (Simplified) | `Helsinki-NLP/opus-mt-en-zh` | Noto Sans CJK SC |
| `ko` | Korean | `Helsinki-NLP/opus-mt-tc-big-en-ko` | Noto Sans CJK KR |
| `vi` | Vietnamese | `Helsinki-NLP/opus-mt-en-vi` | — |
| `id` | Indonesian | `Helsinki-NLP/opus-mt-en-id` | — |
| `fa` | Persian | `Helsinki-NLP/opus-mt-en-fa` | Noto Naskh Arabic |
| `hi` | Hindi | `Helsinki-NLP/opus-mt-en-hi` | Noto Sans Devanagari |
| `th` | Thai | `Helsinki-NLP/opus-mt-en-th` | Noto Sans Thai |
| `custom` | Your folder | whatever you downloaded | optional |

Chinese / Portuguese / Korean packs send an Opus **target token** (`>>cmn_Hans<<`, `>>por<<`, `>>kor<<`). You do not type these unless you use **Custom**.

Japanese repo id is **`opus-mt-en-jap`**, not `en-ja`.

Every pack’s **token / font** lives in `TargetLanguages.cs`. Worker protocol and cache keys are in `LocalTranslator.cs` / `TranslationCache.cs`.

---

## Commands

| Command | Action |
|---------|--------|
| `/lisan` | Toggle plugin on/off |
| `/lisan config` | Open settings |
| `/lisan lang` | Print current language |
| `/lisan lang ar` | Switch language and restart the worker |
| `/lisan test` | Translate a sample line (Talk/BattleTalk must be open) |
| `/dialogar` | Same commands (old name, still works) |

---

## Settings overview

### General

- Enable plugin  
- **Target language** (built-in list or Custom)  
- Custom: optional Opus token, RTL checkbox  
- Translate **Talk** / **BattleTalk** / **TalkSubtitle**  
- Balloon **font size** and **text color** (plugin-font languages: Arabic, CJK, …)

Translation always writes onto the game balloon. Arabic / CJK use the plugin font on the same balloon.

### Translator

- Status + active model path + cache count  
- **Fast translation** (greedy decode, recommended) vs slightly better/slower beams  
- Warm up translator on load  
- Optional custom model folder (Apply restarts the worker)  
- Clear cache / test translate / apply language  

### Debug

- Log each translation to the Dalamud log  
- Live view of the current line (source, speaker, EN, translation)

---

## How it works

```
Talk / BattleTalk / TalkSubtitle
        │
        ▼
 AddonDialogueWatcher  ── reads speaker + English body text
        │
        ├─ cache hit (per language)? ──► overlay
        │
        └─ LocalTranslator ── JSON line to Python worker
                │
                ▼
        translate_worker.py + opus-mt-en-<lang>
                │
                ▼
        Overlay (RTL bake only when needed)
```

- **Addon hooks:** `PostDraw` / `PostRefresh` / `PreFinalize` via `IAddonLifecycle`  
- **Visibility:** overlay only while the matching game dialogue addon is visible  
- **Worker:** long-lived Python process; stdin/stdout JSON (`cmd: tr`)  
- **Cache:** `dialogue-cache.json` in the plugin config directory, keys prefixed with the language code  

Keep the **game client language on English**. Arabic (and other scripts the game font cannot draw) is drawn on the Talk balloon with a plugin font.

---

## Project layout

```
Lisan/
├── Brand.cs                  # display name, /lisan, habsi.net
├── Plugin.cs                 # Dalamud entry, commands, language apply
├── Configuration.cs          # Settings
├── TargetLanguages.cs        # Built-in language catalog
├── Services/
│   ├── AddonDialogueWatcher.cs
│   ├── LocalTranslator.cs
│   ├── TranslationCache.cs
│   └── DialogueDisplay.cs    # RTL bake + wrap
├── Windows/
│   ├── ConfigWindow.cs
│   └── DialogueOverlay.cs
├── Models/
│   ├── translate_worker.py
│   ├── setup-models.ps1
│   └── opus-mt-en-*/         # after setup
└── Fonts/
    └── (language font after setup)
```

**Release output:** `Lisan\bin\x64\Release\`

Dalamud **InternalName** is `Lisan` (config lives in `%AppData%\XIVLauncher\pluginConfigs\Lisan`).

---

## Add a language that is not in the list

1. Find an English→X model on Hugging Face, usually `Helsinki-NLP/opus-mt-en-<code>` or `Helsinki-NLP/opus-mt-tc-big-en-<code>`.
2. Download it:

```powershell
.\setup-models.ps1 -Language xx -Repo Helsinki-NLP/opus-mt-en-xx
```

3. If the model card says you must send `>>id<<`, put that token in **Custom → Opus token**.
4. In config: **Custom model folder**, set the folder, RTL if needed.
5. Click **Apply custom language**.
6. Optional: add a row to `TargetLanguages.All` and to the hashtable in `setup-models.ps1` so it appears in the dropdown next build.

To ship a new built-in pack, keep **Code / Repo / Folder / Font / token** in sync between `TargetLanguages.cs` and `setup-models.ps1`.

---

## Performance tips

- First line after load may wait while the model warms up; later lines (and cache hits) are much faster.  
- Keep **Fast translation** on unless you care about marginal quality.  
- CJK fonts are large; first overlay font build can hitch.  
- Clear cache if old translations linger after you change model quality settings (not needed when only switching language).  

---

## Troubleshooting

| Problem | What to check |
|--------|----------------|
| Stays English / no translation | Translator status; Python on PATH; `config.json` + weights in the model folder; `setup-models.ps1 -Language <code>`; **Apply language** |
| Balloon stays English | Plugin enabled, Talk visible, source toggles on; Translator status Ready |
| Tofu boxes | Font missing in `Fonts\`; re-run setup for that language; reload plugin |
| Arabic looks LTR | Language must be `ar` / `fa` (or Custom + RTL) |
| Wrong language after switch | `/lisan lang <code>` or Apply language; confirm Translator path folder name |
| Custom model ignored | Folder must exist and contain `config.json`; then Apply language |
| `setup-models.ps1` 404 | Wrong repo id (Japanese is `opus-mt-en-jap`); pass `-Repo` from the model card |
| Thai / some small pairs fail download | That `opus-mt-en-th` repo may not exist; find another Helsinki repo and use `-Repo` |

---

## Build notes

- SDK: `Dalamud.NET.Sdk/15.0.0`  
- NuGet: `ArabicRt` (Arabic-script overlay only)  
- Unsafe blocks enabled (addon node reads)  

```powershell
dotnet build -c Release -p:Platform=x64
```

---

## Credits & licenses

**Lisan** is made with love at [habsi.net](https://habsi.net).

- Translation models: [Helsinki-NLP Opus-MT](https://huggingface.co/Helsinki-NLP)  
- Arabic shaping: [ArabicRt](https://www.nuget.org/packages/ArabicRt)  
- Fonts: [Noto](https://fonts.google.com/noto) (SIL OFL)  
- Game integration: [Dalamud](https://github.com/goatcorp/Dalamud)  

This plugin is a local client tool for personal use. Respect Square Enix ToS and Dalamud plugin guidelines. Translation quality is machine-MT and will not be perfect for lore-heavy dialogue.
