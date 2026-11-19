<#
  Executa o Claude Code isolado para UMA rodada do experimento.

  Cada rodada recebe:
    - uma pasta de trabalho própria (src, db)         -> /workspace
    - os arquivos de importação (somente leitura)     -> /workspace/files
    - SÓ o prompt escolhido (somente leitura)         -> /workspace/prompt/<arquivo>
    - uma pasta de config/sessões do Claude própria   -> /home/node/.claude
    - uma rede Docker própria, apagada no final
  Dentro do container: firewall que só libera a API da Anthropic, sem subagentes,
  sem web, usuário sem privilégios. Nenhuma rodada enxerga ou alcança outra.

  Uso:
    .\run.ps1 -Modelo sonnet -Esforco high   -Prompt zero_shot.md
    .\run.ps1 -Modelo opus   -Esforco medium -Prompt complete.md -Execucao 3

  Re-prompting (mesma sessão do Claude, com o feedback do avaliar.ps1 da iteração anterior):
    .\run.ps1 -Modelo sonnet -Esforco high -Prompt zero_shot.md -Execucao 1 -Iteracao 2

  -Automatico: roda sem interação (usado pelo experimento.ps1). Sai com o código do agente:
    0 = ok, 1 = falha, 75 = limite de uso (tentar depois).
#>
param(
  [Parameter(Mandatory)][string]$Modelo,   # alias ou nome completo do modelo (vira ANTHROPIC_MODEL)
  [Parameter(Mandatory)]
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max')]
  [string]$Esforco,                        # vira CLAUDE_CODE_EFFORT_LEVEL (prioridade sobre /effort)
  [Parameter(Mandatory)][string]$Prompt,   # nome do arquivo dentro da pasta de prompts
  [int]$Execucao = 0,                      # 0 = próxima execução livre
  [int]$Iteracao = 1,                      # >1 = continua a sessão da execução com o feedback
  [switch]$Automatico                      # sem interação: envia a mensagem e sai sozinho
)
$ErrorActionPreference = 'Stop'

# ---- Caminhos (ajuste se precisar) -------------------------------------------
$TccRoot    = 'D:\Projects\TCC'
$BaseDir    = Join-Path $TccRoot 'base'            # base\src\DDL.sql e base\files\...
$PromptsDir = 'D:\Projects\TCC_GitHub\docs\propmts'
$Image      = 'tcc-claude'
$EnvFile    = Join-Path $PSScriptRoot '.env'       # CLAUDE_CODE_OAUTH_TOKEN=...
# Recursos de cada execução (iguais para todos os modelos; registre na metodologia).
# Sem swap: um comando que estoura a memória é encerrado na hora, em vez de travar tudo.
$Cpus        = 6      # CPUs do container
$MemoriaGB   = 12     # RAM do container (GB)
$TempoMaxMin = 180    # tempo máximo de cada iteração no modo automático (minutos)
# ------------------------------------------------------------------------------

# Modelo esperado na saída de cada iteração: o tcc-agente recusa a iteração se outro
# modelo gerar a resposta (por exemplo, um fallback do Claude Code)
$ModelosApi = @{
  'opus'   = 'claude-opus-5-5'
  'sonnet' = 'claude-sonnet-5-5'
  'haiku'  = 'claude-haiku-5-5'
}
$modeloApi = if ($ModelosApi.ContainsKey($Modelo.ToLower())) { $ModelosApi[$Modelo.ToLower()] } else { $Modelo }

$promptPath = Join-Path $PromptsDir $Prompt
$ddlPath    = Join-Path $BaseDir 'src\DDL.sql'
$filesDir   = Join-Path $BaseDir 'files'

foreach ($p in @($promptPath, $ddlPath)) {
  if (-not (Test-Path $p -PathType Leaf)) { throw "Arquivo não encontrado: $p" }
}
foreach ($p in @($filesDir)) {
  if (-not (Test-Path $p -PathType Container)) { throw "Pasta não encontrada: $p" }
}
if (-not (Test-Path $EnvFile -PathType Leaf)) {
  throw "Crie $EnvFile com a linha CLAUDE_CODE_OAUTH_TOKEN=... (veja: claude setup-token)"
}

$promptName = [IO.Path]::GetFileNameWithoutExtension($Prompt)
$modeloDir  = $Modelo -replace '[^a-zA-Z0-9_.-]', '-'
$groupRuns  = Join-Path $TccRoot "runs\$modeloDir\$Esforco\$promptName"
$groupLogs  = Join-Path $TccRoot "logs\$modeloDir\$Esforco\$promptName"

