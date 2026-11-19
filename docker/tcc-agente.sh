#!/bin/bash
# Modo automático: roda o Claude sem interação (claude -p) e guarda a saída em JSON.
#
#   tcc-agente inicial  "<mensagem>"           -> nova sessão com a mensagem
#   tcc-agente reprompt /reprompt/feedback.md  -> retoma a última sessão e envia o arquivo
#
# Códigos de saída: 0 = ok | 1 = falha | 75 = limite de uso/servidor ocupado (tentar depois)
set -uo pipefail

modo=${1:-}; arg=${2:-}
pasta="$HOME/.claude/saidas"
mkdir -p "$pasta"
saida="$pasta/$(date -u +%Y%m%dT%H%M%SZ)_$modo.json"

# Sem perguntas ao usuário (não há ninguém para responder) e sem telas de permissão
# (o container já é isolado). As regras de bloqueio da configuração gerenciada continuam valendo.
comum=(--output-format json --dangerously-skip-permissions --disallowedTools AskUserQuestion)

# Tempo máximo da iteração (TCC_TEMPO_MAX_MIN, passado pelo run.ps1): passou disso, o
# Claude é encerrado e a iteração conta como falha (o experimento.ps1 refaz do zero).
limite=(timeout --kill-after=60 "${TCC_TEMPO_MAX_MIN:-180}m")

case "$modo" in
  inicial)  "${limite[@]}" claude -p "$arg" "${comum[@]}" > "$saida"; codigo=$? ;;
  reprompt) "${limite[@]}" claude --continue -p "$(cat "$arg")" "${comum[@]}" > "$saida"; codigo=$? ;;
  *) echo "uso: tcc-agente inicial <mensagem> | reprompt <arquivo>" >&2; exit 2 ;;
esac

# Histórico em Markdown do SpecStory a partir da sessão (sem rede, com tempo máximo)
timeout 300 specstory sync claude </dev/null >/dev/null 2>&1 || true

if [ $codigo -eq 124 ] || [ $codigo -eq 137 ]; then
  echo "ERRO: a iteração foi encerrada à força (passou de ${TCC_TEMPO_MAX_MIN:-180} min ou ficou sem memória)." | tee "$saida.tempo_esgotado" >&2
  exit 1
fi

if [ $codigo -ne 0 ] || grep -q '"is_error":true' "$saida"; then
  if grep -qiE 'usage limit|limit reached|rate.?limit|overloaded|529' "$saida"; then
    echo "Limite de uso ou servidor ocupado. Saída em $saida" >&2
    exit 75
  fi
  echo "O Claude terminou com erro (código $codigo). Saída em $saida" >&2
  exit 1
fi
# Garantia do modelo: se outro modelo gerou a resposta (por exemplo, um fallback do
# Claude Code depois que as salvaguardas interrompem uma resposta), a execução não vale.
if [ -n "${TCC_MODELO_ESPERADO:-}" ]; then
  outros=$(node -e '
    const o = JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"));
    const esperado = process.argv[2];
    process.stdout.write(Object.entries(o.modelUsage || {})
      .filter(([m, u]) => m !== esperado && (u.outputTokens || 0) > 0).map(([m]) => m).join(","));
  ' "$saida" "$TCC_MODELO_ESPERADO" 2>/dev/null) || outros="(não consegui ler o modelUsage da saída)"
  if [ -n "$outros" ]; then
    echo "ERRO: a resposta veio de outro modelo ($outros) em vez de $TCC_MODELO_ESPERADO. A execução não vale." | tee "$saida.erro" >&2
    exit 1
  fi
fi

echo "Claude terminou. Saída em $saida"
exit 0
