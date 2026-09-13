# DialogueAr

Offline **English → any language** dialogue translator for Final Fantasy XIV (Dalamud).

Hooks the game’s **Talk**, **BattleTalk**, and **TalkSubtitle** windows, translates lines locally with **Helsinki-NLP Opus-MT** (no cloud translation API), shows the result in a floating ImGui overlay, and can speak it with **Windows TTS**.

Default language is still **Arabic** so existing installs keep working. Pick French, Japanese, Chinese, or any built-in pack in **General → Target language**.

**How TTS + models work (full internals):** [TTS-AND-MODELS.md](TTS-AND-MODELS.md)  
STA thread, voice pick, SSML, Opus-MT folders, Python JSON protocol, cache keys, target tokens, every language’s TTS culture.

**Current version:** 0.3.0  
**Dalamud:** API 15 (`.NET 10`)  
**Author:** ALHABSI

---

## Make it fully working (checklist)

Do these in order. Skip a step only if that piece is already on the machine.

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
cd D:\Dev\DialogueAr\DialogueAr
dotnet build -c Release -p:Platform=x64
```

Output folder (this is the Dev Plugin path):

`D:\Dev\DialogueAr\DialogueAr\bin\x64\Release`

### 4. Download a language model + font

From the **source** `Models` folder (not only the bin copy — setup writes next to the script, then you rebuild or copy):

```powershell
cd D:\Dev\DialogueAr\DialogueAr\Models
.\setup-models.ps1 -Language list          # see every built-in code
.\setup-models.ps1 -Language ar            # Arabic (default)
.\setup-models.ps1 -Language fr            # French
.\setup-models.ps1 -Language ja            # Japanese (repo is opus-mt-en-jap)
```

What the script does:

1. Installs `torch` (CPU), `transformers`, `sentencepiece`, `huggingface_hub`.
2. Downloads the Hugging Face repo into `Models\<folder>\` (needs internet **once**).
3. Downloads an overlay font into `Fonts\` when the script needs one (Arabic, CJK, Hebrew, Hindi, Thai, Persian).

After download, **rebuild** so `bin\x64\Release\Models\...` and `Fonts\...` are current:

```powershell
cd D:\Dev\DialogueAr\DialogueAr
dotnet build -c Release -p:Platform=x64
```

Or copy `Models\<folder>` and `Fonts\` into the Release output yourself.

**Unknown language** (not in the list):

```powershell
.\setup-models.ps1 -Language xx -Repo Helsinki-NLP/opus-mt-en-xx
```

Then in-game pick **Custom model folder**, set TTS culture (e.g. `ja-JP`), optional Opus token (`>>cmn_Hans<<` for some Chinese/TC models), and point Translator at that folder.

### 5. Load it in Dalamud

1. Dalamud Settings → **Experimental** / **Dev Plugins**.
2. Add path: `D:\Dev\DialogueAr\DialogueAr\bin\x64\Release`
3. Enable **DialogueAr** and click **Reload**.
4. `/dialogar config` → **General** → **Target language** = the code you downloaded.
5. **Translator** tab must say `Ready (opus-mt-…)` after the first line or after **Apply language / restart worker**.

If status is `Model missing`, the folder with `config.json` + weights is not next to the loaded DLL. Run setup again and rebuild, or set **Custom model folder** to the absolute path of `Models\opus-mt-en-fr` (etc.).

### 6. Overlay

1. Enable **Show overlay**.
2. Talk to an NPC (game client language should be **English** — that is the source text).
3. First line can take 10–30s (Python + model warm-up). Later lines and cache hits are fast.
4. Drag the overlay; right-click opens config.

**Tofu / empty boxes** for Japanese, Chinese, Korean, Arabic, Hebrew, Hindi, Thai: font file missing. Re-run `setup-models.ps1 -Language <code>` and reload the plugin (font is loaded at apply/reload).

### 7. Optional: Windows TTS

1. Config → **Speech** → enable **Speak translation**.
2. **★ Auto** picks a voice for the target language, then Multilingual (Ava / Emma / Andrew / Brian), then any gender match.
3. Best quality: install a native TTS pack (Admin PowerShell). The Speech tab prints the exact capability name. Examples:

```powershell
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~ar-SA~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~fr-FR~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~ja-JP~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~de-DE~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~es-ES~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~zh-CN~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~ko-KR~0.0.1.0
```

4. Then **Refresh voice list** and pick Auto or a named voice.
5. Per-character voices: **Characters** tab (Talk speaker name).

English-only desktop voices often misread other scripts. Use a native pack or a **Multilingual** voice.

Full TTS internals (STA queue, Auto ladder, gender, SSML vs PromptBuilder, capability names): [TTS-AND-MODELS.md](TTS-AND-MODELS.md).

### 8. Switch language later

```powershell
cd D:\Dev\DialogueAr\DialogueAr\Models
.\setup-models.ps1 -Language de
```

Rebuild (or copy the new `Models` folder into Release), then in-game:

- `/dialogar lang de`  
  or General tab dropdown → **Apply language / restart worker**

Cache is stored **per language**. Old Arabic cache stays valid.

---

## Built-in languages

| Code | Language | Hugging Face repo | Extra font |
|------|----------|-------------------|------------|
| `ar` | Arabic | `Helsinki-NLP/opus-mt-en-ar` | Noto Naskh Arabic |
| `fr` | French | `Helsinki-NLP/opus-mt-en-fr` | — |
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
| `he` | Hebrew | `Helsinki-NLP/opus-mt-en-he` | Noto Sans Hebrew |
| `fa` | Persian | `Helsinki-NLP/opus-mt-en-fa` | Noto Naskh Arabic |
| `hi` | Hindi | `Helsinki-NLP/opus-mt-en-hi` | Noto Sans Devanagari |
| `th` | Thai | `Helsinki-NLP/opus-mt-en-th` | Noto Sans Thai |
| `custom` | Your folder | whatever you downloaded | optional |

Chinese / Portuguese / Korean packs send an Opus **target token** (`>>cmn_Hans<<`, `>>por<<`, `>>kor<<`). You do not type these unless you use **Custom**.

Japanese repo id is **`opus-mt-en-jap`**, not `en-ja`.

Every pack’s **token / TTS culture / Windows capability / font**: [TTS-AND-MODELS.md §3](TTS-AND-MODELS.md#3-language-pack-map-model--tts-together). Worker protocol, chunking, cache keys: same file.

---

## Commands

| Command | Action |
|---------|--------|
| `/dialogar` | Toggle plugin on/off |
| `/dialogar config` | Open settings |
| `/dialogar lang` | Print current language |
| `/dialogar lang fr` | Switch language and restart the worker |
| `/dialogar test` | Translate a sample line (Talk/BattleTalk must be open) |
| `/dialogar stop` | Stop / clear TTS queue |

Right-click the overlay (if enabled) also opens config.

---

## Settings overview

### General

- Enable plugin  
- **Target language** (built-in list or Custom)  
- Custom: TTS culture, optional Opus token, RTL checkbox  
- Translate **Talk** / **BattleTalk** / **TalkSubtitle**

### Overlay

- Show / lock (click-through)  
- Show speaker name  
- Show original English  
- Show “translating…” placeholder  
- Right-click opens config  
- Scale, wrap width, wrap chars (`0` = auto)  
- Background opacity, translation text color  
- Reset overlay position  

RTL scripts (Arabic, Persian, Hebrew) are pre-wrapped and right-aligned. LTR languages use ImGui wrap.

### Speech (TTS)

- Enable Windows TTS  
- **Voice** dropdown: all installed voices, or **★ Auto (Male / Female by speaker)**  
- **Auto gender:** detect speaker sex in-world, or prefer Female / Male  
- Volume, speak rate (−10…+10; foreign voices use SSML prosody)  
- Queue lines vs interrupt  
- Stop TTS when dialogue closes  
- Skip duplicate lines  
- Warm up TTS on load  

**Voice priority**

1. **Per-character override** (Characters tab)  
2. Global manual voice  
3. Auto by gender (target-language voice → Multilingual → any matching gender)

### Characters

Assign a Windows voice to a Talk **speaker name**.

- Quick-fill from the current dialogue speaker  
- Matches full name or first name (e.g. `Alphinaud` ↔ `Alphinaud Leveilleur`)

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
        ├─ cache hit (per language)? ──► overlay + TTS
        │
        └─ LocalTranslator ── JSON line to Python worker
                │
                ▼
        translate_worker.py + opus-mt-en-<lang>
                │
                ▼
        Overlay (RTL bake only when needed)  +  DialogueTtsService (STA thread)
```