if ($Iteracao -lt 1) { throw "-Iteracao deve ser 1 ou mais." }
if ($Iteracao -gt 1 -and $Execucao -le 0) {
  throw "Para -Iteracao $Iteracao, informe -Execucao (a execução que vai continuar)."
}
if ($Execucao -le 0) {
  $Execucao = 1
  while (Test-Path (Join-Path $groupRuns ('run-{0:D2}' -f $Execucao))) { $Execucao++ }
}
$runName = 'run-{0:D2}' -f $Execucao
$runDir  = Join-Path $groupRuns $runName
$logDir  = Join-Path $groupLogs $runName

$resDir  = Join-Path $TccRoot "resultados\$modeloDir\$Esforco\$promptName\$runName"

if ($Iteracao -eq 1) {
  # Nunca reaproveita uma pasta já usada: cada rodada começa limpa.
  if (Test-Path $runDir) { throw "A execução já existe: $runDir" }
}
else {
  if (-not (Test-Path $runDir) -or -not (Test-Path $logDir)) { throw "Execução não encontrada: $runDir" }
  $anterior = $Iteracao - 1
  $feedback = @(Get-ChildItem $resDir -Recurse -Filter "*_i${anterior}_feedback.md" -ErrorAction SilentlyContinue)
  if ($feedback.Count -ne 1) {
    throw "Não achei o feedback da iteração $anterior em $resDir. Rode o avaliar.ps1 com -Iteracao $anterior (se ela foi ACEITA, não há feedback)."
  }
  $feedbackPath = $feedback[0].FullName
}

$containerName = ("tcc-$modeloDir-$Esforco-$promptName-$runName" -replace '[^a-zA-Z0-9_.-]', '-').ToLower()
$netName       = "$containerName-net"

# A partir daqui os erros do docker são tratados pelo $LASTEXITCODE
$ErrorActionPreference = 'Continue'

# Restos de uma execução interrompida (ex.: janela fechada sem /exit).
# Como a pasta desta execução não existe, qualquer container/rede com este nome é órfão.
if ((docker ps -a --format '{{.Names}}') -contains $containerName) {
  Write-Host "Removendo container órfão $containerName" -ForegroundColor Yellow
  docker rm -f $containerName | Out-Null
}
if ((docker network ls --format '{{.Name}}') -contains $netName) {
  Write-Host "Removendo rede órfã $netName" -ForegroundColor Yellow
  docker network rm $netName | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "Não consegui remover a rede órfã $netName" }
}

# Rede exclusiva desta execução (containers em redes diferentes não se enxergam)
docker network create $netName | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Não consegui criar a rede $netName" }

try {
  if ($Iteracao -eq 1) {
    New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'src'), (Join-Path $runDir 'db'), $logDir | Out-Null
    Copy-Item $ddlPath (Join-Path $runDir 'src\DDL.sql')

    # Cópia do prompt dentro da pasta da execução (registra qual prompt foi usado).
    # No container ela fica somente leitura, então o Claude lê sem pedir permissão e não altera.
    New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'prompt') | Out-Null
    $runPrompt = Join-Path $runDir "prompt\$Prompt"
    Copy-Item $promptPath $runPrompt

    # A geração do prompt ({{geracao}}) acompanha o número da execução: run-02 -> r2.
    # Só a cópia da execução muda; o arquivo original em propmts fica intacto.
    $texto = [IO.File]::ReadAllText($runPrompt)
    $padraoGeracao = '(`\{\{geracao\}\}`:\s*`)r\d+(`)'
    if ($texto -match $padraoGeracao) {
      $texto = [regex]::Replace($texto, $padraoGeracao, ('${1}r' + $Execucao + '${2}'))
      [IO.File]::WriteAllText($runPrompt, $texto, (New-Object Text.UTF8Encoding($false)))
      Write-Host "Geração no prompt: r$Execucao"
    }
    else {
      Write-Warning "Não achei a linha {{geracao}} no prompt; a geração ficou como está no original."
    }

    # Pré-configura o Claude para pular as telas de primeiro uso (tema e confiar na pasta).
    # Grava em UTF-8 sem BOM (o Claude não lê JSON com BOM).
    $claudeConfig = @'
  {
    "theme": "dark",
    "hasCompletedOnboarding": true,
    "projects": {
      "/workspace": {
        "hasTrustDialogAccepted": true
      }
    }
  }
