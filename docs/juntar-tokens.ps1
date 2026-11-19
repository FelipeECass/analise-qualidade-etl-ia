<#
  juntar-tokens.ps1
  Junta os CSVs de tokens das duas empresas num CSV só, com as mesmas colunas:
    tokens_metricas.csv (Claude, tokens-tcc.ps1) + tokens_metricas_openai.csv (OpenAI, tokens-openai.ps1)
    -> tokens_metricas_todos.csv

  O que muda em relação aos CSVs de origem:
    - coluna nova "empresa" (Anthropic ou OpenAI), na frente;
    - versao_claude_code e versao_codex viram uma coluna só: versao_agente;
    - coluna nova "iteracoes_correcao" = iteracoes - respostas_padrao (só as iterações que
      vieram do feedback do Comparador, sem as respostas padrão a perguntas do modelo);
    - subagentes e buscas_web ficam vazias no Claude (só o script da OpenAI mede isso;
      no Claude essas ferramentas são bloqueadas pela configuração gerenciada).

  Uso (depois de rodar tokens-tcc.ps1 e tokens-openai.ps1):
    powershell -ExecutionPolicy Bypass -File .\juntar-tokens.ps1
#>
param(
  [string]$ClaudeCsv = 'D:\Projects\TCC\tokens_metricas.csv',
  [string]$OpenAICsv = 'D:\Projects\TCC\tokens_metricas_openai.csv',
  [string]$OutCsv    = 'D:\Projects\TCC\tokens_metricas_todos.csv'
)
$ErrorActionPreference = 'Stop'

# Ordem das colunas no CSV final (colunas que não estiverem aqui vão para o fim)
$ordem = @(
  'empresa', 'modelo', 'esforco', 'prompt', 'execucao', 'modelo_api', 'esforco_aplicado', 'versao_agente', 'modo',
  'iteracoes', 'respostas_padrao', 'iteracoes_correcao',
  'interacao_humana', 'perguntas_modelo', 'mensagens_manuais', 'aprovacoes_manuais', 'interrupcoes', 'salvaguardas',
  'chamadas_api', 'turnos', 'input_tokens', 'cache_write_tokens', 'cache_read_tokens', 'total_entrada',
  'output_tokens', 'thinking_tokens', 'tempo_ativo_s', 'inicio', 'fim', 'tempo_total_s',
  'subagentes', 'buscas_web'
)
$renomear = @{ 'versao_claude_code' = 'versao_agente'; 'versao_codex' = 'versao_agente' }
$fontes = @(
  @{ Empresa = 'Anthropic'; Arquivo = $ClaudeCsv },
  @{ Empresa = 'OpenAI';    Arquivo = $OpenAICsv }
)

# 1) Lê as linhas das duas empresas
$registros = New-Object System.Collections.Generic.List[object]
foreach ($f in $fontes) {
  if (-not (Test-Path $f.Arquivo -PathType Leaf)) {
    Write-Warning "Não achei $($f.Arquivo). Rode antes o script de tokens dessa empresa."
    continue
  }
  $n = 0
  foreach ($r in (Import-Csv $f.Arquivo -Delimiter ';' -Encoding UTF8)) {
    $o = [ordered]@{ empresa = $f.Empresa }
    foreach ($p in $r.PSObject.Properties) {
      $nome = if ($renomear.ContainsKey($p.Name)) { $renomear[$p.Name] } else { $p.Name }
      $o[$nome] = $p.Value
    }
    $iter = 0; $padrao = 0
    [void][int]::TryParse([string]$o['iteracoes'], [ref]$iter)
    [void][int]::TryParse([string]$o['respostas_padrao'], [ref]$padrao)
    $o['iteracoes_correcao'] = [string]($iter - $padrao)
    $registros.Add($o)
    $n++
  }
  Write-Host "$($f.Empresa): $n execuções de $($f.Arquivo)"
}
if ($registros.Count -eq 0) { throw 'Nenhuma linha para juntar.' }

# 2) Colunas: a ordem acima e, no fim, qualquer coluna nova que só exista numa das empresas
$colunas = New-Object System.Collections.Generic.List[string]
foreach ($c in $ordem) { $colunas.Add($c) }
foreach ($o in $registros) { foreach ($k in $o.Keys) { if (-not $colunas.Contains([string]$k)) { $colunas.Add([string]$k) } } }

# 3) Todas as linhas com todas as colunas (vazio onde a empresa não tem o dado)
$linhas = foreach ($o in $registros) {
  $l = [ordered]@{}
  foreach ($c in $colunas) { $l[$c] = if ($o.Contains($c)) { $o[$c] } else { '' } }
  [pscustomobject]$l
}

$linhas | Sort-Object empresa, modelo, esforco, prompt, execucao |
  Export-Csv $OutCsv -NoTypeInformation -Delimiter ';' -Encoding UTF8
Write-Host "CSV salvo em $OutCsv ($($registros.Count) execuções, $($colunas.Count) colunas)" -ForegroundColor Green
