# One-click Lisan setup: Python packages, model + font, Release build,
# old DialogueAr config copy, Dalamud Dev Plugin path.
#
# Double-click Install-Lisan.cmd  or:
#   .\Install-Lisan.ps1
#   .\Install-Lisan.ps1 -Language fr
#   .\Install-Lisan.ps1 -ForceModel
param(
    [string]$Language = "ar",
    [string]$Repo = "",
    [switch]$ForceModel,
    [switch]$SkipModel,
    [switch]$SkipBuild,
    [switch]$SkipDalamud
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$Proj = Join-Path $RepoRoot "Lisan"
$Models = Join-Path $Proj "Models"
$OutDir = Join-Path $Proj "bin\x64\Release"
$Dll = Join-Path $OutDir "Lisan.dll"
$DalamudConfig = Join-Path $env:APPDATA "XIVLauncher\dalamudConfig.json"
$PluginConfigs = Join-Path $env:APPDATA "XIVLauncher\pluginConfigs"

$Language = $Language.Trim().ToLowerInvariant()

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Fail([string]$msg) {
    Write-Host "ERROR: $msg" -ForegroundColor Red
    exit 1
}

function Get-PythonExe {
    $candidates = @(
        @{ Cmd = "python"; Args = @("-c", "import sys; print(sys.executable)") },
        @{ Cmd = "py"; Args = @("-3", "-c", "import sys; print(sys.executable)") }
    )
    foreach ($c in $candidates) {
        $cmd = Get-Command $c.Cmd -ErrorAction SilentlyContinue
        if (-not $cmd) { continue }
        try {
            $exe = (& $c.Cmd @($c.Args) 2>$null | Select-Object -Last 1)
            if ($exe -and (Test-Path $exe)) { return $exe.Trim() }
        } catch { }
    }
    return $null
}

function Test-ModelPresent([string]$code) {
    $known = @{
        ar = "opus-mt-en-ar"; fr = "opus-mt-en-fr"; de = "opus-mt-en-de"; es = "opus-mt-en-es"
        it = "opus-mt-en-it"; pt = "opus-mt-tc-big-en-pt"; nl = "opus-mt-en-nl"; pl = "opus-mt-en-pl"
        ru = "opus-mt-en-ru"; uk = "opus-mt-en-uk"; tr = "opus-mt-en-tr"; sv = "opus-mt-en-sv"
        da = "opus-mt-en-da"; fi = "opus-mt-en-fi"; cs = "opus-mt-en-cs"; ro = "opus-mt-en-ro"
        hu = "opus-mt-en-hu"; el = "opus-mt-en-el"; bg = "opus-mt-en-bg"; ja = "opus-mt-en-jap"
        zh = "opus-mt-en-zh"; ko = "opus-mt-tc-big-en-ko"; vi = "opus-mt-en-vi"; id = "opus-mt-en-id"
        fa = "opus-mt-en-fa"; hi = "opus-mt-en-hi"; th = "opus-mt-en-th"
    }
    $folder = $known[$code]
    if (-not $folder) { $folder = "opus-mt-en-$code" }
    $dir = Join-Path $Models $folder
    $cfg = Join-Path $dir "config.json"
    $bin = Join-Path $dir "pytorch_model.bin"
    $safe = Join-Path $dir "model.safetensors"
    return (Test-Path $cfg) -and ((Test-Path $bin) -or (Test-Path $safe))
}

Write-Host "=== Lisan installer ===" -ForegroundColor Green
Write-Host "Repo     : $RepoRoot"
Write-Host "Language : $Language"

# 1. Tools
Write-Step "Check tools"

$python = Get-PythonExe
if (-not $python) {
    Fail "Python 3.10+ not found. Install from https://www.python.org/downloads/ and tick Add python.exe to PATH."
}
$env:LISAN_PYTHON = $python
$pyVer = & $python --version 2>&1
Write-Host "Python   : $pyVer"
Write-Host "          $python"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail "dotnet SDK not found. Install .NET 10 SDK from https://dotnet.microsoft.com/download"
}
Write-Host "dotnet   : $(& dotnet --version)"

