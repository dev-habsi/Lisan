# One-time local model + font setup for DialogueAr (no translation cloud API).
# Examples:
#   .\setup-models.ps1
#   .\setup-models.ps1 -Language fr
#   .\setup-models.ps1 -Language ja
#   .\setup-models.ps1 -Language zh -Repo Helsinki-NLP/opus-mt-en-zh
param(
    [string]$Language = "ar",
    [string]$Repo = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$FontDir = Join-Path (Split-Path -Parent $Root) "Fonts"

# code = @{ Repo; Folder; FontFile; FontUrl }
$Packs = @{
    ar = @{ Repo = "Helsinki-NLP/opus-mt-en-ar"; Folder = "opus-mt-en-ar"; FontFile = "NotoNaskhArabic-Regular.ttf"; FontUrl = "https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf/NotoNaskhArabic/NotoNaskhArabic-Regular.ttf" }
    fr = @{ Repo = "Helsinki-NLP/opus-mt-en-fr"; Folder = "opus-mt-en-fr" }
    de = @{ Repo = "Helsinki-NLP/opus-mt-en-de"; Folder = "opus-mt-en-de" }
    es = @{ Repo = "Helsinki-NLP/opus-mt-en-es"; Folder = "opus-mt-en-es" }
    it = @{ Repo = "Helsinki-NLP/opus-mt-en-it"; Folder = "opus-mt-en-it" }
    pt = @{ Repo = "Helsinki-NLP/opus-mt-tc-big-en-pt"; Folder = "opus-mt-tc-big-en-pt" }
    nl = @{ Repo = "Helsinki-NLP/opus-mt-en-nl"; Folder = "opus-mt-en-nl" }
    pl = @{ Repo = "Helsinki-NLP/opus-mt-en-pl"; Folder = "opus-mt-en-pl" }
    ru = @{ Repo = "Helsinki-NLP/opus-mt-en-ru"; Folder = "opus-mt-en-ru" }
    uk = @{ Repo = "Helsinki-NLP/opus-mt-en-uk"; Folder = "opus-mt-en-uk" }
    tr = @{ Repo = "Helsinki-NLP/opus-mt-en-tr"; Folder = "opus-mt-en-tr" }
    sv = @{ Repo = "Helsinki-NLP/opus-mt-en-sv"; Folder = "opus-mt-en-sv" }
    da = @{ Repo = "Helsinki-NLP/opus-mt-en-da"; Folder = "opus-mt-en-da" }
    fi = @{ Repo = "Helsinki-NLP/opus-mt-en-fi"; Folder = "opus-mt-en-fi" }
    cs = @{ Repo = "Helsinki-NLP/opus-mt-en-cs"; Folder = "opus-mt-en-cs" }
    ro = @{ Repo = "Helsinki-NLP/opus-mt-en-ro"; Folder = "opus-mt-en-ro" }
    hu = @{ Repo = "Helsinki-NLP/opus-mt-en-hu"; Folder = "opus-mt-en-hu" }
    el = @{ Repo = "Helsinki-NLP/opus-mt-en-el"; Folder = "opus-mt-en-el" }
    bg = @{ Repo = "Helsinki-NLP/opus-mt-en-bg"; Folder = "opus-mt-en-bg" }
    ja = @{ Repo = "Helsinki-NLP/opus-mt-en-jap"; Folder = "opus-mt-en-jap"; FontFile = "NotoSansCJKjp-Regular.otf"; FontUrl = "https://github.com/notofonts/noto-cjk/raw/main/Sans/OTF/Japanese/NotoSansCJKjp-Regular.otf" }
    zh = @{ Repo = "Helsinki-NLP/opus-mt-en-zh"; Folder = "opus-mt-en-zh"; FontFile = "NotoSansCJKsc-Regular.otf"; FontUrl = "https://github.com/notofonts/noto-cjk/raw/main/Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf" }
    ko = @{ Repo = "Helsinki-NLP/opus-mt-tc-big-en-ko"; Folder = "opus-mt-tc-big-en-ko"; FontFile = "NotoSansCJKkr-Regular.otf"; FontUrl = "https://github.com/notofonts/noto-cjk/raw/main/Sans/OTF/Korean/NotoSansCJKkr-Regular.otf" }
    vi = @{ Repo = "Helsinki-NLP/opus-mt-en-vi"; Folder = "opus-mt-en-vi" }
    id = @{ Repo = "Helsinki-NLP/opus-mt-en-id"; Folder = "opus-mt-en-id" }
    he = @{ Repo = "Helsinki-NLP/opus-mt-en-he"; Folder = "opus-mt-en-he"; FontFile = "NotoSansHebrew-Regular.ttf"; FontUrl = "https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf/NotoSansHebrew/NotoSansHebrew-Regular.ttf" }
    fa = @{ Repo = "Helsinki-NLP/opus-mt-en-fa"; Folder = "opus-mt-en-fa"; FontFile = "NotoNaskhArabic-Regular.ttf"; FontUrl = "https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf/NotoNaskhArabic/NotoNaskhArabic-Regular.ttf" }
    hi = @{ Repo = "Helsinki-NLP/opus-mt-en-hi"; Folder = "opus-mt-en-hi"; FontFile = "NotoSansDevanagari-Regular.ttf"; FontUrl = "https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf/NotoSansDevanagari/NotoSansDevanagari-Regular.ttf" }
    th = @{ Repo = "Helsinki-NLP/opus-mt-en-th"; Folder = "opus-mt-en-th"; FontFile = "NotoSansThai-Regular.ttf"; FontUrl = "https://github.com/googlefonts/noto-fonts/raw/main/hinted/ttf/NotoSansThai/NotoSansThai-Regular.ttf" }
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

$ModelDir = Join-Path $Root $pack.Folder

Write-Host "=== DialogueAr model setup ==="
Write-Host "Language : $Language"
Write-Host "Repo     : $($pack.Repo)"
Write-Host "Model dir: $ModelDir"

Write-Host "[1/3] Python packages..."
python -m pip install --upgrade pip
python -m pip install "torch" --index-url https://download.pytorch.org/whl/cpu
python -m pip install "transformers>=4.40" sentencepiece huggingface_hub

Write-Host "[2/3] Download $($pack.Repo) (local files only after this)..."
$env:DIALOGUEAR_MODEL = $ModelDir
$env:DIALOGUEAR_REPO = $pack.Repo
python -c "from huggingface_hub import snapshot_download; import os; d=os.environ['DIALOGUEAR_MODEL']; r=os.environ['DIALOGUEAR_REPO']; snapshot_download(repo_id=r, local_dir=d, local_dir_use_symlinks=False); print('OK', r, '->', d)"

New-Item -ItemType Directory -Force -Path $FontDir | Out-Null
if ($pack.FontFile -and $pack.FontUrl) {
    $FontPath = Join-Path $FontDir $pack.FontFile
    if (-not (Test-Path $FontPath)) {
        Write-Host "[3/3] Download overlay font $($pack.FontFile)..."
        try {
            Invoke-WebRequest -Uri $pack.FontUrl -OutFile $FontPath -UseBasicParsing
            Write-Host "Font: $FontPath"
        } catch {
            Write-Host "Font download failed (place the file in Fonts\ yourself): $_"
        }
    } else {
        Write-Host "[3/3] Font already present."
    }
} else {
    Write-Host "[3/3] No extra font required for this language (Dalamud default is enough)."
}

Write-Host ""
Write-Host "Done. In-game: /dialogar lang $Language   or pick the language in config, then reload if the worker was already running."
Write-Host "Optional TTS (Admin PowerShell), example French:"
Write-Host "  Add-WindowsCapability -Online -Name Language.TextToSpeech~~~fr-FR~0.0.1.0"
