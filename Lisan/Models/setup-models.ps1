# One-time local model + font setup for Lisan (no translation cloud API).
# The in-game Setup tab runs this with -ModelRoot / -FontDir pointing at the plugin config folder.
# Examples:
#   .\setup-models.ps1
#   .\setup-models.ps1 -Language ar
#   .\setup-models.ps1 -Language ja
#   .\setup-models.ps1 -Language xx -Repo Helsinki-NLP/opus-mt-en-xx
#   .\setup-models.ps1 -Language ar -ModelRoot "$env:APPDATA\XIVLauncher\pluginConfigs\Lisan\Models"
param(
    [string]$Language = "ar",
    [string]$Repo = "",
    [string]$ModelRoot = "",
    [string]$FontDir = "",
    [switch]$Force
)

# Native tools (pip, python) write progress to stderr; with "Stop" PowerShell 5.1 would abort on it.
$ErrorActionPreference = "Continue"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ModelRoot)) { $ModelRoot = $Root }
if ([string]::IsNullOrWhiteSpace($FontDir)) { $FontDir = Join-Path (Split-Path -Parent $Root) "Fonts" }
$Python = if (-not [string]::IsNullOrWhiteSpace($env:LISAN_PYTHON)) { $env:LISAN_PYTHON } else { "python" }

# Plain output when stdout is a pipe (the in-game log), UTF-8 everywhere.
$env:PIP_PROGRESS_BAR = "off"
$env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
$env:HF_HUB_DISABLE_PROGRESS_BARS = "1"
$env:PYTHONIOENCODING = "utf-8"
$env:PYTHONUTF8 = "1"

