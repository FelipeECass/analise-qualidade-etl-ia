<#
  tokens-tcc.ps1
  Lê as sessões brutas do Claude (JSONL) de cada execução em D:\Projects\TCC_GitHub\TCC\logs e gera:
    - tokens_metricas.csv : uma linha por execução (tokens, chamadas, tempo e interações)
    - interacoes.csv      : uma linha por interação humana fora do protocolo
                            (pergunta do modelo + sua resposta, mensagem digitada, aprovação de permissão, interrupção)

  Uso:
    powershell -ExecutionPolicy Bypass -File .\tokens-tcc.ps1

  Estrutura lida: logs\<modelo>\<esforço>\<prompt>\run-NN\projects\*\*.jsonl
  Protocolo (não conta como interação): a 1ª mensagem (prompt inicial) e os re-prompts
  (mensagens que começam com o texto padrão do feedback do Comparador).
#>
param(
  [string]$LogsRoot      = 'D:\Projects\TCC_GitHub\TCC\logs',
  [string]$OutCsv        = 'D:\Projects\TCC_GitHub\TCC\tokens_metricas.csv',
  [string]$InteracoesCsv = 'D:\Projects\TCC_GitHub\TCC\interacoes.csv',
  [string]$ReprompPrefixo = 'A solução não atingiu o critério de aceitação',
  [string]$PadraoPrefixo  = 'Não há ninguém disponível para responder perguntas'
)
$ErrorActionPreference = 'Stop'

