<#
  reavaliar_todos.ps1
  Roda o Comparador (modo comparar) em todas as gerações de runs\, contra o base.db atual
  (já com a correção do duplex_mhz). Não recompila nem reexecuta o código gerado: só compara
  o banco final de cada geração. A iteração usada é a última registrada no resultados.csv
  original de cada execução. Os resultados originais (resultados\) não são alterados.

  Uso: powershell -ExecutionPolicy Bypass -File D:\Projects\TCC\analise\reavaliar_todos.ps1
#>
param(
  [string]$TccRoot    = 'D:\Projects\TCC',
  [string]$Comparador = 'D:\Projects\TCC_GitHub\comparator\src\Comparador\bin\Release\net10.0\Comparador.dll',
  [string]$Gabarito   = 'D:\Projects\TCC_GitHub\db\base.db',
  [string]$Ddl        = 'D:\Projects\TCC_GitHub\db\DDL.sql',
  [string]$Saida      = 'D:\Projects\TCC\resultados_reavaliacao'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$padrao = '^(?<empresa>[^_]+)_(?<modelo>.+)_(?<condicao>[^_]+)_r(?<geracao>\d+)$'
$runsRoot = Join-Path $TccRoot 'runs'

New-Item -ItemType Directory -Force -Path $Saida | Out-Null
$log    = Join-Path $Saida 'reavaliacao.log'
$resumo = Join-Path $Saida 'resumo.csv'
"modelo_dir,esforco,prompt,run,solucao,iteracao,codigo,segundos" | Out-File -Encoding utf8 $resumo
"Início: $(Get-Date -Format s)" | Out-File -Encoding utf8 $log

$bancos = @(Get-ChildItem $runsRoot -Recurse -Filter *.db | Where-Object { $_.Directory.Name -eq 'db' } | Sort-Object FullName)
Write-Host "$($bancos.Count) bancos encontrados" -ForegroundColor Cyan
$n = 0
foreach ($b in $bancos) {
  $n++
  $runDir = $b.Directory.Parent.FullName
  $rel    = $runDir.Substring($runsRoot.Length + 1)          # ex.: haiku\medium\completo\run-01
  $partes = $rel -split '\\'
  $sol    = $b.BaseName
  if ($sol -notmatch $padrao) { Write-Warning "Fora do padrão, pulei: $($b.FullName)"; continue }
  $id = $Matches.Clone()

  $iter = '1'
  $csvOrig = Join-Path $TccRoot "resultados\$rel\resultados.csv"
  if (Test-Path $csvOrig) {
    $linhas = @(Import-Csv $csvOrig)
    if ($linhas.Count -gt 0) { $iter = $linhas[-1].iteracao }
  }

  $out = Join-Path $Saida $rel
  New-Item -ItemType Directory -Force -Path $out | Out-Null
  Write-Host "[$n/$($bancos.Count)] $rel -> $sol (iteração $iter)" -ForegroundColor Cyan
  "==== [$n/$($bancos.Count)] $rel | $sol | i$iter | $(Get-Date -Format s)" | Out-File -Append -Encoding utf8 $log

  $t = [Diagnostics.Stopwatch]::StartNew()
  $ErrorActionPreference = 'Continue'
  $texto = & dotnet $Comparador comparar `
    --empresa $id.empresa --modelo $id.modelo --condicao $id.condicao --geracao $id.geracao --iteracao $iter `
    --gabarito $Gabarito --ddl $Ddl --saida $out --solucao $b.FullName 2>&1 | ForEach-Object { "$_" }
  $codigo = $LASTEXITCODE
  $ErrorActionPreference = 'Stop'
  $t.Stop()

  $texto | Out-File -Append -Encoding utf8 $log
  $texto | ForEach-Object { Write-Host "  $_" }
  "$($partes[0]),$($partes[1]),$($partes[2]),$($partes[3]),$sol,$iter,$codigo,$([int]$t.Elapsed.TotalSeconds)" | Out-File -Append -Encoding utf8 $resumo
}
"Fim: $(Get-Date -Format s)" | Out-File -Append -Encoding utf8 $log
Write-Host "Concluído. Resumo em $resumo" -ForegroundColor Green