'@
    [IO.File]::WriteAllText((Join-Path $logDir '.claude.json'), $claudeConfig, (New-Object Text.UTF8Encoding($false)))
  }
  else {
    # Iteração seguinte: reaproveita a pasta, o prompt e a sessão desta execução
    $runPrompt = Join-Path $runDir "prompt\$Prompt"
    if (-not (Test-Path $runPrompt -PathType Leaf)) { throw "Prompt da execução não encontrado: $runPrompt" }
  }

  $dockerArgs = @('run', '--rm')
  if (-not $Automatico) { $dockerArgs += '-it' }
  $dockerArgs += @(
    '--name', $containerName,
    '--hostname', 'tcc',
    '--network', $netName,
    # Privilégios mínimos: só o necessário para montar o firewall e trocar de usuário
    '--cap-drop', 'ALL',
    '--cap-add', 'NET_ADMIN', '--cap-add', 'NET_RAW',
    '--cap-add', 'SETUID', '--cap-add', 'SETGID',
    '--security-opt', 'no-new-privileges',
    '--pids-limit', '1024',
    # Recursos fixos por execução
    '--cpus', "$Cpus",
    '--memory', "${MemoriaGB}g", '--memory-swap', "${MemoriaGB}g",
    '-e', "TCC_TEMPO_MAX_MIN=$TempoMaxMin",
    # A pasta da execução é copiada para dentro do container e devolvida no final
    '--mount', "type=bind,source=$runDir,target=/out/workspace",
    '--mount', "type=bind,source=$filesDir,target=/workspace/files,readonly",
    '--mount', "type=bind,source=$runPrompt,target=/workspace/prompt/$Prompt,readonly",
    '--mount', "type=bind,source=$logDir,target=/home/node/.claude",
    '--env-file', $EnvFile,
    '-e', "ANTHROPIC_MODEL=$Modelo",
    '-e', "CLAUDE_CODE_EFFORT_LEVEL=$Esforco",
    '-e', "TCC_MODELO_ESPERADO=$modeloApi",
    # Consumo de CPU, memória e disco desta iteração (gravado pelo sync-run.sh)
    '-e', ('TCC_RECURSOS=/home/node/.claude/recursos/iter{0:D2}_agente.txt' -f $Iteracao)
  )
  if ($Iteracao -gt 1) {
    # Feedback da iteração anterior (somente leitura, fora do /workspace)
    $dockerArgs += @('--mount', "type=bind,source=$feedbackPath,target=/reprompt/feedback.md,readonly")
  }
  $dockerArgs += $Image

  if ($Automatico) {
    if ($Iteracao -eq 1) { $dockerArgs += @('tcc-agente', 'inicial', "Siga as instruções em prompt/$Prompt") }
    else { $dockerArgs += @('tcc-agente', 'reprompt', '/reprompt/feedback.md') }
  }
  elseif ($Iteracao -gt 1) {
    # Retoma a sessão anterior e envia o feedback como mensagem
    $dockerArgs += @('specstory', 'run', 'claude', '--no-cloud-sync', '-c', 'claude-reprompt /reprompt/feedback.md')
  }

  Write-Host "Execução: $Modelo / esforço $Esforco / $promptName / $runName / iteração $Iteracao"
  Write-Host "Recursos:        $Cpus CPUs, $MemoriaGB GB de RAM, até $TempoMaxMin min por iteração (automático)"
  Write-Host "Código e bancos: $runDir"
  Write-Host "Sessões/logs:    $logDir"
  if ($Automatico) {
    Write-Host "Modo automático: o Claude trabalha sozinho e o container fecha ao terminar." -ForegroundColor Cyan
  }
  else {
    Write-Host ""
    if ($Iteracao -eq 1) {
      Write-Host "Mensagem para o Claude:  Siga as instruções em prompt/$Prompt" -ForegroundColor Cyan
    }
    else {
      Write-Host "Iteração $($Iteracao): retomando a sessão e enviando o feedback automaticamente" -ForegroundColor Cyan
      Write-Host "Feedback: $feedbackPath"
    }
    Write-Host "Ao terminar, saia com /exit: o resultado só é copiado para $runDir no final." -ForegroundColor Yellow
    Write-Host "Fechar a janela no meio PERDE a execução (os logs de tokens ficam salvos)." -ForegroundColor Yellow
  }

  & docker @dockerArgs
  $codigo = $LASTEXITCODE
}
finally {
  # Garante que nada desta execução fica para trás
  if ((docker ps -a --format '{{.Names}}') -contains $containerName) { docker rm -f $containerName | Out-Null }
  docker network rm $netName | Out-Null
}
exit $codigo