- **Addon hooks:** `PostDraw` / `PostRefresh` / `PreFinalize` via `IAddonLifecycle`  
- **Visibility:** overlay only while the matching game dialogue addon is visible  
- **Worker:** long-lived Python process; stdin/stdout JSON (`cmd: tr`)  
- **Cache:** `dialogue-cache.json` in the plugin config directory, keys prefixed with the language code  
- **TTS:** never runs `System.Speech` on the game/pool thread; all synth work is queued on an STA worker thread  

Native English in the game UI is left unchanged. The translation appears only in the overlay (and optionally as speech). Keep the **game client language on English**.

Step-by-step of one Talk line through cache → Python → SSML: [TTS-AND-MODELS.md](TTS-AND-MODELS.md).

---

## Project layout

```
DialogueAr/
├── Plugin.cs                 # Dalamud entry, commands, language apply
├── Configuration.cs          # Settings + character voice lookup
├── TargetLanguages.cs        # Built-in language catalog
├── CharacterVoiceOverride.cs
├── TtsVoiceGender.cs
├── Services/
│   ├── AddonDialogueWatcher.cs
│   ├── LocalTranslator.cs
│   ├── TranslationCache.cs
│   ├── DialogueDisplay.cs    # RTL bake + wrap
│   ├── DialogueTtsService.cs
│   └── SpeakerGender.cs
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

**Release output:** `DialogueAr\bin\x64\Release\`

---

## Add a language that is not in the list

1. Find an English→X model on Hugging Face, usually `Helsinki-NLP/opus-mt-en-<code>` or `Helsinki-NLP/opus-mt-tc-big-en-<code>`.
2. Download it:

```powershell
.\setup-models.ps1 -Language xx -Repo Helsinki-NLP/opus-mt-en-xx
```

3. If the model card says you must send `>>id<<`, put that token in **Custom → Opus token**.
4. In config: **Custom model folder**, set the folder, TTS culture (`xx-XX`), RTL if needed.
5. Click **Apply custom language**.
6. Optional: add a row to `TargetLanguages.All` and to the hashtable in `setup-models.ps1` so it appears in the dropdown next build.

To ship a new built-in pack, keep **Code / Repo / Folder / Font / TTS culture / token** in sync between `TargetLanguages.cs` and `setup-models.ps1`.

---

## Performance tips

- First line after load may wait while the model warms up; later lines (and cache hits) are much faster.  
- Keep **Fast translation** on unless you care about marginal quality.  
- **Online** Windows voices are slower to start; a local TTS pack is better for latency.  
- CJK fonts are large; first overlay font build can hitch.  
- Clear cache if old translations linger after you change model quality settings (not needed when only switching language).  

---

## Troubleshooting

| Problem | What to check |
|--------|----------------|
| Stays English / no translation | Translator status; Python on PATH; `config.json` + weights in the model folder; `setup-models.ps1 -Language <code>`; **Apply language** |
| Overlay empty | Plugin enabled, Show overlay on, Talk visible, source toggles on |
| Tofu boxes | Font missing in `Fonts\`; re-run setup for that language; reload plugin |
| Arabic/Hebrew looks LTR | Language must be `ar` / `he` / `fa` (or Custom + RTL); wrap width |
| TTS silent or English | Translator still warming (placeholder); English-only voice; install native TTS or Multilingual |
| Wrong language after switch | `/dialogar lang <code>` or Apply language; confirm Translator path folder name |
| Custom model ignored | Folder must exist and contain `config.json`; then Apply language |
| Game crash on talk (old builds) | Upgrade; TTS must stay on STA thread |
| Character voice ignored | Exact/first-name match; Characters tab override beats global voice |
| `setup-models.ps1` 404 | Wrong repo id (Japanese is `opus-mt-en-jap`); pass `-Repo` from the model card |
| Thai / some small pairs fail download | That `opus-mt-en-th` repo may not exist; find another Helsinki repo and use `-Repo` |

---

## Build notes

- SDK: `Dalamud.NET.Sdk/15.0.0`  
- NuGet: `ArabicRt` (Arabic-script overlay only), `System.Speech`  
- Unsafe blocks enabled (addon node reads)  

```powershell
dotnet build -c Release -p:Platform=x64
```

---

## Credits & licenses

- Translation models: [Helsinki-NLP Opus-MT](https://huggingface.co/Helsinki-NLP)  
- Arabic shaping: [ArabicRt](https://www.nuget.org/packages/ArabicRt)  
- Fonts: [Noto](https://fonts.google.com/noto) (SIL OFL)  
- Game integration: [Dalamud](https://github.com/goatcorp/Dalamud)  

This plugin is a local client tool for personal use. Respect Square Enix ToS and Dalamud plugin guidelines. Translation quality is machine-MT and will not be perfect for lore-heavy dialogue.
