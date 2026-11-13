# Changelog

All notable changes to Lisan. Versions follow `<Version>` in `Lisan/Lisan.csproj`; each release is tagged `vX.Y.Z`.

## 1.0.1 — 2026-11-10

### Changed
- Relicensed from MIT to **AGPL-3.0-or-later**, matching Dalamud.
- The Setup tab no longer runs a PowerShell script: pip, the Hugging Face pack download and the font download now run from the plugin itself, so setup has no Windows-only dependency. Custom repo ids are validated, and downloads go to `.part` files first so an interrupted download is never mistaken for a finished pack.
- Optional TTS logs a warning instead of failing silently when Windows speech is unavailable (e.g. under Wine).

### Fixed
- Overlay text on dark Talk/BattleTalk balloons was black on black; it now uses the game's own text color for the balloon.
- `pluginmaster.json` was written as a nested array, so Dalamud could not read the custom repo.

### Removed
- `Install-Lisan.ps1` / `Install-Lisan.cmd` and `Models/setup-models.ps1`. Install Lisan from the plugin installer only.

## 1.0.0 — 2026-11-07

First public release.

### Added
- **Setup tab** in `/lisan config`: checks Python, downloads the selected language pack and overlay font into the plugin config folder, shows live progress, and restarts the translator when done. No more manual scripts or rebuilds for users.
- Custom Hugging Face repo download from the Setup tab (any `Helsinki-NLP/opus-mt-en-*` model).
- Plugin icon, GitHub release workflow, and `pluginmaster.json` so Lisan installs from a Dalamud custom repository.
- Worker start timeout and stderr capture: a broken Python environment now shows the real error in the Translator status instead of hanging.

### Changed
- Language packs are **no longer bundled** in the plugin zip or the git repository (the Arabic pack alone was 1.1 GB of LFS objects). The download now skips the Rust/TensorFlow mirrors, so Arabic is ~300 MB instead of ~1.1 GB.
- Packs that no longer exist on Hugging Face were remapped: Turkish → `opus-mt-tc-big-en-tr`; Polish → `opus-mt-en-sla` (`>>pol<<`); Persian → `opus-mt-en-mul` (`>>pes<<`); Thai → `opus-mt-en-mul` (`>>tha<<`).
- Worker protocol parsing uses `System.Text.Json`; `py` launcher is invoked with `-3`.
- `setup-models.ps1` accepts `-ModelRoot`, `-FontDir`, `-Force`, retries `pip` with `--user` on permission errors, and fails loudly with distinct exit codes.
- Version bumped to 1.0.0; description no longer mentions chat translation (removed in 0.7.0).

### Removed
- Tooltip translation experiment (0.7.x) and chat translation (0.6.x) — both removed before 1.0 as unreliable.

## 0.7.x — 2026-09 to 2026-11
- Balloon text defaults tuned several times (size 26, white text on dark panels; Talk balloon ink is black).
- Config migrations up to version 17.

## 0.6.0 — 2026-09
- Chat translation window (later removed).
- Arabic Windows TTS (`System.Speech`), history window, export, hotkeys.

## 0.5.0 and earlier
- Renamed from DialogueAr to Lisan; settings and cache migrate automatically.
- Talk, BattleTalk, TalkSubtitle, dialogue choices and Yes/No translation written onto the game balloon; plugin font overlay for Arabic and CJK.