$dalamudHooks = Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev"
if (-not (Test-Path $dalamudHooks)) {
    Write-Host "WARN: Dalamud not found at $dalamudHooks" -ForegroundColor Yellow
    Write-Host "      Install XIVLauncher + Dalamud, log in once, then re-run this installer if the build fails."
} else {
    Write-Host "Dalamud  : $dalamudHooks"
}

if (-not (Test-Path (Join-Path $Proj "Lisan.csproj"))) {
    Fail "Project missing: $Proj\Lisan.csproj"
}

# 2. Model + Python packages
$needModel = $ForceModel -or (-not $SkipModel -and -not (Test-ModelPresent $Language))
if ($SkipModel) {
    Write-Step "Skip model download"
} elseif (-not $needModel) {
    Write-Step "Model already on disk; skip download (use -ForceModel to redo)"
    Write-Host "Checking Python packages (torch / transformers)..."
    & $python -c 'import transformers, torch; print("packages ok")'
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Packages missing; running setup-models.ps1 for pip + model"
        $needModel = $true
    }
}

if ($needModel) {
    Write-Step "Download model + font (internet once)"
    $setup = Join-Path $Models "setup-models.ps1"
    $setupArgs = @{ Language = $Language }
    if (-not [string]::IsNullOrWhiteSpace($Repo)) { $setupArgs.Repo = $Repo }
    & $setup @setupArgs
    if ($LASTEXITCODE -ne 0) { Fail "setup-models.ps1 failed" }
}

# 3. Build
if ($SkipBuild) {
    Write-Step "Skip build"
} else {
    Write-Step "Build Release (Lisan.dll)"
    Push-Location $Proj
    try {
        & dotnet build -c Release -p:Platform=x64
        if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed" }
    } finally {
        Pop-Location
    }
}

if (-not (Test-Path $Dll)) {
    Fail "Build output missing: $Dll"
}
Write-Host "DLL      : $Dll"

# 4. Migrate DialogueAr settings
Write-Step "Keep old DialogueAr settings / cache if present"
if (Test-Path $PluginConfigs) {
    $oldJson = Join-Path $PluginConfigs "DialogueAr.json"
    $newJson = Join-Path $PluginConfigs "Lisan.json"
    $oldDir = Join-Path $PluginConfigs "DialogueAr"
    $newDir = Join-Path $PluginConfigs "Lisan"
    if ((Test-Path $oldJson) -and -not (Test-Path $newJson)) {
        Copy-Item $oldJson $newJson -Force
        Write-Host "Copied DialogueAr.json -> Lisan.json"
    }
    if ((Test-Path $oldDir) -and -not (Test-Path $newDir)) {
        Copy-Item $oldDir $newDir -Recurse -Force
        Write-Host "Copied pluginConfigs\DialogueAr -> Lisan"
    }
    if ((Test-Path $oldDir) -and (Test-Path $newDir)) {
        Get-ChildItem $oldDir -File | ForEach-Object {
            $dest = Join-Path $newDir $_.Name
            if (-not (Test-Path $dest)) {
                Copy-Item $_.FullName $dest
                Write-Host "Copied missing $($_.Name)"
            }
        }
    }
    if (-not (Test-Path $oldJson) -and -not (Test-Path $oldDir)) {
        Write-Host "No old DialogueAr config; skip"
    }
} else {
    Write-Host "No XIVLauncher pluginConfigs yet; skip (ok on first install)"
}

