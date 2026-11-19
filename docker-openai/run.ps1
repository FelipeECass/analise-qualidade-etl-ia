<#
  Executa o Codex (OpenAI) isolado para UMA rodada do experimento.
  Cópia do docker\run.ps1 do Claude, com a mesma estrutura de pastas e o mesmo isolamento.

  Cada rodada recebe:
    - uma pasta de trabalho própria (src, db)         -> /workspace
    - os arquivos de importação (somente leitura)     -> /workspace/files
    - SÓ o prompt escolhido (somente leitura)         -> /workspace/prompt/<arquivo>
    - uma pasta de config/sessões do Codex própria    -> /codex (CODEX_HOME)
    - uma rede Docker própria, apagada no final
  Dentro do container: firewall que só libera o ChatGPT (API do Codex e login), sem
  subagentes, sem web, usuário sem privilégios. Nenhuma rodada enxerga ou alcança outra.

  Modelos (atalhos): sol = gpt-6.1-sol | terra = gpt-6.1-terra | luna = gpt-6-luna
  (também aceita o nome completo do modelo)

  Uso:
    .\run.ps1 -Modelo terra -Esforco medium -Prompt zero_shot.md
    .\run.ps1 -Modelo sol   -Esforco medium -Prompt completo.md -Execucao 3

  Re-prompting (mesma sessão do Codex, com o feedback do avaliar.ps1 da iteração anterior):
    .\run.ps1 -Modelo terra -Esforco medium -Prompt zero_shot.md -Execucao 1 -Iteracao 2

  -Automatico: roda sem interação (usado pelo experimento.ps1). Sai com o código do agente:
    0 = ok, 1 = falha, 75 = limite de uso (tentar depois).

  Login: auth\auth.json (crie com o login.ps1). É copiado para cada execução e, se o Codex
  renovar o token durante a execução, a versão nova volta para auth\auth.json.
