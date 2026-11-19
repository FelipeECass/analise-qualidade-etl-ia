#!/bin/bash
# Modo automático: roda o Codex sem interação (codex exec) e guarda a saída em JSONL.
#
#   tcc-agente inicial  "<mensagem>"           -> nova sessão com a mensagem
#   tcc-agente reprompt /reprompt/feedback.md  -> retoma a última sessão e envia o arquivo
#
# Modelo e esforço vêm de TCC_MODELO e TCC_ESFORCO (passados pelo run.ps1).
# Códigos de saída: 0 = ok | 1 = falha | 75 = limite de uso/servidor ocupado (tentar depois)
set -uo pipefail

modo=${1:-}; arg=${2:-}
modelo=${TCC_MODELO:?defina TCC_MODELO}
esforco=${TCC_ESFORCO:?defina TCC_ESFORCO}
pasta="$CODEX_HOME/saidas"
mkdir -p "$pasta"
base="$pasta/$(date -u +%Y%m%dT%H%M%SZ)_$modo"
saida="$base.jsonl"
erros="$base.stderr.txt"
falha() { echo "$1" | tee -a "$erros" >&2; }   # também vai para o arquivo (o experimento.ps1 lê)

# Sem aprovações (approval_policy = never e sandbox liberado na configuração gerenciada;
# o container já é isolado). Perguntas ao usuário não funcionam no codex exec:
# a ferramenta request_user_input devolve erro, e o modelo segue sozinho.
# --skip-git-repo-check: o /workspace não é um repositório git (igual ao Claude).
comum=(--json --skip-git-repo-check --dangerously-bypass-approvals-and-sandbox
       -m "$modelo" -c "model_reasoning_effort=\"$esforco\"")

# Tempo máximo da iteração (TCC_TEMPO_MAX_MIN, passado pelo run.ps1): passou disso, o
# Codex é encerrado e a iteração conta como falha (o experimento.ps1 refaz do zero).
limite=(timeout --kill-after=60 "${TCC_TEMPO_MAX_MIN:-180}m")

case "$modo" in
  inicial)  "${limite[@]}" codex exec "${comum[@]}" "$arg" </dev/null >"$saida" 2>"$erros"; codigo=$? ;;
  reprompt) "${limite[@]}" codex exec resume --last "${comum[@]}" "$(cat "$arg")" </dev/null >"$saida" 2>"$erros"; codigo=$? ;;
  *) echo "uso: tcc-agente inicial <mensagem> | reprompt <arquivo>" >&2; exit 2 ;;
esac

# Histórico em Markdown do SpecStory a partir da sessão (sem rede, com tempo máximo)
timeout 300 specstory sync codex --no-cloud-sync </dev/null >/dev/null 2>&1 || true

# Garantia do isolamento: se o Codex teve subagentes ou busca na web disponíveis
# (por exemplo, se uma versão nova ignorar a configuração gerenciada), a execução
# não vale para o experimento.
sessoes=$(find "$CODEX_HOME/sessions" -name 'rollout-*.jsonl' 2>/dev/null)
if [ -n "$sessoes" ]; then
  if grep -lqE '"name":"(spawn_agent|spawn_agents_on_csv)"' $sessoes; then
    falha "ERRO: o Codex teve ferramentas de subagentes disponíveis. A execução não vale."
    exit 1
  fi
  if grep -lqE '"type":"(web_search_call|web_search_begin|web_search_end)"' $sessoes; then
    falha "ERRO: o Codex usou busca na web. A execução não vale."
    exit 1
  fi
  # Garantia do modelo: todas as rodadas da sessão (turn_context) têm de usar o modelo pedido
  outros=$(node -e '
    const fs = require("fs"); const esperado = process.argv[1]; const outros = new Set();
    for (const f of process.argv.slice(2))
      for (const l of fs.readFileSync(f, "utf8").split("\n")) {
        if (!l.includes("\"turn_context\"")) continue;
        try { const d = JSON.parse(l); const m = d.payload && d.payload.model;
              if (d.type === "turn_context" && m && m !== esperado) outros.add(m); } catch (e) {}
      }
    process.stdout.write([...outros].join(","));
  ' "$modelo" $sessoes 2>/dev/null) || outros="(não consegui ler as sessões)"
  if [ -n "$outros" ]; then
    falha "ERRO: o Codex usou outro modelo ($outros) em vez de $modelo. A execução não vale."
    exit 1
  fi
fi

if [ $codigo -eq 124 ] || [ $codigo -eq 137 ]; then
  falha "ERRO: a iteração foi encerrada à força (passou de ${TCC_TEMPO_MAX_MIN:-180} min ou ficou sem memória)."
  exit 1
fi

if [ $codigo -ne 0 ] || grep -q '"type":"turn.failed"' "$saida"; then
  if grep -qiE 'usage.?limit|rate.?limit|too many requests|429|overloaded|server_overloaded|try again later' "$saida" "$erros"; then
    echo "Limite de uso ou servidor ocupado. Saída em $saida" >&2
    exit 75
  fi
  echo "O Codex terminou com erro (código $codigo). Saída em $saida" >&2
  tail -n 5 "$erros" >&2
  exit 1
fi
echo "Codex terminou. Saída em $saida"
exit 0
