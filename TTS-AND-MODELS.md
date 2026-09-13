# How TTS and translation models work

This is the full internals guide for DialogueAr: **Windows speech**, **Helsinki-NLP Opus-MT**, the **Python worker**, **cache**, and **how a Talk line becomes spoken audio**. Setup steps stay in [README.md](README.md). This file is *how the machine actually runs*.

Nothing here leaves your PC for translation. Hugging Face is used only when you run `setup-models.ps1`. After that, `local_files_only=True`. TTS is Windows `System.Speech` on your machine (desktop voices are local; **Online / Natural** voices may hit Microsoft’s cloud — that is Windows, not this plugin).

---

## End-to-end pipeline

```
FFXIV Talk / BattleTalk / TalkSubtitle
        │  AddonDialogueWatcher reads speaker + English body
        ▼
 Per-language cache  (dialogue-cache.json key = "<lang>|<english>")
        │
        ├─ HIT  → overlay + DialogueTtsService.Speak(translation, speaker)
        │
        └─ MISS → LocalTranslator.Translate(english)
                      │
                      ▼
              python translate_worker.py <modelDir> [>>token<<]
                      │  HuggingFace Marian / Opus-MT on disk
                      ▼
              JSON {"ok":true,"t":"..."}
                      │
                      ▼
              cache.Set  +  overlay  +  Speak(...)
```

Game UI English is **never replaced**. Overlay shows the translation. TTS speaks the translation only.

Source text must be English. Set the **FFXIV client language to English**.

---

## 1. Translation models

### What a “model” is