# 5. Register Dalamud Dev Plugin
if ($SkipDalamud) {
    Write-Step "Skip Dalamud Dev Plugin register"
} elseif (-not (Test-Path $DalamudConfig)) {
    Write-Step "Dalamud config missing; add Dev Plugin by hand later"
    Write-Host "Path to add: $Dll"
} else {
    Write-Step "Register Dev Plugin in dalamudConfig.json"
    $backup = "$DalamudConfig.bak-lisan"
    Copy-Item $DalamudConfig $backup -Force
    Write-Host "Backup   : $backup"

    $pyReg = @'
import json, os, uuid

path = os.environ["LISAN_DALAMUD_CONFIG"]
dll = os.environ["LISAN_DLL"]
dll_norm = os.path.normcase(os.path.abspath(dll))

with open(path, encoding="utf-8") as f:
    cfg = json.load(f)

cfg["DevMode"] = True

locs = cfg.get("DevPluginLoadLocations") or {}
values = locs.get("$values") or []
kept = []
seen = False
for item in values:
    p = item.get("Path") or ""
    pn = os.path.normcase(os.path.abspath(p)) if p else ""
    if "dialoguear.dll" in pn.replace("/", "\\"):
        item["IsEnabled"] = False
        kept.append(item)
        continue
    if pn == dll_norm:
        item["IsEnabled"] = True
        seen = True
    kept.append(item)
if not seen:
    kept.append({
        "$type": "Dalamud.Configuration.DevPluginLocationSettings, Dalamud",
        "Path": dll,
        "IsEnabled": True,
        "Nickname": None,
    })
locs["$type"] = "System.Collections.Generic.List`1[[Dalamud.Configuration.DevPluginLocationSettings, Dalamud]], System.Private.CoreLib"
locs["$values"] = kept
cfg["DevPluginLoadLocations"] = locs

settings = cfg.get("DevPluginSettings") or {}
settings["$type"] = "System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[Dalamud.Configuration.Internal.DevPluginSettings, Dalamud]], System.Private.CoreLib"
if dll not in settings:
    settings[dll] = {
        "$type": "Dalamud.Configuration.Internal.DevPluginSettings, Dalamud",
        "StartOnBoot": True,
        "NotifyForErrors": True,
        "AutomaticReloading": True,
        "WorkingPluginId": str(uuid.uuid4()),
        "DismissedValidationProblems": {
            "$type": "System.Collections.Generic.List`1[[System.String, System.Private.CoreLib]], System.Private.CoreLib",
            "$values": [],
        },
    }
else:
    settings[dll]["StartOnBoot"] = True
cfg["DevPluginSettings"] = settings

tmp = path + ".lisan-tmp"
with open(tmp, "w", encoding="utf-8", newline="\n") as f:
    json.dump(cfg, f, indent=2, ensure_ascii=False)
    f.write("\n")
os.replace(tmp, path)
print("registered", dll)
'@
    $env:LISAN_DALAMUD_CONFIG = $DalamudConfig
    $env:LISAN_DLL = $Dll
    $tmpPy = Join-Path $env:TEMP "lisan-register-devplugin.py"
    [System.IO.File]::WriteAllText($tmpPy, $pyReg, [System.Text.UTF8Encoding]::new($false))
    & $python $tmpPy
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARN: could not edit dalamudConfig.json. Restore backup if the file looks wrong." -ForegroundColor Yellow
        Write-Host "      Add Dev Plugin by hand: $Dll"
    } else {
        Write-Host "Enabled : $Dll"
        Write-Host "Disabled old DialogueAr.dll entries (if any)."
        Write-Host "If the game is already running, reload plugins (or restart the game) so Dalamud picks this up."
    }
    Remove-Item $tmpPy -ErrorAction SilentlyContinue
}

# Done
Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Dev plugin path: $Dll"
Write-Host ""
Write-Host "Still you (Dalamud cannot click itself):"
Write-Host "  1. FFXIV + Dalamud running"
Write-Host "  2. If Dev Plugin list did not refresh: Settings > Experimental > add/enable Lisan > Reload"
Write-Host "  3. /lisan config  -> language = $Language"
Write-Host "  4. Game client language = English"
Write-Host ""
Write-Host "Other language later:"
Write-Host "  .\Install-Lisan.ps1 -Language fr"
Write-Host "  /lisan lang fr"