#>
param(
  [Parameter(Mandatory)][string]$Modelo,   # atalho (sol, terra, luna) ou nome completo do modelo
  [Parameter(Mandatory)]
  [ValidateSet('low', 'medium', 'high', 'xhigh', 'max', 'ultra')]
  [string]$Esforco,                        # vira model_reasoning_effort (sempre explícito)
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
$Image      = 'tcc-openai'
$AuthFile   = Join-Path $PSScriptRoot 'auth\auth.json'   # login do ChatGPT (login.ps1)
# Recursos de cada execução (iguais para todos os modelos; registre na metodologia).
# Sem swap: um comando que estoura a memória é encerrado na hora, em vez de travar tudo.
$Cpus        = 6      # CPUs do container
$MemoriaGB   = 12     # RAM do container (GB)
$TempoMaxMin = 180    # tempo máximo de cada iteração no modo automático (minutos)
# ------------------------------------------------------------------------------

# Atalhos dos modelos da OpenAI
$ModelosApi = @{
  'sol'   = 'gpt-6.1-sol'
  'terra' = 'gpt-6.1-terra'
  'luna'  = 'gpt-6-luna'
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
if (-not (Test-Path $AuthFile -PathType Leaf)) {
  throw "Login não encontrado: $AuthFile. Rode antes: powershell -ExecutionPolicy Bypass -File .\login.ps1"
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

# Restos de uma execução interrompida (ex.: janela fechada sem sair do Codex).
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

$authRun = Join-Path $logDir 'auth.json'
$utf8 = New-Object Text.UTF8Encoding($false)
try {
  if ($Iteracao -eq 1) {
    New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'src'), (Join-Path $runDir 'db'), $logDir | Out-Null
    Copy-Item $ddlPath (Join-Path $runDir 'src\DDL.sql')

    # Cópia do prompt dentro da pasta da execução (registra qual prompt foi usado).
    # No container ela fica somente leitura, então o Codex lê e não altera.
    New-Item -ItemType Directory -Force -Path (Join-Path $runDir 'prompt') | Out-Null
    $runPrompt = Join-Path $runDir "prompt\$Prompt"
    Copy-Item $promptPath $runPrompt

    # A geração do prompt ({{geracao}}) acompanha o número da execução: run-02 -> r2.
    # Só a cópia da execução muda; o arquivo original em propmts fica intacto.
    $texto = [IO.File]::ReadAllText($runPrompt)
    $padraoGeracao = '(`\{\{geracao\}\}`:\s*`)r\d+(`)'
    if ($texto -match $padraoGeracao) {
      $texto = [regex]::Replace($texto, $padraoGeracao, ('${1}r' + $Execucao + '${2}'))
      [IO.File]::WriteAllText($runPrompt, $texto, $utf8)
      Write-Host "Geração no prompt: r$Execucao"
    }
    else {
      Write-Warning "Não achei a linha {{geracao}} no prompt; a geração ficou como está no original."
    }

    # Configuração do usuário do Codex: modelo, esforço e pasta confiável (pula a tela
    # de confiança no modo interativo). Em UTF-8 sem BOM. Telemetria, web, subagentes e
    # aprovações ficam na configuração gerenciada da imagem, que tem prioridade.
    $codexConfig = @"
model = "$modeloApi"
model_reasoning_effort = "$Esforco"

[projects."/workspace"]
trust_level = "trusted"
"@
    [IO.File]::WriteAllText((Join-Path $logDir 'config.toml'), $codexConfig, $utf8)
  }
  else {
    # Iteração seguinte: reaproveita a pasta, o prompt e a sessão desta execução
    $runPrompt = Join-Path $runDir "prompt\$Prompt"
    if (-not (Test-Path $runPrompt -PathType Leaf)) { throw "Prompt da execução não encontrado: $runPrompt" }
  }

  # Login do ChatGPT só durante a execução (é apagado da pasta de logs no final)
  Copy-Item $AuthFile $authRun -Force

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
    # A pasta da execução e a do Codex são copiadas para dentro do container e devolvidas no final
    '--mount', "type=bind,source=$runDir,target=/out/workspace",
    '--mount', "type=bind,source=$filesDir,target=/workspace/files,readonly",
    '--mount', "type=bind,source=$runPrompt,target=/workspace/prompt/$Prompt,readonly",
    '--mount', "type=bind,source=$logDir,target=/out/codex",
    '-e', "TCC_MODELO=$modeloApi",
    '-e', "TCC_ESFORCO=$Esforco",
    # Consumo de CPU, memória e disco desta iteração (gravado pelo sync-run.sh)
    '-e', ('TCC_RECURSOS=/codex/recursos/iter{0:D2}_agente.txt' -f $Iteracao)
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
    $dockerArgs += @('specstory', 'run', 'codex', '--no-cloud-sync', '-c', 'codex-reprompt /reprompt/feedback.md')
  }

  Write-Host "Execução: $Modelo ($modeloApi) / esforço $Esforco / $promptName / $runName / iteração $Iteracao"
  Write-Host "Recursos:        $Cpus CPUs, $MemoriaGB GB de RAM, até $TempoMaxMin min por iteração (automático)"
  Write-Host "Código e bancos: $runDir"
  Write-Host "Sessões/logs:    $logDir"
  if ($Automatico) {
    Write-Host "Modo automático: o Codex trabalha sozinho e o container fecha ao terminar." -ForegroundColor Cyan
  }
  else {
    Write-Host ""
    if ($Iteracao -eq 1) {
      Write-Host "Mensagem para o Codex:  Siga as instruções em prompt/$Prompt" -ForegroundColor Cyan
    }
    else {
      Write-Host "Iteração $($Iteracao): retomando a sessão e enviando o feedback automaticamente" -ForegroundColor Cyan
      Write-Host "Feedback: $feedbackPath"
    }
    Write-Host "Ao terminar, saia com /quit: o resultado só é copiado para $runDir no final." -ForegroundColor Yellow
    Write-Host "Fechar a janela no meio PERDE a execução e a sessão do Codex." -ForegroundColor Yellow
  }

  & docker @dockerArgs
  $codigo = $LASTEXITCODE
}
finally {
  # Garante que nada desta execução fica para trás
  if ((docker ps -a --format '{{.Names}}') -contains $containerName) { docker rm -f $containerName | Out-Null }
  docker network rm $netName | Out-Null

  # Se o Codex renovou o login durante a execução, guarda a versão nova
  # (o token antigo deixa de valer). Depois tira o login da pasta de logs.
  if (Test-Path $authRun) {
    try {
      $novo = Get-Content $authRun -Raw -Encoding UTF8
      $j = $novo | ConvertFrom-Json
      $valido = $j.tokens -and $j.tokens.refresh_token
      if ($valido -and $novo -ne (Get-Content $AuthFile -Raw -Encoding UTF8)) {
        Copy-Item $AuthFile "$AuthFile.anterior" -Force
        Copy-Item $authRun $AuthFile -Force
        Write-Host "Login do ChatGPT renovado pelo Codex: auth\auth.json atualizado." -ForegroundColor DarkGray
      }
    }
    catch { Write-Warning "Não consegui ler o auth.json da execução: $_" }
    Remove-Item $authRun -Force -ErrorAction SilentlyContinue
  }
}
exit $codigo