Each target language is a **separate English→X neural machine-translation pack** from [Helsinki-NLP](https://huggingface.co/Helsinki-NLP):

| Family | Example | Size / notes |
|--------|---------|----------------|
| `opus-mt-en-*` | `opus-mt-en-ar`, `opus-mt-en-fr` | Classic Marian transformer-align. One source, one target. |
| `opus-mt-en-jap` | Japanese | Repo id is **`jap`**, not `ja`. |
| `opus-mt-tc-big-en-*` | Portuguese, Korean | Newer Tatoeba-Challenge “transformer-big”. Often needs a **target token** (`>>por<<`, `>>kor<<`). |
| `opus-mt-en-zh` | Chinese | Multi-target Chinese varieties. Plugin prepends `>>cmn_Hans<<` (Simplified Mandarin). |

These are **seq2seq** models: tokenizer turns English into token ids → `model.generate` → tokenizer decodes target-language text.

The plugin does **not** use a cloud translate API and does **not** use NLLB / GPT. One downloaded folder = one language pair.

### Files that must exist on disk

After `setup-models.ps1 -Language <code>`, you get something like:

```
Models/opus-mt-en-fr/
  config.json              ← required (plugin presence check)
  pytorch_model.bin        ← or model.safetensors / any *.bin / *.safetensors
  tokenizer_config.json
  vocab.json               ← or sentencepiece pieces
  source.spm / target.spm  ← many opus-mt packs
  generation_config.json
```

`LocalTranslator.ModelPresent` is true only if:

1. The folder exists.
2. `config.json` exists.
3. At least one of: `pytorch_model.bin`, `model.safetensors`, any `*.bin`, any `*.safetensors`.

No weights → status `Model missing — run Models\setup-models.ps1 -Language <code>`.

### How the plugin finds the folder

`LocalTranslator.ResolveModelDir` walks, in order:

1. **Custom override** — `Configuration.ModelDirectory` if that path exists (any language).
2. **Next to the loaded DLL** — `<pluginDir>\Models\<ModelFolder>`  
   Release: `bin\x64\Release\Models\opus-mt-en-fr`
3. **Source tree** (dev) — `..\..\..\Models\<ModelFolder>` from the bin folder.
4. **XIVLauncher config** — `%AppData%\XIVLauncher\pluginConfigs\DialogueAr\Models\<ModelFolder>`

`<ModelFolder>` comes from `TargetLanguages` (e.g. `opus-mt-en-ar`, `opus-mt-en-jap`, `opus-mt-tc-big-en-pt`). First candidate that has `config.json` wins. If none do, status still points at candidate 1.

**Language switch does not download.** You must run setup (or copy a folder) *before* `/dialogar lang fr`. Then **Apply language** kills the old Python process and starts one with the new path.

### Catalog fields (`TargetLanguages.cs`)

Every built-in pack is one row:

| Field | Role |
|-------|------|
| `Code` | Config + cache prefix + `/dialogar lang` (`ar`, `fr`, `ja`, …) |
| `Name` | UI label; also used when matching TTS voice *names* (“Arabic”, “French”) |
| `HuggingFaceRepo` | What `setup-models.ps1` downloads |
| `ModelFolder` | On-disk directory name |
| `TargetToken` | Optional `>>id<<` prepended to English before generate |
| `TtsCulture` | SSML `xml:lang` and native-voice match (`ar-SA`, `fr-FR`, `ja-JP`, …) |
| `WindowsTtsCapability` | `Add-WindowsCapability` name printed in the Speech tab |
| `BusyText` | Overlay placeholder; TTS **refuses** to speak this |
| `RightToLeft` / `ArabicScript` | Overlay layout / ArabicRt shaping |
| `FontFile` / `GlyphRanges` | Overlay font (not used by the MT model) |

`custom` reads `CustomTtsCulture`, `CustomTargetToken`, `CustomRightToLeft`, and `ModelDirectory`.

### Target tokens (`>>id<<`)

Some Helsinki packs are **multi-target**. The first tokens of the *source* must be a language id:

| Code | Token sent to the worker | Why |
|------|--------------------------|-----|
| `zh` | `>>cmn_Hans<<` | Simplified Mandarin among Chinese varieties |
| `pt` | `>>por<<` | Portuguese (card also lists `>>pob<<` for Brazilian) |
| `ko` | `>>kor<<` | Korean TC-big |

C# starts Python as:

```text
python "translate_worker.py" "C:\...\Models\opus-mt-en-zh" ">>cmn_Hans<<"
```

Worker does `src = token + " " + english` then tokenizes that. Built-in packs send this for you. Custom packs: type the token in **General → Opus token**.

### Python worker (`translate_worker.py`)

Long-lived process. **One model in memory.** Switching language = kill + start.

**Startup**

1. Plugin finds `python` or `py -3` (`FindPython`).
2. Starts with redirected UTF-8 stdin/stdout, no window.
3. Worker `import torch`, `transformers`.
4. Caps CPU threads: `torch.set_num_threads(1..8)`, interop threads = 1 (dialogue latency on CPU).
5. `AutoTokenizer` + `AutoModelForSeq2SeqLM` from `model_dir` with **`local_files_only=True`** (no network).
6. `model.eval()`, then a dummy `translate_one(..., "Hello.")` so the first real line is not a cold compile.
7. Prints one JSON line: `{"ok": true, "pong": true}`.  
   If C# does not see `"ok": true`, status = `Worker ready failed: …`.

**JSON protocol** (one object per line)

| You send | Worker replies |
|----------|----------------|
| `{"cmd":"ping"}` | `{"ok":true,"pong":true}` |
| `{"cmd":"tr","fast":true,"text":"Stay close."}` | `{"ok":true,"t":"…","ar":"…"}` |

`ar` is a leftover alias so old C# parsers still work. Current C# reads `"t"` first, then `"ar"`.

`fast`:

- `true` (config **Fast translation**, default) → `num_beams=1` (greedy). Faster.
- `false` → `num_beams=2`. Slightly better, slower.

Always `do_sample=False`, `use_cache=True`.

**Length / chunking** (Marian max position is 512)

- Encode cap: `MAX_TOKENS = 480` (room for special tokens).
- Generate cap: `MAX_NEW_CAP = 256` new tokens; per chunk `max(48, min(256, in_len * 2 + 16))`.
- If the line is longer than 480 tokens: split on `. ! ? …`, pack sentences into chunks under 480, translate each, join with spaces. A single huge sentence is split on words.

**Errors**

- Missing pip deps → `{"ok":false,"error":"deps: …"}` then exit.
- Bad/missing weights → `{"ok":false,"error":"model: …"}` then exit.
- Generate throw → `{"ok":false,"error":"…","t": <original english>}` (C# then shows English).

### C# translator (`LocalTranslator`)

- First `Translate` (or warmup) calls `EnsureStarted` → `StartWorker`.
- `Translate` is **synchronous and locked**. One line at a time on that process (stdin/stdout is not multiplexed).
- Empty input returned as-is.
- Cache hit skips Python.
- After a good result (`translated != english`), `cache.Set`.
- Worker death on next request: `StartWorker()` again.
- `Restart()` (language change / Apply): kill tree, recompute `modelDir` + `targetToken`, `ready = false`. Next line or warmup starts a new process.
- Warmup (`WarmupTranslatorOnLoad`): background `EnsureStarted()` + `Translate("Hello.")`.

### Cache (`TranslationCache`)

File: `%AppData%\XIVLauncher\pluginConfigs\DialogueAr\dialogue-cache.json`

| Version | Key | Example |
|---------|-----|---------|
| 0.3+ | `<lang>\|<trimmed english>` | `fr\|Stay close.` |
| pre-0.3 | trimmed english only | treated as **`ar`** on read |

Writes are immediate. **Clear cache** wipes all languages in that file. Switching language does **not** need a clear — keys are namespaced.

Failed translates (English echoed) are **not** cached (`translated != english` check).

### `setup-models.ps1`

```powershell
.\setup-models.ps1 -Language list
.\setup-models.ps1 -Language fr
.\setup-models.ps1 -Language xx -Repo Helsinki-NLP/opus-mt-en-xx
```

1. `pip install torch` (CPU wheel) + `transformers` + `sentencepiece` + `huggingface_hub`.
2. `snapshot_download(repo_id=…, local_dir=Models\<Folder>)`.
3. Optional **font** into `Fonts\` (overlay only — the MT model does not use fonts).

Internet is required **once**. After that, the worker never calls Hugging Face.

Rebuild (or copy `Models\` + `Fonts\` into `bin\x64\Release`) so the loaded plugin sees the files.

### Python packages the worker needs

| Package | Why |
|---------|-----|
| `torch` | Tensor + `generate` (CPU is enough) |
| `transformers` | `AutoTokenizer`, `AutoModelForSeq2SeqLM` |
| `sentencepiece` | Opus-MT SPM tokenizers |
| `huggingface_hub` | Setup script download only |

---

## 2. TTS (Windows speech)

### What TTS is (and is not)

| Piece | Role |
|-------|------|
| `DialogueTtsService` | Plugin wrapper |
| `System.Speech.Synthesis.SpeechSynthesizer` | Windows SAPI |
| Installed **voices** | Desktop (offline) or Online/Natural (Windows may use cloud) |
| **Not** the Opus-MT model | Translation and speech are separate |

The MT model only outputs **text**. TTS only **reads that text**. A French model + an English-only voice = French text spoken as if it were English (garbage). Install a matching voice or a Multilingual voice.

### Why a dedicated STA thread

`System.Speech` is COM / STA. Calling it from the game thread or a thread-pool thread has crashed FFXIV in older builds.

On construct:

1. Start background thread `DialogueAr.TTS`.
2. `SetApartmentState(STA)`.
3. Loop `BlockingCollection<Action>` — every synth call is posted onto that thread.

`Speak`, `Stop`, `RefreshVoiceList`, `EnsureReady`, dispose all `queue.Add(...)`. The game thread never touches `SpeechSynthesizer`.

### When speech is requested

`AddonDialogueWatcher` calls `Plugin.Tts.Speak(translation, speaker)` after:

- a **cache hit**, or
- a finished **Python** translate.

It does **not** speak the busy placeholder from `PushBusy`. `Speak` also hard-skips placeholders via `TargetLanguages.IsBusyPlaceholder` (localized “Translating…”, `جاري الترجمة`, etc.).

`/dialogar test` speaks the sample after translate. `/dialogar stop` → `Tts.Stop()`.

### `Speak(text, speakerName)` — exact order

1. Abort if disposed, `EnableTts` off, empty text, or busy placeholder.
2. **Gender** = `SpeakerGender.Resolve(speakerName, TtsVoiceGender)` (see below).
3. **Character voice** = `Configuration.FindCharacterVoice(speakerName)` (exact name, then first name).
4. Post to STA queue:
   1. Create synth if needed (`SetOutputToDefaultAudioDevice`).
   2. **Duplicate skip** if enabled and same text + gender + voice key as last time.
   3. `PickVoiceUnlocked(gender, characterVoice)`.
   4. Apply volume `0..100` and rate `-10..10`.
   5. If **Queue lines** is off → `SpeakAsyncCancelAll()` (cut current speech).
   6. If the chosen voice is **not** native to the target language → **SSML** with `xml:lang="<TtsCulture>"` and `<prosody rate="N%">`.
   7. If the voice **is** native → `PromptBuilder` + `PromptStyle` rate buckets.
   8. `SpeakAsync` / `SpeakSsmlAsync` (non-blocking on the STA thread).

### Voice pick priority

```
1. Characters tab override (exact Windows voice name)
2. Speech tab manual voice (TtsVoiceName not empty)
3. AUTO:
     a. Native voice for target language + wanted gender (Neutral allowed)
        prefer exact TtsCulture (fr-FR over fr-CA), prefer non-Online
     b. Any native voice for that language
     c. Name contains "Multilingual" + wanted gender
        Female prefer: Ava > Emma
        Male prefer: Andrew > Brian
     d. Any Multilingual
     e. Any voice with wanted gender (prefer non-Online)
     f. First installed voice
4. No voices → dispose synth, status "TTS: no voices installed"
```

**Native match** (`MatchesTarget`) if any of:

- voice culture two-letter ISO = first part of `TtsCulture` (`fr` from `fr-FR`)
- voice culture name equals `TtsCulture` (`ja-JP`)
- voice **name** contains the language **Name** (`French`, `Arabic`, `Japanese`)

Manual voice missing → log fallback and continue Auto.

### Gender (`SpeakerGender`)

Config **Auto gender**:

| Mode | Result |
|------|--------|
| Prefer Female / Male | That gender, ignore the world |
| Auto (default) | Look up the Talk speaker in the object table |

Lookup order: current **target** → **focus target** → scan **object table**. `CustomizeData.Sex`: `1` = female, else male. Name match = full name or first token (`Alphinaud` ↔ `Alphinaud Leveilleur`).

If nobody matches: **local player** sex. If that fails: **Female** (Ava-style default).

Gender only affects **Auto** voice pick (and duplicate-skip key). A Characters-tab voice is used as-is.

### SSML vs PromptBuilder

`usePromptCulture = !MatchesTarget(selectedVoice, current language)`.

**SSML** (voice is English / Multilingual / wrong language) — tells the engine which language the *text* is:

```xml
<?xml version="1.0" encoding="utf-8"?>
<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="ar-SA">
  <prosody rate="130%">…escaped translation…</prosody>
</speak>
```

`xml:lang` = pack `TtsCulture`. Rate percent = `clamp(100 + rate*10, 50, 200)`. Config default rate `3` → **130%**.

**PromptBuilder** (native voice) — `PromptRate`: ExtraSlow ≤ −7, Slow ≤ −3, Medium ≤ 2, Fast ≤ 6, ExtraFast above.

English-only **desktop** voices often **ignore** SSML language and skip or butcher non-Latin text. That is a Windows limit. Fix: install a native pack or use **Microsoft Multilingual Online** (Ava / Emma / Andrew / Brian).

### Settings that change speech

| Setting | Default | Effect |
|---------|---------|--------|
| Enable TTS | on | Master switch |
| Voice | Auto (`""`) | Empty = Auto ladder; else exact `VoiceInfo.Name` |
| Auto gender | Auto | Female / Male / detect speaker |
| Volume | 100 | `SpeechSynthesizer.Volume` 0–100 |
| Rate | 3 | −10…+10; SSML % or PromptRate buckets |
| Queue lines | on | Off = cancel previous `SpeakAsync` |
| Stop when dialogue closes | off | `PreFinalize` / hidden addon → `Stop()` |
| Skip duplicates | on | Same text + gender + voice key |
| Warm up TTS on load | on | Create synth + load voice list early |
| Character voices | [] | Per Talk speaker name |

### Character overrides

`FindCharacterVoice`:

1. Exact speaker = row `SpeakerName` (ignore case).
2. Else first-word match either way (`Alphinaud` ↔ `Alphinaud Leveilleur`).

Wins over global Auto **and** global manual. Voice must still be installed; unknown name → Auto ladder.

### Voice list

`RefreshVoiceList` (plugin load + Speech tab button) enumerates `synth.GetInstalledVoices()` where `Enabled`, stores name / gender / culture / “Multilingual” flag. Combo label: `Name  ·  Gender  ·  culture`.

New Windows language packs do not appear until **Refresh** (or plugin reload).

### Installing Windows TTS packs

Admin PowerShell. Speech tab prints the exact name for the current language.

```powershell
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~ar-SA~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~fr-FR~0.0.1.0
Add-WindowsCapability -Online -Name Language.TextToSpeech~~~ja-JP~0.0.1.0
```

Pattern: `Language.TextToSpeech~~~<culture>~0.0.1.0`.

You can also install a **language pack** (Settings → Time & language → Speech) or Multilingual Online voices (Settings → Accounts / Speech — names vary by Windows build).

Persian (`fa`) has **no** capability string in the catalog. Use Multilingual or any installed `fa-*` voice.

Check installed voices:

```powershell
Add-Type -AssemblyName System.Speech
(New-Object System.Speech.Synthesis.SpeechSynthesizer).GetInstalledVoices() |
  ForEach-Object { $_.VoiceInfo.Name + "  " + $_.VoiceInfo.Culture.Name + "  " + $_.VoiceInfo.Gender }
```

### TTS status line (Speech tab)

Examples:

- `TTS: 12 voices loaded`
- `TTS: Microsoft Ava Multilingual (ar-SA SSML, Female, Auto) · speaking (rate 3)`
- `TTS: Microsoft Hortense (fr-FR, Female, Manual) · speaking (rate 3)`
- `TTS skipped` is **not** used anymore for “not Arabic”; non-Arabic text is spoken.
- `Speak error: …` — synth fault; service disposes the synthesizer and the next line recreates it.

---

## 3. Language pack map (model + TTS together)

| Code | MT repo / folder | Token | TTS culture | TTS capability | Overlay font |
|------|------------------|-------|-------------|----------------|--------------|
| ar | `Helsinki-NLP/opus-mt-en-ar` | — | ar-SA | `Language.TextToSpeech~~~ar-SA~0.0.1.0` | Noto Naskh Arabic |
| fr | `Helsinki-NLP/opus-mt-en-fr` | — | fr-FR | `…~~~fr-FR~0.0.1.0` | default |
| de | `Helsinki-NLP/opus-mt-en-de` | — | de-DE | `…~~~de-DE~0.0.1.0` | default |
| es | `Helsinki-NLP/opus-mt-en-es` | — | es-ES | `…~~~es-ES~0.0.1.0` | default |
| it | `Helsinki-NLP/opus-mt-en-it` | — | it-IT | `…~~~it-IT~0.0.1.0` | default |
| pt | `Helsinki-NLP/opus-mt-tc-big-en-pt` | `>>por<<` | pt-BR | `…~~~pt-BR~0.0.1.0` | default |
| nl | `Helsinki-NLP/opus-mt-en-nl` | — | nl-NL | `…~~~nl-NL~0.0.1.0` | default |
| pl | `Helsinki-NLP/opus-mt-en-pl` | — | pl-PL | `…~~~pl-PL~0.0.1.0` | default |
| ru | `Helsinki-NLP/opus-mt-en-ru` | — | ru-RU | `…~~~ru-RU~0.0.1.0` | default |
| uk | `Helsinki-NLP/opus-mt-en-uk` | — | uk-UA | `…~~~uk-UA~0.0.1.0` | default |
| tr | `Helsinki-NLP/opus-mt-en-tr` | — | tr-TR | `…~~~tr-TR~0.0.1.0` | default |
| sv | `Helsinki-NLP/opus-mt-en-sv` | — | sv-SE | `…~~~sv-SE~0.0.1.0` | default |
| da | `Helsinki-NLP/opus-mt-en-da` | — | da-DK | `…~~~da-DK~0.0.1.0` | default |
| fi | `Helsinki-NLP/opus-mt-en-fi` | — | fi-FI | `…~~~fi-FI~0.0.1.0` | default |
| cs | `Helsinki-NLP/opus-mt-en-cs` | — | cs-CZ | `…~~~cs-CZ~0.0.1.0` | default |
| ro | `Helsinki-NLP/opus-mt-en-ro` | — | ro-RO | `…~~~ro-RO~0.0.1.0` | default |
| hu | `Helsinki-NLP/opus-mt-en-hu` | — | hu-HU | `…~~~hu-HU~0.0.1.0` | default |
| el | `Helsinki-NLP/opus-mt-en-el` | — | el-GR | `…~~~el-GR~0.0.1.0` | default |
| bg | `Helsinki-NLP/opus-mt-en-bg` | — | bg-BG | `…~~~bg-BG~0.0.1.0` | default |
| ja | `Helsinki-NLP/opus-mt-en-jap` | — | ja-JP | `…~~~ja-JP~0.0.1.0` | Noto Sans CJK JP |
| zh | `Helsinki-NLP/opus-mt-en-zh` | `>>cmn_Hans<<` | zh-CN | `…~~~zh-CN~0.0.1.0` | Noto Sans CJK SC |
| ko | `Helsinki-NLP/opus-mt-tc-big-en-ko` | `>>kor<<` | ko-KR | `…~~~ko-KR~0.0.1.0` | Noto Sans CJK KR |
| vi | `Helsinki-NLP/opus-mt-en-vi` | — | vi-VN | `…~~~vi-VN~0.0.1.0` | default |
| id | `Helsinki-NLP/opus-mt-en-id` | — | id-ID | `…~~~id-ID~0.0.1.0` | default |
| he | `Helsinki-NLP/opus-mt-en-he` | — | he-IL | `…~~~he-IL~0.0.1.0` | Noto Sans Hebrew |
| fa | `Helsinki-NLP/opus-mt-en-fa` | — | fa-IR | *(none in catalog)* | Noto Naskh Arabic |
| hi | `Helsinki-NLP/opus-mt-en-hi` | — | hi-IN | `…~~~hi-IN~0.0.1.0` | Noto Sans Devanagari |
| th | `Helsinki-NLP/opus-mt-en-th` | — | th-TH | `…~~~th-TH~0.0.1.0` | Noto Sans Thai |

Capability names in the Speech tab are the full `Language.TextToSpeech~~~xx-YY~0.0.1.0` string.

Not every culture ships on every Windows SKU. If `Add-WindowsCapability` fails, use Multilingual Online or Settings → Speech.

`opus-mt-en-th` may 404. Pass `-Repo` from the model card if setup fails.

---

## 4. Overlay vs model vs TTS

Three independent pieces:

| | Model (Python) | Overlay (ImGui) | TTS (SAPI) |
|--|----------------|-----------------|------------|
| Input | English | Translated string | Same translated string |
| Needs | `Models\<folder>` + Python | Optional `Fonts\<file>` for non-Latin | Installed Windows voice |
| Fail mode | Stays English | Tofu boxes / empty | Silent, English-sounding, or wrong language |

You can run **translation without TTS** (disable Speech). You can run **TTS without a pretty font** (Latin languages). Arabic/CJK without a font still translates and can still speak.

---

## 5. Code map

| File | Responsibility |
|------|----------------|
| `TargetLanguages.cs` | Pack catalog (repo, token, TTS culture, font, busy text) |
| `Services/LocalTranslator.cs` | Find Python, spawn worker, JSON `tr`, parse `t`/`ar` |
| `Models/translate_worker.py` | Load Marian, chunk, generate, stdout JSON |
| `Models/setup-models.ps1` | pip + Hugging Face snapshot + font |
| `Services/TranslationCache.cs` | `lang\|english` disk map |
| `Services/AddonDialogueWatcher.cs` | Read addons, cache/translate, call `Speak` |
| `Services/DialogueTtsService.cs` | STA queue, voice pick, SSML / Prompt |
| `Services/SpeakerGender.cs` | Object-table sex → Male/Female |
| `Configuration.cs` | All TTS + model settings |
| `Plugin.cs` | Warmup, `/dialogar lang`, `ApplyLanguage` |

---

## 6. Troubleshooting (TTS + models only)

| Symptom | Cause | Fix |
|---------|--------|-----|
| Translator `Model missing` | No `config.json` / weights on the path C# resolved | `setup-models.ps1 -Language <code>`, rebuild or set custom folder, Apply language |
| `Worker ready failed` / `deps:` | Python packages missing | Re-run setup step 1 (`torch`, `transformers`, `sentencepiece`) |
| `Worker failed` / `Python not found` | `python`/`py` not on PATH | Install Python 3, restart game |
| First line 10–30s | Model load + dummy `Hello.` | Enable both warmups; later lines use the live process + cache |
| Stays English | Worker error or model still missing | Debug tab + Dalamud log; `Log translations` |
| Wrong target language | Old worker still up, or token missing | Apply language; Custom token for TC-big / zh |
| Cache “wrong” after model change | Same `lang\|line` reused | Clear cache |
| TTS silent | TTS off; placeholder; no voices | Enable Speech; wait for translate; install a voice |
| TTS English / garbage | English desktop voice + foreign text | Native pack or Ava/Emma/Andrew/Brian Multilingual |
| TTS ignores character | Name mismatch | Use Talk name or first name; Refresh voices |
| Crash on Talk (ancient builds) | Synth on game/pool thread | Current code is STA-only — update |
| Online voice lag | Natural/Online voices start slow | Local desktop TTS pack; raise rate after it starts |

---

## 7. Privacy

- Dialogue English is written to a **local** Python stdin pipe and a **local** JSON cache.
- Opus-MT inference is **offline** after download.
- TTS audio is produced by Windows. Desktop voices stay local. **Online** voices are a Microsoft/Windows feature and may use the network.