# code = @{ Repo; Folder; FontFile; FontUrl }  — keep in sync with TargetLanguages.cs
$NotoCjk = "https://github.com/notofonts/noto-cjk/raw/main/Sans/OTF"
$Noto = "https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf"
$Packs = @{
    ar = @{ Repo = "Helsinki-NLP/opus-mt-en-ar"; Folder = "opus-mt-en-ar"; FontFile = "NotoNaskhArabic-Regular.ttf"; FontUrl = "$Noto/NotoNaskhArabic/NotoNaskhArabic-Regular.ttf" }
    de = @{ Repo = "Helsinki-NLP/opus-mt-en-de"; Folder = "opus-mt-en-de" }
    es = @{ Repo = "Helsinki-NLP/opus-mt-en-es"; Folder = "opus-mt-en-es" }
    it = @{ Repo = "Helsinki-NLP/opus-mt-en-it"; Folder = "opus-mt-en-it" }
    pt = @{ Repo = "Helsinki-NLP/opus-mt-tc-big-en-pt"; Folder = "opus-mt-tc-big-en-pt" }
    nl = @{ Repo = "Helsinki-NLP/opus-mt-en-nl"; Folder = "opus-mt-en-nl" }
    pl = @{ Repo = "Helsinki-NLP/opus-mt-en-sla"; Folder = "opus-mt-en-sla" }
    ru = @{ Repo = "Helsinki-NLP/opus-mt-en-ru"; Folder = "opus-mt-en-ru" }
    uk = @{ Repo = "Helsinki-NLP/opus-mt-en-uk"; Folder = "opus-mt-en-uk" }
    tr = @{ Repo = "Helsinki-NLP/opus-mt-tc-big-en-tr"; Folder = "opus-mt-tc-big-en-tr" }
    sv = @{ Repo = "Helsinki-NLP/opus-mt-en-sv"; Folder = "opus-mt-en-sv" }
    da = @{ Repo = "Helsinki-NLP/opus-mt-en-da"; Folder = "opus-mt-en-da" }
    fi = @{ Repo = "Helsinki-NLP/opus-mt-en-fi"; Folder = "opus-mt-en-fi" }
    cs = @{ Repo = "Helsinki-NLP/opus-mt-en-cs"; Folder = "opus-mt-en-cs" }
    ro = @{ Repo = "Helsinki-NLP/opus-mt-en-ro"; Folder = "opus-mt-en-ro" }
    hu = @{ Repo = "Helsinki-NLP/opus-mt-en-hu"; Folder = "opus-mt-en-hu" }
    el = @{ Repo = "Helsinki-NLP/opus-mt-en-el"; Folder = "opus-mt-en-el" }
    bg = @{ Repo = "Helsinki-NLP/opus-mt-en-bg"; Folder = "opus-mt-en-bg" }
    ja = @{ Repo = "Helsinki-NLP/opus-mt-en-jap"; Folder = "opus-mt-en-jap"; FontFile = "NotoSansCJKjp-Regular.otf"; FontUrl = "$NotoCjk/Japanese/NotoSansCJKjp-Regular.otf" }
    zh = @{ Repo = "Helsinki-NLP/opus-mt-en-zh"; Folder = "opus-mt-en-zh"; FontFile = "NotoSansCJKsc-Regular.otf"; FontUrl = "$NotoCjk/SimplifiedChinese/NotoSansCJKsc-Regular.otf" }
    ko = @{ Repo = "Helsinki-NLP/opus-mt-tc-big-en-ko"; Folder = "opus-mt-tc-big-en-ko"; FontFile = "NotoSansCJKkr-Regular.otf"; FontUrl = "$NotoCjk/Korean/NotoSansCJKkr-Regular.otf" }
    vi = @{ Repo = "Helsinki-NLP/opus-mt-en-vi"; Folder = "opus-mt-en-vi" }
    id = @{ Repo = "Helsinki-NLP/opus-mt-en-id"; Folder = "opus-mt-en-id" }
    fa = @{ Repo = "Helsinki-NLP/opus-mt-en-mul"; Folder = "opus-mt-en-mul"; FontFile = "NotoNaskhArabic-Regular.ttf"; FontUrl = "$Noto/NotoNaskhArabic/NotoNaskhArabic-Regular.ttf" }
    hi = @{ Repo = "Helsinki-NLP/opus-mt-en-hi"; Folder = "opus-mt-en-hi"; FontFile = "NotoSansDevanagari-Regular.ttf"; FontUrl = "$Noto/NotoSansDevanagari/NotoSansDevanagari-Regular.ttf" }
    th = @{ Repo = "Helsinki-NLP/opus-mt-en-mul"; Folder = "opus-mt-en-mul"; FontFile = "NotoSansThai-Regular.ttf"; FontUrl = "$Noto/NotoSansThai/NotoSansThai-Regular.ttf" }
}

$Language = $Language.Trim().ToLowerInvariant()
if ($Language -eq "list") {
    Write-Host "Built-in language codes:"
    $Packs.Keys | Sort-Object | ForEach-Object {
        $p = $Packs[$_]
        Write-Host ("  {0,-4}  {1}" -f $_, $p.Repo)
    }
    Write-Host "Use: .\setup-models.ps1 -Language <code>"
    Write-Host "Override repo: .\setup-models.ps1 -Language xx -Repo Helsinki-NLP/opus-mt-en-xx"
    exit 0
}

if (-not $Packs.ContainsKey($Language)) {
    if ([string]::IsNullOrWhiteSpace($Repo)) {
        Write-Host "Unknown language '$Language'."
        Write-Host "Run: .\setup-models.ps1 -Language list"
        Write-Host "Or pass any Hugging Face repo: .\setup-models.ps1 -Language $Language -Repo Helsinki-NLP/opus-mt-en-$Language"
        exit 1
    }
    $Packs[$Language] = @{ Repo = $Repo; Folder = ($Repo.Split('/')[-1]) }
}

$pack = $Packs[$Language]
if (-not [string]::IsNullOrWhiteSpace($Repo)) {
    $pack.Repo = $Repo
    $pack.Folder = $Repo.Split('/')[-1]
}

$ModelDir = Join-Path $ModelRoot $pack.Folder

Write-Host "=== Lisan model setup ==="
Write-Host "Language : $Language"
Write-Host "Repo     : $($pack.Repo)"
Write-Host "Model dir: $ModelDir"
Write-Host "Font dir : $FontDir"
Write-Host "Python   : $Python"

