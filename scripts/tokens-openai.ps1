<#
  tokens-openai.ps1
  Versão do tokens-tcc.ps1 para o Codex (OpenAI). Lê as sessões brutas do Codex (rollout JSONL)
  de cada execução em D:\Projects\TCC_GitHub\TCC\logs e gera:
    - tokens_metricas_openai.csv : uma linha por execução (tokens, chamadas, tempo e interações)
    - interacoes_openai.csv      : uma linha por interação humana fora do protocolo
                                   (pergunta do modelo, mensagem digitada, aprovação, interrupção)

  As colunas são as mesmas do tokens_metricas.csv do Claude, na mesma ordem, com três diferenças:
    - versao_codex no lugar de versao_claude_code;
    - subagentes e buscas_web no final (chamadas de spawn_agent e de busca na web; devem ser 0,
      porque as duas coisas estão desligadas na configuração gerenciada).
  Correspondência dos tokens (a OpenAI conta o cache dentro da entrada):
    input_tokens       = entrada sem cache (input_tokens - cached_input_tokens)
    cache_write_tokens = cache_write_input_tokens (0 quando a API não informa)
    cache_read_tokens  = cached_input_tokens
    total_entrada      = soma dos três = input_tokens da OpenAI
    output_tokens      = output_tokens (inclui o raciocínio, como no Claude)
    thinking_tokens    = reasoning_output_tokens

  Uso:
    powershell -ExecutionPolicy Bypass -File .\tokens-openai.ps1

  Estrutura lida: logs\<modelo>\<esforço>\<prompt>\run-NN\sessions\**\rollout-*.jsonl
  (execuções do Claude, sem a pasta sessions, são ignoradas)
  Protocolo (não conta como interação): a 1ª mensagem (prompt inicial) e os re-prompts
  (mensagens que começam com o texto padrão do feedback do Comparador).
#>
param(
  [string]$LogsRoot      = 'D:\Projects\TCC_GitHub\TCC\logs',
  [string]$OutCsv        = 'D:\Projects\TCC_GitHub\TCC\tokens_metricas_openai.csv',
  [string]$InteracoesCsv = 'D:\Projects\TCC_GitHub\TCC\interacoes_openai.csv',
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
  ($_.FullName.Substring($root.Length + 1) -split '\\').Count -eq 4 -and
  # Só pastas do Codex: as do Claude também podem ter uma pasta "sessions", mas sem rollout-*.jsonl
  @(Get-ChildItem (Join-Path $_.FullName 'sessions') -Recurse -Filter 'rollout-*.jsonl' -ErrorAction SilentlyContinue).Count -gt 0
}
if (-not $runs) { throw "Nenhuma execução do Codex (pasta sessions) encontrada em $root" }

# Só estas linhas do rollout interessam; o resto (saídas de ferramentas etc.) é pulado sem ler o JSON
$filtro = '"type":"(session_meta|turn_context)"|"payload":\{"type":"(token_count|task_complete|task_started|user_message|item_completed|turn_aborted|error|exec_approval_request|apply_patch_approval_request|request_permissions|web_search_end|function_call|web_search_call)"'

$detalhes = New-Object System.Collections.Generic.List[object]

