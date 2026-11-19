<#
  experimento.ps1
  Roda o experimento inteiro sem interação:
    para cada modelo x prompt x geração:
      1. Claude (modo automático) com o prompt          -> run.ps1 -Automatico
      2. avaliação pelo Comparador                       -> avaliar.ps1
      3. se NÃO ACEITA: re-prompt com o feedback na mesma sessão e volta ao 2,
         até ser aceita ou chegar a -MaxIteracoes
    As métricas (tokens e SonarQube) não são geradas aqui: rode os scripts de docs\
    depois que todas as execuções terminarem.

  Uso (na pasta docker):
    powershell -ExecutionPolicy Bypass -File .\experimento.ps1

  Pode ser interrompido e rodado de novo: execuções já concluídas são puladas e a que
  estava em andamento é refeita do zero. Andamento em D:\Projects\TCC\experimento.log
  Tentativas que falharam vão para D:\Projects\TCC\_falhas (para diagnóstico).
#>
param(
  [int]$MaxIteracoes    = 20,   # limite de iterações por execução (1 + re-prompts)
  [int]$EsperaLimiteMin = 30,   # espera quando bate o limite de uso do plano
  [int]$MaxTentativas   = 3     # refazer uma execução que falhou por outro motivo
)
$ErrorActionPreference = 'Stop'

# ---- Plano do experimento -------------------------------------------------------
$Modelos = @(
  @{ Modelo = 'opus';   Esforco = 'medium' },
  @{ Modelo = 'sonnet'; Esforco = 'high' },
  @{ Modelo = 'haiku';  Esforco = 'medium' },
  @{ Modelo = 'sonnet'; Esforco = 'medium' }
)
$Prompts  = @('zero_shot.md', 'completo.md')
$Geracoes = 1..3
# ---------------------------------------------------------------------------------

$TccRoot    = 'D:\Projects\TCC'
$EstadoPath = Join-Path $TccRoot 'experimento_estado.json'
$LogPath    = Join-Path $TccRoot 'experimento.log'
$RunPs1     = Join-Path $PSScriptRoot 'run.ps1'
$AvaliarPs1 = Join-Path $PSScriptRoot 'avaliar.ps1'