# Native stderr arrives as ErrorRecords when our own stderr is redirected (in-game log); print it as plain text.
function Invoke-Native([string[]]$CmdArgs) {
    & $Python @CmdArgs 2>&1 | ForEach-Object { "$_" } | Out-Host
    return $LASTEXITCODE
}

# System-wide Pythons (C:\Python3xx, Program Files) need admin for site-packages: fall back to --user.
function Invoke-Pip([string[]]$PipArgs) {
    $code = Invoke-Native (@("-m", "pip", "install") + $PipArgs)
    if ($code -ne 0) {
        Write-Host "pip install failed; retrying with --user"
        $code = Invoke-Native (@("-m", "pip", "install", "--user") + $PipArgs)
    }
    return $code
}

Write-Host "[1/3] Python packages (torch CPU, transformers, sentencepiece)..."
if ((Invoke-Pip @("torch", "--index-url", "https://download.pytorch.org/whl/cpu")) -ne 0) { Write-Host "ERROR: torch install failed."; exit 3 }
if ((Invoke-Pip @("transformers>=4.40", "sentencepiece", "sacremoses", "huggingface_hub")) -ne 0) { Write-Host "ERROR: transformers install failed."; exit 3 }

$weightsPresent = (Test-Path (Join-Path $ModelDir "config.json")) -and
    ((Test-Path (Join-Path $ModelDir "pytorch_model.bin")) -or (Test-Path (Join-Path $ModelDir "model.safetensors")))
if ($weightsPresent -and -not $Force) {
    Write-Host "[2/3] Model already on disk; skip download (use -Force to redo)."
} else {
    Write-Host "[2/3] Download $($pack.Repo) (internet once; local files only after this)..."
    New-Item -ItemType Directory -Force -Path $ModelDir | Out-Null
    $env:LISAN_MODEL = $ModelDir
    $env:LISAN_REPO = $pack.Repo
    # Only tokenizer + one set of weights: skips the rust/tf mirrors (hundreds of MB each).
    $dl = @'
import os
from huggingface_hub import snapshot_download, list_repo_files
d = os.environ["LISAN_MODEL"]
r = os.environ["LISAN_REPO"]
files = list_repo_files(r)
ignore = ["*.ot", "*.h5", "*.msgpack", "*.onnx", "*.tflite", ".git*"]
if "model.safetensors" in files:
    ignore.append("pytorch_model.bin")
snapshot_download(repo_id=r, local_dir=d, ignore_patterns=ignore)
print("OK", r, "->", d)
'@
    $dlFile = Join-Path $env:TEMP "lisan-download-model.py"
    [System.IO.File]::WriteAllText($dlFile, $dl, [System.Text.UTF8Encoding]::new($false))
    $code = Invoke-Native @($dlFile)
    Remove-Item $dlFile -ErrorAction SilentlyContinue
    if ($code -ne 0) { Write-Host "ERROR: model download failed (check the repo id and your connection)."; exit 2 }
}

New-Item -ItemType Directory -Force -Path $FontDir | Out-Null
if ($pack.FontFile -and $pack.FontUrl) {
    $FontPath = Join-Path $FontDir $pack.FontFile
    if (-not (Test-Path $FontPath)) {
        Write-Host "[3/3] Download overlay font $($pack.FontFile)..."
        try {
            Invoke-WebRequest -Uri $pack.FontUrl -OutFile $FontPath -UseBasicParsing -ErrorAction Stop
            Write-Host "Font: $FontPath"
        } catch {
            Write-Host "ERROR: font download failed (place $($pack.FontFile) in $FontDir yourself): $_"
            exit 4
        }
    } else {
        Write-Host "[3/3] Font already present."
    }
} else {
    Write-Host "[3/3] No extra font required for this language (Dalamud default is enough)."
}

Write-Host ""
Write-Host "Done. In-game: /lisan lang $Language   or pick the language in config, then 'Apply language / restart worker'."
exit 0