$rows = foreach ($run in ($runs | Sort-Object FullName)) {
  $parts = $run.FullName.Substring($root.Length + 1) -split '\\'
  $files = @(Get-ChildItem (Join-Path $run.FullName 'sessions') -Recurse -Filter 'rollout-*.jsonl' -ErrorAction SilentlyContinue)
  if (-not $files) { Write-Warning "Sem sessão em $($run.FullName)"; continue }

  $vistos   = New-Object System.Collections.Generic.HashSet[string]   # (sessão, ordinal): evita contar a mesma linha duas vezes
  $chamadas = New-Object System.Collections.Generic.List[object]       # uso de tokens por chamada à API
  $ultimoTotal = @{}                                                   # total acumulado anterior, por sessão
  $humanEv  = New-Object System.Collections.Generic.List[object]   # mensagens enviadas ao Codex (evento user_message)
  $humanIt  = New-Object System.Collections.Generic.List[object]   # as mesmas, como item UserMessage (versões novas)
  $asks     = New-Object System.Collections.Generic.List[object]
  $aprov    = New-Object System.Collections.Generic.List[object]
  $interrup = New-Object System.Collections.Generic.List[object]
  $models   = New-Object System.Collections.Generic.HashSet[string]
  $efforts  = New-Object System.Collections.Generic.HashSet[string]
  $versions = New-Object System.Collections.Generic.HashSet[string]
  $first = $null; $last = $null; $activeMs = 0; $turns = 0; $salvaguardas = 0
  $subagentes = 0; $buscas = 0

  foreach ($f in $files) {
    $sessao = $f.BaseName
    foreach ($line in [IO.File]::ReadLines($f.FullName)) {
      # Horário de toda linha (início/fim da execução), sem ler o JSON inteiro
      if ($line -match '^\{"timestamp":"([^"]+)"') {
        $ts = ConvertTo-Utc $Matches[1]
        if (-not $first -or $ts -lt $first) { $first = $ts }
        if (-not $last  -or $ts -gt $last)  { $last  = $ts }
      }
      if ($line -notmatch $filtro) { continue }
      try { $d = $line | ConvertFrom-Json } catch { continue }
      $chave = "$sessao|$($d.ordinal)|$($d.timestamp)"
      if (-not $vistos.Add($chave)) { continue }
      $p  = $d.payload
      $ts = ConvertTo-Utc $d.timestamp

      if ($d.type -eq 'session_meta') {
        if ($p.cli_version) { [void]$versions.Add([string]$p.cli_version) }
        if ($p.id) { $sessao = [string]$p.id }
        continue
      }
      if ($d.type -eq 'turn_context') {
        if ($p.model)  { [void]$models.Add([string]$p.model) }
        if ($p.effort) { [void]$efforts.Add([string]$p.effort) }
        continue
      }
      if ($d.type -eq 'response_item') {
        if ($p.type -eq 'function_call') {
          $nome = [string]$p.name
          if ($nome -like '*request_user_input*') {
            $q = $null; try { $q = ($p.arguments | ConvertFrom-Json).questions } catch { }
            $textos = if ($q) { @($q | ForEach-Object { if ($_.question) { $_.question } elseif ($_.title) { $_.title } else { "$_" } }) } else { @([string]$p.arguments) }
            foreach ($t in $textos) { $asks.Add([pscustomobject]@{ ts = $ts; texto = $t }) }
          }
          elseif ($nome -like 'spawn_agent*') { $subagentes++ }
        }
        elseif ($p.type -eq 'web_search_call') { $buscas++ }
        continue
      }
      if ($d.type -ne 'event_msg') { continue }

      switch ([string]$p.type) {
        'token_count' {
          # Uma resposta nova muda o total acumulado; avisos repetidos (ex.: limites) não mudam
          if ($p.info -and $p.info.last_token_usage) {
            $tot = [string](N $p.info.total_token_usage.total_tokens)
            if ($ultimoTotal[$sessao] -ne $tot) {
              $ultimoTotal[$sessao] = $tot
              $chamadas.Add($p.info.last_token_usage)
            }
          }
        }
        'task_complete' {
          $turns++
          if ($p.duration_ms) { $activeMs += N $p.duration_ms }
          elseif ($p.started_at -and $p.completed_at) { $activeMs += 1000 * ((N $p.completed_at) - (N $p.started_at)) }
        }
        'user_message' {
          if ($p.message) { $humanEv.Add([pscustomobject]@{ ts = $ts; texto = [string]$p.message }) }
        }
        'item_completed' {
          if ($p.item -and $p.item.type -eq 'UserMessage') {
            $texto = (@($p.item.content) | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join "`n"
            if ($texto) { $humanIt.Add([pscustomobject]@{ ts = $ts; texto = $texto }) }
          }
        }
        'turn_aborted' { $interrup.Add([pscustomobject]@{ ts = $ts; motivo = [string]$p.reason }) }
        { $_ -in 'exec_approval_request', 'apply_patch_approval_request', 'request_permissions' } {
          $aprov.Add([pscustomobject]@{ ts = $ts; motivo = [string]$p.type })
        }
        'web_search_end' { $buscas++ }
        'error' {
          if ((ConvertTo-Json $p -Depth 6 -Compress) -match 'cyber_policy|bio_policy|misalignment_policy|content_filter') { $salvaguardas++ }
        }
      }
    }
  }

  # A mesma mensagem pode aparecer nos dois formatos: usa um só (o item, se existir)
  $msgs = if ($humanIt.Count -gt 0) { @($humanIt | Sort-Object ts) } else { @($humanEv | Sort-Object ts) }
  $reprompts = 0; $padrao = 0; $manuais = @()
  for ($i = 0; $i -lt $msgs.Count; $i++) {
    if ($i -eq 0) { continue }
    $t = $msgs[$i].texto.TrimStart()
    if ($t.StartsWith($ReprompPrefixo)) { $reprompts++ }
    elseif ($t.StartsWith($PadraoPrefixo)) { $reprompts++; $padrao++ }
    else { $manuais += $msgs[$i] }
  }

  # Modo automático: o tcc-agente guarda a saída do codex exec em saidas\*.jsonl
  $modoAuto = @(Get-ChildItem (Join-Path $run.FullName 'saidas') -Filter '*.jsonl' -ErrorAction SilentlyContinue).Count -gt 0

  $novo = { param($horario, $tipo, $texto, $resposta)
    [pscustomobject][ordered]@{ modelo = $parts[0]; esforco = $parts[1]; prompt = $parts[2]; execucao = $parts[3]
      horario = $horario; tipo = $tipo; texto = $texto; resposta = $resposta } }
  foreach ($a in $asks)     { $detalhes.Add((& $novo (Local $a.ts) 'pergunta_do_modelo' (Curto $a.texto) '')) }
  foreach ($m in $interrup) { $detalhes.Add((& $novo (Local $m.ts) 'interrupcao' "turno interrompido ($($m.motivo))" '')) }
  foreach ($m in $manuais)  { $detalhes.Add((& $novo (Local $m.ts) 'mensagem_manual' (Curto $m.texto) '')) }
  foreach ($p in $aprov)    { $detalhes.Add((& $novo (Local $p.ts) 'aprovacao_manual' "permissão ($($p.motivo))" '')) }

  $u = $chamadas.ToArray()   # @() sobre a List dava "Os tipos de argumento não correspondem" no PowerShell 5.1
  $inTot   = ($u | ForEach-Object { N $_.input_tokens } | Measure-Object -Sum).Sum
  $cacheR  = ($u | ForEach-Object { N $_.cached_input_tokens } | Measure-Object -Sum).Sum
  $cacheW  = ($u | ForEach-Object { N $_.cache_write_input_tokens } | Measure-Object -Sum).Sum
  $output  = ($u | ForEach-Object { N $_.output_tokens } | Measure-Object -Sum).Sum
  $think   = ($u | ForEach-Object { N $_.reasoning_output_tokens } | Measure-Object -Sum).Sum
  $inSemCache = [long]$inTot - [long]$cacheR - [long]$cacheW
  if ($inSemCache -lt 0) { $inSemCache = 0 }
  $interagiu = ($asks.Count + $manuais.Count + $aprov.Count + $interrup.Count) -gt 0

  [pscustomobject][ordered]@{
    modelo             = $parts[0]
    esforco            = $parts[1]
    prompt             = $parts[2]
    execucao           = $parts[3]
    modelo_api         = ($models -join ',')
    esforco_aplicado   = ($efforts -join ',')
    versao_codex       = ($versions -join ',')
    modo               = if ($modoAuto) { 'automatico' } else { 'interativo' }
    iteracoes          = 1 + $reprompts
    interacao_humana   = if ($interagiu) { 'sim' } else { 'nao' }
    perguntas_modelo   = $asks.Count
    respostas_padrao   = $padrao
    mensagens_manuais  = $manuais.Count
    aprovacoes_manuais = $aprov.Count
    interrupcoes       = $interrup.Count
    salvaguardas       = $salvaguardas
    chamadas_api       = $u.Count
    turnos             = $turns
    input_tokens       = [long]$inSemCache
    cache_write_tokens = [long]$cacheW
    cache_read_tokens  = [long]$cacheR
    total_entrada      = [long]($inSemCache + $cacheW + $cacheR)
    output_tokens      = [long]$output
    thinking_tokens    = [long]$think
    tempo_ativo_s      = [math]::Round($activeMs / 1000)
    inicio             = Local $first
    fim                = Local $last
    tempo_total_s      = if ($first -and $last) { [math]::Round(($last - $first).TotalSeconds) } else { 0 }
    subagentes         = $subagentes
    buscas_web         = $buscas
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
