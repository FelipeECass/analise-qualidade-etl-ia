<#
  avaliar.ps1
  Roda o Comparador sobre UMA execução, num container separado do agente.
  Cópia do docker\avaliar.ps1 do Claude; muda só a imagem (tcc-openai).

  Por que num container: o código gerado foi feito para o layout do container
  (/workspace, /workspace/files). Rodar no Windows quebraria os caminhos, e o
  código gerado nunca executa direto na sua máquina. O gabarito só é montado
  aqui, nunca no container do agente.

  Uso:
    .\avaliar.ps1 -Modelo terra -Esforco medium -Prompt zero_shot.md -Execucao 1
    .\avaliar.ps1 -Modelo terra -Esforco medium -Prompt zero_shot.md -Execucao 1 -Iteracao 2
    .\avaliar.ps1 ... -Modo comparar      (só compara o banco, sem recompilar/reexecutar)
#>
param(
  [Parameter(Mandatory)][string]$Modelo,
  [Parameter(Mandatory)][ValidateSet('low', 'medium', 'high', 'xhigh', 'max', 'ultra')][string]$Esforco,
  [Parameter(Mandatory)][string]$Prompt,
  [Parameter(Mandatory)][int]$Execucao,
  [int]$Iteracao = 1,
  [ValidateSet('ciclo', 'comparar')][string]$Modo = 'ciclo',
  [string]$Solucao                          # opcional: nome da pasta da solução em src
)
$ErrorActionPreference = 'Stop'

# ---- Caminhos (ajuste se precisar) -------------------------------------------
$TccRoot    = 'D:\Projects\TCC'
$FilesDir   = Join-Path $TccRoot 'base\files'
$GabDir     = 'D:\Projects\TCC_GitHub\db'           # base.db (gabarito) e DDL.sql
$Comparador = 'D:\Projects\TCC_GitHub\scripts\comparator\src\Comparador\bin\Release\net10.0'
$Image      = 'tcc-openai'
$Cpus       = 6      # CPUs do container (igual ao run.ps1)
$MemoriaGB  = 12     # RAM do container (GB, sem swap; igual ao run.ps1)
# ------------------------------------------------------------------------------

$promptName = [IO.Path]::GetFileNameWithoutExtension($Prompt)
$modeloDir  = $Modelo -replace '[^a-zA-Z0-9_.-]', '-'
$runName    = 'run-{0:D2}' -f $Execucao
$runDir     = Join-Path $TccRoot "runs\$modeloDir\$Esforco\$promptName\$runName"
$outDir     = Join-Path $TccRoot "resultados\$modeloDir\$Esforco\$promptName\$runName"

if (-not (Test-Path $runDir -PathType Container)) { throw "Execução não encontrada: $runDir" }
foreach ($p in @((Join-Path $GabDir 'base.db'), (Join-Path $GabDir 'DDL.sql'), (Join-Path $Comparador 'Comparador.dll'))) {
  if (-not (Test-Path $p -PathType Leaf)) { throw "Arquivo não encontrado: $p" }
}

# Descobre a solução: <empresa>_<modelo>_<condicao>_r<geracao>
# O modelo pode ter "_" no nome (ex.: chatgpt_gpt_6_zeroshot_r2 -> modelo gpt_6): o Comparador
# monta o nome de volta como <empresa>_<modelo>_<condicao>_r<geracao>, então dá o mesmo nome.
$padrao = '^(?<empresa>[^_]+)_(?<modelo>.+)_(?<condicao>[^_]+)_r(?<geracao>\d+)$'
if (-not $Solucao) {
  $candidatas = @(Get-ChildItem (Join-Path $runDir 'src') -Directory | Where-Object { $_.Name -match $padrao })
  if ($candidatas.Count -ne 1) {
    throw "Esperava 1 pasta de solução em $runDir\src e achei $($candidatas.Count). Informe -Solucao <nome>."
  }
  $Solucao = $candidatas[0].Name
}
if ($Solucao -notmatch $padrao) { throw "Nome fora do padrão <empresa>_<modelo>_<condicao>_r<N>: $Solucao" }
$id = $Matches

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$containerName = ("tcc-aval-$modeloDir-$Esforco-$promptName-$runName" -replace '[^a-zA-Z0-9_.-]', '-').ToLower()
$netName       = "$containerName-net"

$ErrorActionPreference = 'Continue'
if ((docker ps -a --format '{{.Names}}') -contains $containerName) { docker rm -f $containerName | Out-Null }
if ((docker network ls --format '{{.Name}}') -contains $netName) { docker network rm $netName | Out-Null }
docker network create $netName | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Não consegui criar a rede $netName" }

$dockerArgs = @(
  'run', '--rm', '-t',
  '--name', $containerName,
  '--hostname', 'tcc-aval',
  '--network', $netName,
  '--cap-drop', 'ALL',
  '--cap-add', 'NET_ADMIN', '--cap-add', 'NET_RAW',
  '--cap-add', 'SETUID', '--cap-add', 'SETGID',
  '--security-opt', 'no-new-privileges',
  '--pids-limit', '1024',
  '--cpus', "$Cpus",
  '--memory', "${MemoriaGB}g", '--memory-swap', "${MemoriaGB}g",
  # Execução e gabarito são copiados para dentro do container (a pasta da execução não é alterada)
  '--mount', "type=bind,source=$runDir,target=/in/workspace,readonly",
  '--mount', "type=bind,source=$FilesDir,target=/workspace/files,readonly",
  '--mount', "type=bind,source=$GabDir,target=/in/gabarito,readonly",
  '--mount', "type=bind,source=$Comparador,target=/opt/comparador,readonly",
  '--mount', "type=bind,source=$outDir,target=/out/resultados",
  '-e', 'TCC_ROOT=/workspace',
  # Consumo de CPU, memória e disco da avaliação (build + ETL + comparação), gravado pelo sync-run.sh
  '-e', ('TCC_RECURSOS=/resultados/recursos/iter{0:D2}_avaliacao.txt' -f $Iteracao),
  # No ciclo o banco e a compilação são refeitos: copia a pasta db vazia, sem o banco antigo
  '-e', $(if ($Modo -eq 'ciclo') { 'SYNC_EXCLUDE_IN=./db/* ./src/*/bin ./src/*/obj' } else { 'SYNC_EXCLUDE_IN=./src/*/bin ./src/*/obj' }),
  $Image,
  'comparador', $Modo,
  '--empresa', $id.empresa, '--modelo', $id.modelo, '--condicao', $id.condicao,
  '--geracao', $id.geracao, '--iteracao', "$Iteracao",
  '--workspace', '/workspace',
  '--gabarito', '/gabarito/base.db',
  '--ddl', '/gabarito/DDL.sql',
  '--saida', '/resultados'
)

Write-Host "Avaliando $Solucao (iteração $Iteracao, modo $Modo)" -ForegroundColor Cyan
Write-Host "Resultados em: $outDir"
try {
  & docker @dockerArgs
  $codigo = $LASTEXITCODE
}
finally {
  if ((docker ps -a --format '{{.Names}}') -contains $containerName) { docker rm -f $containerName | Out-Null }
  docker network rm $netName | Out-Null
}
switch ($codigo) {
  0 { Write-Host 'ACEITA' -ForegroundColor Green }
  1 { Write-Host "NÃO ACEITA (veja o *_feedback.md em $outDir)" -ForegroundColor Yellow }
  default { Write-Host "Comparador terminou com código $codigo" -ForegroundColor Red }
}
exit $codigo
