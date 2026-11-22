# Builds the Laya that ships with the game: <game>\laya\python (embeddable Python 3.11 with CPU PyTorch and Laya)
# and <game>\laya\hf (only the English weights, 810 MB). The game then starts it by itself (Boot -> LayaHost) and needs
# nothing installed on the PC; no GPU is needed (about 200 ms per classification on a CPU, 30 ms on a GPU).
#
#   pwsh Tools/Package-Laya.ps1 -Game <game folder> [-Gpu]
#
# Needs internet once (Python, PyTorch, Laya, weights). Run it again to repair or update; existing parts are kept.
# -Gpu installs the default CUDA PyTorch instead of the CPU one (about 4 GB larger, only worthwhile on an NVIDIA GPU).
param(
    [Parameter(Mandatory)][string]$Game,
    [switch]$Gpu,
    [string]$PythonVersion = '3.11.9'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path $Game 'laya'
$py = Join-Path $root 'python'
$exe = Join-Path $py 'python.exe'
$hf = Join-Path $root 'hf'
New-Item -ItemType Directory -Force $py, $hf | Out-Null
function Step($t) { Write-Host "== $t" -ForegroundColor Cyan }
function Run { & $exe @args; if ($LASTEXITCODE -ne 0) { throw "python $($args -join ' ') failed ($LASTEXITCODE)" } }

if (-not (Test-Path $exe)) {
    Step "Python $PythonVersion (embeddable)"
    $zip = Join-Path ([IO.Path]::GetTempPath()) "python-$PythonVersion-embed-amd64.zip"
    Invoke-WebRequest "https://www.python.org/ftp/python/$PythonVersion/python-$PythonVersion-embed-amd64.zip" -OutFile $zip
    Expand-Archive $zip $py -Force
    # the embeddable build ignores site-packages until its ._pth file says so
    $pth = Get-ChildItem $py -Filter 'python*._pth' | Select-Object -First 1
    $lines = Get-Content $pth.FullName | ForEach-Object { if ($_ -match '^#\s*import site') { 'import site' } else { $_ } }
    if ($lines -notcontains 'Lib\site-packages') { $lines = @($lines[0], 'Lib\site-packages') + $lines[1..($lines.Count - 1)] }
    Set-Content $pth.FullName $lines
    New-Item -ItemType Directory -Force (Join-Path $py 'Lib\site-packages') | Out-Null
}

& $exe -c "import pip" 2>$null
if ($LASTEXITCODE -ne 0) {
    Step 'pip'
    $gp = Join-Path ([IO.Path]::GetTempPath()) 'get-pip.py'
    Invoke-WebRequest 'https://bootstrap.pypa.io/get-pip.py' -OutFile $gp
    Run $gp --no-warn-script-location
}

$env:PYTHONNOUSERSITE = '1'
& $exe -c "import torch, laya, uvicorn, fastapi" 2>$null
if ($LASTEXITCODE -ne 0) {
    if (-not $Gpu) { Step 'PyTorch (CPU build)'; Run -m pip install --no-warn-script-location torch --index-url https://download.pytorch.org/whl/cpu }
    Step 'Laya'
    Run -m pip install --no-warn-script-location "laya[serve]"   # [serve] brings the web server (uvicorn, fastapi)
}

Step 'English weights'
$env:HF_HOME = $hf
Run -c "from huggingface_hub import snapshot_download; print(snapshot_download('convaiinnovations/laya', allow_patterns=['model.safetensors','rl_agent_config.json','encoder/*','tokenizer/*']))"

Step 'Self-test (offline, as the game runs it)'
$env:HF_HUB_OFFLINE = '1'
$port = 8790; $key = 'selftest'
$env:LAYA_HOST = '127.0.0.1'; $env:LAYA_PORT = "$port"; $env:LAYA_API_KEY = $key; $env:LAYA_MODELS = 'english'
$srv = Start-Process $exe -ArgumentList '-m', 'laya.serve' -PassThru -WindowStyle Hidden
try {
    $body = @{ state = @{ body = 'Betty is warm and in a great mood.' }; questions = @{ q = @{ type = 'choice'; instructions = 'How does this person take a hug?'; criteria = @{ cold = 'distant'; receptive = 'warm and open' } } } } | ConvertTo-Json -Depth 6
    $ok = $false
    for ($i = 0; $i -lt 120 -and -not $ok; $i++) {
        Start-Sleep 1
        try { $r = Invoke-RestMethod "http://127.0.0.1:$port/v1/systemone" -Method Post -Body $body -ContentType 'application/json' -Headers @{ Authorization = "Bearer $key" } -TimeoutSec 5; $ok = $true } catch { }
    }
    if (-not $ok) { throw 'the bundled Laya did not answer' }
    Write-Host ("answer: " + $r.answers.q.choice) -ForegroundColor Green
}
finally { if ($srv -and -not $srv.HasExited) { $srv.Kill() } }
$mb = [math]::Round((Get-ChildItem $root -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host "Laya is bundled in $root ($mb MB)." -ForegroundColor Green
