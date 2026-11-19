<#
  sonar-tcc.ps1
  Analisa no SonarQube local o código final de cada execução em
  D:\Projects\TCC_GitHub\TCC\runs\<modelo>\<esforço>\<prompt>\run-NN\src\<solução>
  e exporta as métricas para um CSV.

  Uso:
    $env:SONAR_TOKEN = "squ_..."   # token de usuário admin
    powershell -ExecutionPolicy Bypass -File .\sonar-tcc.ps1

  Filtros (opcionais, combináveis):
    ... -Modelo sonnet -Esforco high -Prompt zero_shot -Execucao 1

  Só exportar (sem reanalisar):
    ... -SkipAnalysis

  A análise é feita numa cópia temporária da solução (sem bin/obj), então a pasta
  da execução não é alterada.

  Cada projeto é apagado no SonarQube antes de ser reanalisado, para que toda análise
  seja a primeira (sem "código novo"; o quality gate não compara com análises antigas).
  O token precisa de permissão de administrador (token de usuário, squ_...).

  Só as regras do Sonar são medidas: avisos do compilador/analisadores do .NET
  (issues external_roslyn:*) são ignorados com sonar.cs.roslyn.ignoreIssues=true,
  porque entram sem esforço de correção e dependem do .csproj gerado pelo modelo.
#>
param(
    [string]$TccRoot  = "D:\Projects\TCC_GitHub\TCC",
    [string]$Modelo,
    [string]$Esforco,
    [string]$Prompt,
    [int]$Execucao    = 0,
    [string]$SonarUrl = "http://localhost:9000",
    [string]$Token    = $env:SONAR_TOKEN,
    [string]$OutCsv   = "D:\Projects\TCC_GitHub\TCC\sonar_metricas.csv",
    [switch]$SkipAnalysis
)

if (-not $Token) { throw "Defina o token antes: `$env:SONAR_TOKEN = 'squ_...'" }
$headers = @{ Authorization = "Bearer $Token" }

# Métricas exportadas (ratings: 1 = A ... 5 = E)
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