function Log([string]$msg, [string]$cor = 'Gray') {
  $l = '{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $msg
  Write-Host $l -ForegroundColor $cor
  Add-Content -Path $LogPath -Value $l -Encoding UTF8
}
function Ler-Estado {
  $h = @{}
  if (Test-Path $EstadoPath) {
    (Get-Content $EstadoPath -Raw -Encoding UTF8 | ConvertFrom-Json).PSObject.Properties |
      ForEach-Object { $h[$_.Name] = $_.Value }
  }
  return $h
}
function Salvar-Estado($h) { $h | ConvertTo-Json -Depth 5 | Set-Content $EstadoPath -Encoding UTF8 }
function Pastas($it) {
  foreach ($b in 'runs', 'logs', 'resultados') {
    Join-Path $TccRoot ("$b\$($it.Modelo)\$($it.Esforco)\$($it.PromptNome)\run-{0:D2}" -f $it.Geracao)
  }
}
function Descartar($it) {
  # Não apaga: move para _falhas\<data-hora>\..., para dar para ver depois por que falhou
  $destBase = Join-Path $TccRoot ('_falhas\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
  foreach ($d in (Pastas $it)) {
    if (Test-Path $d) {
      $dest = Join-Path $destBase $d.Substring($TccRoot.Length + 1)
      New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
      Move-Item $d $dest
    }
  }
}
function Motivo($it) {
  # Motivo do erro, lido da última saída do Claude guardada pelo tcc-agente
  $logs = (Pastas $it)[1]
  $j = Get-ChildItem (Join-Path $logs 'saidas') -Filter '*.json' -ErrorAction SilentlyContinue |
    Sort-Object Name | Select-Object -Last 1
  $sessoes = Get-ChildItem (Join-Path $logs 'projects') -Recurse -Filter '*.jsonl' -ErrorAction SilentlyContinue
  $salv = @($sessoes | Select-String -Pattern 'safeguards stopped' -SimpleMatch).Count
  $prefixo = if ($salv -gt 0) { "salvaguardas do modelo interromperam a resposta $salv vez(es); " } else { '' }
  if (-not $j) { return $prefixo + 'sem saída do Claude' }
  foreach ($ext in '.tempo_esgotado', '.erro') {
    $forca = "$($j.FullName)$ext"
    if (Test-Path $forca) { return $prefixo + (Get-Content $forca -Raw -Encoding UTF8).Trim() }
  }
  try {
    $o = Get-Content $j.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $txt = "$($o.subtype) $($o.result) $($o.errors -join ' ')"
    return $prefixo + (($txt -replace '\s+', ' ').Trim() | ForEach-Object { if ($_.Length -gt 300) { $_.Substring(0, 300) + '…' } else { $_ } })
  }
  catch { return "saída ilegível: $($j.Name)" }
}
function Agente($it, [int]$iter) {
  & $RunPs1 -Modelo $it.Modelo -Esforco $it.Esforco -Prompt $it.Prompt -Execucao $it.Geracao -Iteracao $iter -Automatico | Out-Host
  return $LASTEXITCODE
}
function Avaliar($it, [int]$iter) {
  & $AvaliarPs1 -Modelo $it.Modelo -Esforco $it.Esforco -Prompt $it.Prompt -Execucao $it.Geracao -Iteracao $iter | Out-Host
  return $LASTEXITCODE
}
function Agente-Repetindo($it, [int]$iter) {
  # Re-prompt: repete a mesma iteração enquanto o plano estiver no limite de uso
  while ($true) {
    $c = Agente $it $iter
    if ($c -ne 75) { return $c }
    Log "Limite de uso. Esperando $EsperaLimiteMin min para repetir a iteração $iter" 'Yellow'
    Start-Sleep -Seconds ($EsperaLimiteMin * 60)
  }
}
function Tem-Solucao($it) {
  # O modelo criou alguma pasta de projeto em src? O nome é conferido pelo avaliar.ps1;
  # aqui só importa se o modelo terminou sem criar nada (em geral, depois de uma pergunta).
  $src = Join-Path (Pastas $it)[0] 'src'
  return @(Get-ChildItem $src -Directory -ErrorAction SilentlyContinue).Count -ge 1
}
# Resposta padrão quando o modelo termina o turno sem solução (em geral, com uma pergunta).
# Ninguém responde perguntas no experimento; a resposta vai como re-prompt e conta como iteração.
# O mesmo texto é reconhecido pelos scripts de tokens (coluna respostas_padrao).
$RespostaPadrao = 'Não há ninguém disponível para responder perguntas durante esta execução. Tome as decisões necessárias com a interpretação mais razoável e conclua a tarefa descrita no prompt, sem fazer novas perguntas.'
function Escrever-RespostaPadrao($it, [int]$iter) {
  # Fica em resultados\...\sem_solucao\, onde o run.ps1 procura o feedback da iteração anterior
  $dir = Join-Path (Pastas $it)[2] 'sem_solucao'
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  [IO.File]::WriteAllText((Join-Path $dir "sem_solucao_i${iter}_feedback.md"), $RespostaPadrao, (New-Object Text.UTF8Encoding $false))
}

# Monta o plano
$plano = foreach ($m in $Modelos) { foreach ($p in $Prompts) { foreach ($g in $Geracoes) {
  [pscustomobject]@{
    Modelo = $m.Modelo; Esforco = $m.Esforco; Prompt = $p
    PromptNome = [IO.Path]::GetFileNameWithoutExtension($p); Geracao = $g
    Chave = "{0}/{1}/{2}/run-{3:D2}" -f $m.Modelo, $m.Esforco, [IO.Path]::GetFileNameWithoutExtension($p), $g
  }
} } }

$estado = Ler-Estado
$finais = 'aceita', 'limite_iteracoes'

# Pastas de execuções do plano que existem mas não foram feitas por este script
$antigas = @($plano | Where-Object { $estado[$_.Chave].status -notin $finais } | Where-Object {
  @(Pastas $_ | Where-Object { Test-Path $_ }).Count -gt 0
})
if ($antigas.Count -gt 0) {
  Write-Host "`nEstas execuções já têm pastas (de testes manuais ou de uma rodada interrompida)" -ForegroundColor Yellow
  Write-Host "e serão movidas para _falhas e refeitas no modo automático:" -ForegroundColor Yellow
  $antigas | ForEach-Object { Write-Host "  $($_.Chave)" }
  $r = Read-Host "`nContinuar? (s/n)"
  if ($r -notmatch '^[sS]') { Write-Host 'Cancelado. Nada foi alterado.'; exit 1 }
}

Log "==== Início: $(@($plano).Count) execuções, até $MaxIteracoes iterações cada ====" 'Cyan'

foreach ($it in $plano) {
  if ($estado[$it.Chave].status -in $finais) { Log "Pulando $($it.Chave) (já $($estado[$it.Chave].status))"; continue }

  $tentativa = 0
  $concluido = $false
  while (-not $concluido) {
    $tentativa++
    if ($tentativa -gt $MaxTentativas) {
      Log "DESISTI de $($it.Chave) após $MaxTentativas tentativas" 'Red'
      $estado[$it.Chave] = @{ status = 'falhou'; fim = (Get-Date -Format s) }
      Salvar-Estado $estado
      break
    }
    Descartar $it   # sempre começa do zero
    Log "$($it.Chave): iteração 1 (tentativa $tentativa)" 'Cyan'
    try {
      $c = Agente $it 1
      if ($c -eq 75) {
        Log "Limite de uso. Esperando $EsperaLimiteMin min para refazer $($it.Chave)" 'Yellow'
        Start-Sleep -Seconds ($EsperaLimiteMin * 60)
        $tentativa--   # limite de uso não conta como tentativa
        continue
      }
      if ($c -ne 0) { throw "agente terminou com código $c na iteração 1: $(Motivo $it)" }

      $iter = 1
      $padrao = 0   # vezes que o modelo terminou sem solução e recebeu a resposta padrão
      while ($true) {
        if (-not (Tem-Solucao $it)) {
          $padrao++
          if ($iter -ge $MaxIteracoes) {
            Log "$($it.Chave): SEM SOLUÇÃO após $iter iterações (limite)" 'Yellow'
            $estado[$it.Chave] = @{ status = 'limite_iteracoes'; iteracoes = $iter; respostas_padrao = $padrao; fim = (Get-Date -Format s) }
            break
          }
          Escrever-RespostaPadrao $it $iter
          $iter++
          Log "$($it.Chave): terminou sem solução; resposta padrão, iteração $iter" 'Yellow'
          $c = Agente-Repetindo $it $iter
          if ($c -ne 0) { throw "agente terminou com código $c na iteração ${iter}: $(Motivo $it)" }
          continue
        }
        $a = Avaliar $it $iter
        if ($a -eq 0) {
          Log "$($it.Chave): ACEITA na iteração $iter" 'Green'
          $estado[$it.Chave] = @{ status = 'aceita'; iteracoes = $iter; respostas_padrao = $padrao; fim = (Get-Date -Format s) }
          break
        }
        if ($a -ne 1) { throw "avaliador terminou com código $a na iteração $iter" }
        if ($iter -ge $MaxIteracoes) {
          Log "$($it.Chave): NÃO ACEITA após $iter iterações (limite)" 'Yellow'
          $estado[$it.Chave] = @{ status = 'limite_iteracoes'; iteracoes = $iter; respostas_padrao = $padrao; fim = (Get-Date -Format s) }
          break
        }
        $iter++
        Log "$($it.Chave): NÃO ACEITA; re-prompt, iteração $iter"
        $c = Agente-Repetindo $it $iter
        if ($c -ne 0) { throw "agente terminou com código $c na iteração ${iter}: $(Motivo $it)" }
      }
      Salvar-Estado $estado
      $concluido = $true
    }
    catch {
      Log "$($it.Chave): falha ($_). Será refeita do zero." 'Red'
    }
  }
}

Log '==== Execuções terminadas ====' 'Cyan'
$plano | ForEach-Object {
  $e = $estado[$_.Chave]
  [pscustomobject]@{ execucao = $_.Chave; status = $e.status; iteracoes = $e.iteracoes }
} | Format-Table -AutoSize | Out-Host

Log '==== Fim ====' 'Cyan'