function ConvertTo-Utc($v) {
  if ($null -eq $v) { return $null }
  if ($v -is [datetime]) { return $v.ToUniversalTime() }
  return [datetime]::Parse([string]$v, [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}
function N($v) { if ($null -eq $v) { 0 } else { [long]$v } }
function Local($dt) { if ($dt) { $dt.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss') } else { '' } }
function Curto([string]$s, [int]$max = 1000) {
  if (-not $s) { return '' }
  $s = ($s -replace '\s+', ' ').Trim()
  if ($s.Length -gt $max) { $s.Substring(0, $max) + '…' } else { $s }
}

$root = (Resolve-Path $LogsRoot).Path.TrimEnd('\')
$runs = Get-ChildItem $root -Directory -Recurse -Filter 'run-*' | Where-Object {
  ($_.FullName.Substring($root.Length + 1) -split '\\').Count -eq 4
}
if (-not $runs) { throw "Nenhuma execução encontrada em $root" }

$detalhes = New-Object System.Collections.Generic.List[object]

$rows = foreach ($run in ($runs | Sort-Object FullName)) {
  $parts = $run.FullName.Substring($root.Length + 1) -split '\\'
  $files = @(Get-ChildItem (Join-Path $run.FullName 'projects') -Recurse -Filter '*.jsonl' -ErrorAction SilentlyContinue)
  if (-not $files) { Write-Warning "Sem sessão em $($run.FullName)"; continue }

  $best     = @{}   # uma entrada por chamada à API (o JSONL repete a mesma chamada em várias linhas)
  $asks     = @{}   # perguntas do modelo (AskUserQuestion), por id da ferramenta
  $answers  = @{}   # respostas a essas perguntas, por id da ferramenta
  $human    = @{}   # mensagens digitadas por você, por uuid
  $perms    = @{}   # aprovações de permissão feitas por você, por uuid
  $models   = New-Object System.Collections.Generic.HashSet[string]
  $efforts  = New-Object System.Collections.Generic.HashSet[string]
  $versions = New-Object System.Collections.Generic.HashSet[string]
  $first = $null; $last = $null; $activeMs = 0; $turns = 0; $salvaguardas = 0

  foreach ($f in $files) {
    foreach ($line in [IO.File]::ReadLines($f.FullName)) {
      if ($line -notmatch '"type":"(assistant|user|system)"') { continue }
      try { $d = $line | ConvertFrom-Json } catch { continue }
      if ($d.type -notin 'assistant', 'user', 'system') { continue }

      $ts = ConvertTo-Utc $d.timestamp
      if ($ts) {
        if (-not $first -or $ts -lt $first) { $first = $ts }
        if (-not $last  -or $ts -gt $last)  { $last  = $ts }
      }
      if ($d.version) { [void]$versions.Add([string]$d.version) }

      if ($d.type -eq 'assistant') {
        if ($d.message.usage) {
          $id = [string]$d.message.id
          $u  = $d.message.usage
          if (-not $best.ContainsKey($id) -or (N $u.output_tokens) -ge (N $best[$id].output_tokens)) { $best[$id] = $u }
          if ($d.message.model) { [void]$models.Add([string]$d.message.model) }
          if ($d.effort) { [void]$efforts.Add([string]$d.effort) }
        }
        foreach ($c in @($d.message.content)) {
          if ($c -and $c.type -eq 'tool_use' -and $c.name -eq 'AskUserQuestion') {
            $asks[[string]$c.id] = [pscustomobject]@{ ts = $ts; questions = @($c.input.questions) }
          }
        }
      }
      elseif ($d.type -eq 'user') {
        $content = $d.message.content
        $toolIds = @(@($content) | Where-Object { $_ -and $_ -isnot [string] -and $_.type -eq 'tool_result' } |
          ForEach-Object { [string]$_.tool_use_id })

        # Resposta a uma pergunta do modelo
        if ($d.toolUseResult -and $d.toolUseResult.answers -and $toolIds.Count -gt 0) {
          $answers[$toolIds[0]] = $d.toolUseResult.answers
        }
        # Aprovação de permissão feita por você (não pela configuração)
        if ($d.permissionDecision -and ([string]$d.permissionDecision.source) -like 'user*') {
          $perms[[string]$d.uuid] = [pscustomobject]@{
            ts = $ts; toolId = ($toolIds | Select-Object -First 1)
            decisao = [string]$d.permissionDecision.decision; motivo = [string]$d.permissionDecision.reasonCode
          }
        }
        # Mensagem enviada ao Claude (digitada por você ou pelo modo automático).
        # Ficam de fora: avisos internos (isMeta), notificações de tarefas e tags do sistema.
        $sistema = ($d.origin -and $d.origin.kind -eq 'task-notification') -or ($d.promptSource -eq 'system')
        if (-not $d.isMeta -and -not $sistema) {
          $texto = if ($content -is [string]) { $content } else {
            (@($content) | Where-Object { $_ -and $_.type -eq 'text' } | ForEach-Object { $_.text }) -join "`n"
          }
          if ($texto -and -not $texto.TrimStart().StartsWith('<')) {
            $human[[string]$d.uuid] = [pscustomobject]@{ ts = $ts; texto = $texto }
          }
        }
      }
      elseif ($d.type -eq 'system' -and ([string]$d.content) -match 'safeguards stopped') {
        $salvaguardas++
      }
      elseif ($d.type -eq 'system' -and $d.subtype -eq 'turn_duration') {
        $activeMs += N $d.durationMs
        $turns++
      }
    }
  }

  # Classifica as mensagens: 1ª = prompt inicial; padrão do feedback = re-prompt;
  # "[Request interrupted..." = você interrompeu; resto = mensagem manual
  $msgs = @($human.Values | Sort-Object ts)
  $reprompts = 0; $padrao = 0; $manuais = @(); $interrup = @()
  for ($i = 0; $i -lt $msgs.Count; $i++) {
    $t = $msgs[$i].texto.TrimStart()
    if ($t.StartsWith('[Request interrupted')) { $interrup += $msgs[$i]; continue }
    if ($i -eq 0) { continue }
    if ($t.StartsWith($ReprompPrefixo)) { $reprompts++ }
    elseif ($t.StartsWith($PadraoPrefixo)) { $reprompts++; $padrao++ }
    else { $manuais += $msgs[$i] }
  }

  # Modo automático (claude -p): duração e turnos vêm das saídas JSON guardadas pelo tcc-agente
  $saidas = @(Get-ChildItem (Join-Path $run.FullName 'saidas') -Filter '*.json' -ErrorAction SilentlyContinue)
  $modoAuto = $saidas.Count -gt 0
  if ($modoAuto -and $turns -eq 0) {
    foreach ($sj in $saidas) {
      try { $o = Get-Content $sj.FullName -Raw -Encoding UTF8 | ConvertFrom-Json; $activeMs += N $o.duration_ms; $turns++ } catch { }
    }
  }
  # Aprovações que não são a resposta de uma pergunta do modelo
  $aprov = @($perms.Values | Where-Object { -not ($_.toolId -and $asks.ContainsKey($_.toolId)) })
  $nPerguntas = ($asks.Values | ForEach-Object { $_.questions.Count } | Measure-Object -Sum).Sum

  $novo = { param($horario, $tipo, $texto, $resposta)
    [pscustomobject][ordered]@{ modelo = $parts[0]; esforco = $parts[1]; prompt = $parts[2]; execucao = $parts[3]
      horario = $horario; tipo = $tipo; texto = $texto; resposta = $resposta } }
  foreach ($k in $asks.Keys) {
    $a = $asks[$k]
    foreach ($q in $a.questions) {
      $resp = if ($answers.ContainsKey($k)) { ($answers[$k].PSObject.Properties | Where-Object Name -eq $q.question).Value } else { $null }
      $detalhes.Add((& $novo (Local $a.ts) 'pergunta_do_modelo' (Curto $q.question) (Curto ([string]$resp))))
    }
  }
  foreach ($m in $interrup) {
    $detalhes.Add((& $novo (Local $m.ts) 'interrupcao' 'você interrompeu o Claude' ''))
  }
  foreach ($m in $manuais) {
    $detalhes.Add((& $novo (Local $m.ts) 'mensagem_manual' (Curto $m.texto) ''))
  }
  foreach ($p in $aprov) {
    $detalhes.Add((& $novo (Local $p.ts) 'aprovacao_manual' "permissão ($($p.motivo))" $p.decisao))
  }

  $u = @($best.Values)
  $inTok   = ($u | ForEach-Object { N $_.input_tokens } | Measure-Object -Sum).Sum
  $cacheW  = ($u | ForEach-Object { N $_.cache_creation_input_tokens } | Measure-Object -Sum).Sum
  $cacheR  = ($u | ForEach-Object { N $_.cache_read_input_tokens } | Measure-Object -Sum).Sum
  $output  = ($u | ForEach-Object { N $_.output_tokens } | Measure-Object -Sum).Sum
  $think   = ($u | ForEach-Object { N $_.output_tokens_details.thinking_tokens } | Measure-Object -Sum).Sum
  $interagiu = ([long]$nPerguntas + $manuais.Count + $aprov.Count + $interrup.Count) -gt 0

  [pscustomobject][ordered]@{
    modelo             = $parts[0]
    esforco            = $parts[1]
    prompt             = $parts[2]
    execucao           = $parts[3]
    modelo_api         = ($models -join ',')
    esforco_aplicado   = ($efforts -join ',')
    versao_claude_code = ($versions -join ',')
    modo               = if ($modoAuto) { 'automatico' } else { 'interativo' }
    iteracoes          = 1 + $reprompts
    interacao_humana   = if ($interagiu) { 'sim' } else { 'nao' }
    perguntas_modelo   = [long]$nPerguntas
    respostas_padrao   = $padrao
    mensagens_manuais  = $manuais.Count
    aprovacoes_manuais = $aprov.Count
    interrupcoes       = $interrup.Count
    salvaguardas       = $salvaguardas
    chamadas_api       = $u.Count
    turnos             = $turns
    input_tokens       = [long]$inTok
    cache_write_tokens = [long]$cacheW
    cache_read_tokens  = [long]$cacheR
    total_entrada      = [long]($inTok + $cacheW + $cacheR)
    output_tokens      = [long]$output
    thinking_tokens    = [long]$think
    tempo_ativo_s      = [math]::Round($activeMs / 1000)
    inicio             = Local $first
    fim                = Local $last
    tempo_total_s      = if ($first -and $last) { [math]::Round(($last - $first).TotalSeconds) } else { 0 }
  }
}

$rows | Export-Csv $OutCsv -NoTypeInformation -Delimiter ';' -Encoding UTF8
$rows | Format-Table execucao, modelo, esforco, iteracoes, interacao_humana, perguntas_modelo, mensagens_manuais, aprovacoes_manuais, total_entrada, output_tokens, tempo_ativo_s -AutoSize
Write-Host "CSV salvo em $OutCsv ($(@($rows).Count) execuções)" -ForegroundColor Green

if ($detalhes.Count -gt 0) {
  $detalhes | Export-Csv $InteracoesCsv -NoTypeInformation -Delimiter ';' -Encoding UTF8
  Write-Host "Interações detalhadas em $InteracoesCsv ($($detalhes.Count) registros)" -ForegroundColor Green
}
else {
  if (Test-Path $InteracoesCsv) { Remove-Item $InteracoesCsv }
  Write-Host 'Nenhuma interação humana fora do protocolo.' -ForegroundColor Green
}
