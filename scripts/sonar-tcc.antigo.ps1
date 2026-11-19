# sonar-tcc.ps1
# Analisa cada pasta de D:\Projects\TCC\src no SonarQube local e exporta as metricas para um CSV.
#
# Uso:
#   $env:SONAR_TOKEN = "sqa_..."
#   powershell -ExecutionPolicy Bypass -File .\sonar-tcc.ps1
#
# So exportar (sem reanalisar):
#   powershell -ExecutionPolicy Bypass -File .\sonar-tcc.ps1 -SkipAnalysis

param(
    [string]$SrcRoot  = "D:\Projects\TCC\src",
    [string]$SonarUrl = "http://localhost:9000",
    [string]$Token    = $env:SONAR_TOKEN,
    [string]$OutCsv   = "D:\Projects\TCC\sonar_metricas.csv",
    [switch]$SkipAnalysis
)

if (-not $Token) { throw "Defina o token antes: `$env:SONAR_TOKEN = 'sqa_...'" }

$headers = @{ Authorization = "Bearer $Token" }

# Metricas exportadas (ratings: 1 = A ... 5 = E)
$metrics = @(
    "ncloc", "complexity", "cognitive_complexity",
    "bugs", "vulnerabilities", "code_smells", "security_hotspots",
    "software_quality_reliability_issues",
    "software_quality_security_issues",
    "software_quality_maintainability_issues",
    "sqale_index", "sqale_debt_ratio",
    "reliability_rating", "security_rating", "sqale_rating",
    "duplicated_lines_density", "duplicated_blocks",
    "alert_status"
)

function Get-Key([string]$name) {
    "tcc-" + ($name.ToLower() -replace '[^a-z0-9_\-\.]', '-')
}

# Considera so as pastas que tem projeto .NET dentro
$folders = Get-ChildItem $SrcRoot -Directory | Where-Object {
    Get-ChildItem $_.FullName -Recurse -File -Include *.sln, *.slnx, *.csproj -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

if (-not $folders) { throw "Nenhuma pasta com projeto .NET encontrada em $SrcRoot" }
Write-Host "Pastas encontradas: $($folders.Count)" -ForegroundColor Green

# ---------- 1) Analise ----------
if (-not $SkipAnalysis) {
    foreach ($f in $folders) {
        $key = Get-Key $f.Name
        Write-Host "`n== Analisando $($f.Name)  ->  $key" -ForegroundColor Cyan
        Push-Location $f.FullName
        try {
            dotnet sonarscanner begin /k:"$key" /n:"$($f.Name)" /d:sonar.host.url="$SonarUrl" /d:sonar.token="$Token"
            if ($LASTEXITCODE -ne 0) { throw "begin falhou" }

            $sln = Get-ChildItem "$($f.FullName)\*" -File -Include *.sln, *.slnx -Name -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($sln) { dotnet build $sln --no-incremental } else { dotnet build --no-incremental }
            if ($LASTEXITCODE -ne 0) { throw "build falhou" }

            dotnet sonarscanner end /d:sonar.token="$Token"
            if ($LASTEXITCODE -ne 0) { throw "end falhou" }
        }
        catch { Write-Warning "$($f.Name): $_" }
        finally { Pop-Location }
    }
}

# ---------- 2) Espera o servidor processar ----------
function Wait-Sonar([string]$key) {
    for ($i = 0; $i -lt 60; $i++) {
        $r = Invoke-RestMethod "$SonarUrl/api/ce/component?component=$key" -Headers $headers
        if (-not $r.queue -or $r.queue.Count -eq 0) { return }
        Start-Sleep -Seconds 5
    }
    Write-Warning "$key ainda processando apos 5 min"
}

# ---------- 3) Exporta ----------
# Mantem so as metricas que existem nesta versao do SonarQube
try {
    $available = (Invoke-RestMethod "$SonarUrl/api/metrics/search?ps=500" -Headers $headers).metrics.key
    $missing = $metrics | Where-Object { $_ -notin $available }
    if ($missing) { Write-Warning "Metricas ignoradas (nao existem nesta versao): $($missing -join ', ')" }
    $metrics = $metrics | Where-Object { $_ -in $available }
}
catch { Write-Warning "Nao consegui listar metricas: $($_.ErrorDetails.Message) $_" }

$rows = foreach ($f in $folders) {
    $key = Get-Key $f.Name
    try {
        Wait-Sonar $key
        $url = "$SonarUrl/api/measures/component?component=$key&metricKeys=$($metrics -join ',')"
        $r = Invoke-RestMethod $url -Headers $headers

        $row = [ordered]@{ projeto = $f.Name; key = $key }
        foreach ($m in $metrics) {
            $row[$m] = ($r.component.measures | Where-Object metric -eq $m).value
        }
        [pscustomobject]$row
    }
    catch { Write-Warning "Sem dados para $key : $($_.ErrorDetails.Message) $_" }
}

if (-not $rows) {
    Write-Host "`nNenhum dado exportado. Projetos que existem no servidor:" -ForegroundColor Red
    try {
        (Invoke-RestMethod "$SonarUrl/api/projects/search?ps=500" -Headers $headers).components |
            ForEach-Object { Write-Host "  $($_.key)" }
    }
    catch { Write-Warning "Nao consegui listar projetos: $($_.ErrorDetails.Message) $_" }
    exit 1
}

$rows | Export-Csv $OutCsv -NoTypeInformation -Delimiter ';' -Encoding UTF8
Write-Host "`nCSV salvo em $OutCsv ($(@($rows).Count) projetos)" -ForegroundColor Green