# ---------- 0) Encontra as soluções ----------
$runsRoot = (Resolve-Path (Join-Path $TccRoot 'runs')).Path.TrimEnd('\')
if ($Prompt) { $Prompt = [IO.Path]::GetFileNameWithoutExtension($Prompt) }
$runName = if ($Execucao -gt 0) { 'run-{0:D2}' -f $Execucao } else { $null }

$runs = Get-ChildItem $runsRoot -Directory -Recurse -Filter 'run-*' | Where-Object {
    $p = $_.FullName.Substring($runsRoot.Length + 1) -split '\\'
    $p.Count -eq 4 -and
    (-not $Modelo  -or $p[0] -eq $Modelo) -and
    (-not $Esforco -or $p[1] -eq $Esforco) -and
    (-not $Prompt  -or $p[2] -eq $Prompt) -and
    (-not $runName -or $p[3] -eq $runName)
} | Sort-Object FullName

$items = foreach ($run in $runs) {
    $p = $run.FullName.Substring($runsRoot.Length + 1) -split '\\'
    $srcDir = Join-Path $run.FullName 'src'
    if (-not (Test-Path $srcDir)) { continue }
    $sols = Get-ChildItem $srcDir -Directory | Where-Object {
        Get-ChildItem $_.FullName -File -Include *.sln, *.slnx, *.csproj -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -First 1
    }
    foreach ($s in $sols) {
        # Última iteração avaliada (o código na pasta é sempre o da última iteração)
        $resDir = Join-Path $TccRoot "resultados\$($p[0])\$($p[1])\$($p[2])\$($p[3])\$($s.Name)"
        $iters = @(Get-ChildItem $resDir -Filter "$($s.Name)_i*.json" -ErrorAction SilentlyContinue |
            ForEach-Object { if ($_.Name -match '_i(\d+)\.json$') { [int]$Matches[1] } })
        $ultima = if ($iters) { ($iters | Measure-Object -Maximum).Maximum } else { $null }

        [pscustomobject]@{
            modelo   = $p[0]
            esforco  = $p[1]
            prompt   = $p[2]
            execucao = $p[3]
            solucao  = $s.Name
            iteracao_final = $ultima
            path     = $s.FullName
            key      = Get-Key "$($p[0])-$($p[1])-$($p[2])-$($p[3])-$($s.Name)"
            name     = "$($p[0]) / $($p[1]) / $($p[2]) / $($p[3]) / $($s.Name)"
        }
    }
}

if (-not $items) { throw "Nenhuma solução .NET encontrada em $runsRoot (com os filtros informados)" }
Write-Host "Soluções encontradas: $(@($items).Count)" -ForegroundColor Green

# ---------- 1) Análise ----------
if (-not $SkipAnalysis) {
    $tmpRoot = Join-Path $env:TEMP 'tcc-sonar'
    # Encerra processos do MSBuild que ficaram vivos e prendem as DLLs do scanner,
    # e limpa sobras de execuções anteriores (o que estiver preso fica para depois)
    dotnet build-server shutdown | Out-Null
    if (Test-Path $tmpRoot) { Remove-Item "$tmpRoot\*" -Recurse -Force -ErrorAction SilentlyContinue }
    foreach ($it in $items) {
        Write-Host "`n== Analisando $($it.name)  ->  $($it.key)" -ForegroundColor Cyan
        # Pasta com sufixo único: uma sobra presa nunca atrapalha a análise seguinte
        $work = Join-Path $tmpRoot ("{0}-{1:yyyyMMddHHmmss}" -f $it.key, (Get-Date))

        # Cópia sem bin/obj (os do container têm caminhos Linux e não servem aqui)
        robocopy $it.path $work /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { Write-Warning "$($it.key): falha ao copiar a solução"; continue }

        # Apaga o projeto antigo (se existir) para a análise ser sempre a primeira
        try {
            $existe = (Invoke-RestMethod "$SonarUrl/api/projects/search?projects=$($it.key)" -Headers $headers).components
            if ($existe) {
                Invoke-RestMethod -Method Post "$SonarUrl/api/projects/delete?project=$($it.key)" -Headers $headers | Out-Null
                Write-Host "   projeto antigo apagado" -ForegroundColor DarkGray
            }
        }
        catch { Write-Warning "$($it.key): não consegui apagar o projeto antigo (o token é de admin?): $($_.ErrorDetails.Message) $_"; continue }

        Push-Location $work
        try {
            dotnet sonarscanner begin /k:"$($it.key)" /n:"$($it.name)" /d:sonar.host.url="$SonarUrl" /d:sonar.token="$Token" /d:sonar.cs.roslyn.ignoreIssues=true
            if ($LASTEXITCODE -ne 0) { throw "begin falhou" }

            $sln = Get-ChildItem "$work\*" -File -Include *.sln, *.slnx -Name -ErrorAction SilentlyContinue | Select-Object -First 1
            # -nodeReuse:false: o MSBuild não deixa processos vivos segurando as DLLs do scanner
            if ($sln) { dotnet build $sln --no-incremental -nodeReuse:false } else { dotnet build --no-incremental -nodeReuse:false }
            if ($LASTEXITCODE -ne 0) { throw "build falhou" }

            dotnet sonarscanner end /d:sonar.token="$Token"
            if ($LASTEXITCODE -ne 0) { throw "end falhou" }
        }
        catch { Write-Warning "$($it.key): $_" }
        finally {
            Pop-Location
            dotnet build-server shutdown | Out-Null
            Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ---------- 2) Espera o servidor processar ----------
function Wait-Sonar([string]$key) {
    for ($i = 0; $i -lt 60; $i++) {
        $r = Invoke-RestMethod "$SonarUrl/api/ce/component?component=$key" -Headers $headers
        if (-not $r.queue -or $r.queue.Count -eq 0) { return }
        Start-Sleep -Seconds 5
    }
    Write-Warning "$key ainda processando após 5 min"
}

# ---------- 3) Exporta ----------
# Mantém só as métricas que existem nesta versão do SonarQube
try {
    $available = (Invoke-RestMethod "$SonarUrl/api/metrics/search?ps=500" -Headers $headers).metrics.key
    $missing = $metrics | Where-Object { $_ -notin $available }
    if ($missing) { Write-Warning "Métricas ignoradas (não existem nesta versão): $($missing -join ', ')" }
    $metrics = $metrics | Where-Object { $_ -in $available }
}
catch { Write-Warning "Não consegui listar métricas: $($_.ErrorDetails.Message) $_" }

$rows = foreach ($it in $items) {
    try {
        Wait-Sonar $it.key
        $url = "$SonarUrl/api/measures/component?component=$($it.key)&metricKeys=$($metrics -join ',')"
        $r = Invoke-RestMethod $url -Headers $headers

        $row = [ordered]@{
            modelo = $it.modelo; esforco = $it.esforco; prompt = $it.prompt; execucao = $it.execucao
            solucao = $it.solucao; iteracao_final = $it.iteracao_final; key = $it.key
        }
        foreach ($m in $metrics) {
            $row[$m] = ($r.component.measures | Where-Object metric -eq $m).value
        }
        [pscustomobject]$row
    }
    catch { Write-Warning "Sem dados para $($it.key) : $($_.ErrorDetails.Message) $_" }
}

if (-not $rows) {
    Write-Host "`nNenhum dado exportado. Projetos que existem no servidor:" -ForegroundColor Red
    try {
        (Invoke-RestMethod "$SonarUrl/api/projects/search?ps=500" -Headers $headers).components |
            ForEach-Object { Write-Host "  $($_.key)" }
    }
    catch { Write-Warning "Não consegui listar projetos: $($_.ErrorDetails.Message) $_" }
    exit 1
}

$rows | Export-Csv $OutCsv -NoTypeInformation -Delimiter ';' -Encoding UTF8
Write-Host "`nCSV salvo em $OutCsv ($(@($rows).Count) soluções)" -ForegroundColor Green
