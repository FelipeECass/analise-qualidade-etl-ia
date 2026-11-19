#!/bin/bash
# Trabalho em disco rápido: o Windows (D:) chega ao container por uma ponte lenta,
# então tudo é copiado para dentro do container antes e devolvido só no final.
#
#   /in/<nome>   -> copiado para /<nome> no início (a pasta original não é alterada)
#   /out/<nome>  -> copiado para /<nome> no início e devolvido para /out/<nome> no final
#
# "files" e "prompt" são montagens somente leitura e nunca são copiadas.
# SYNC_EXCLUDE_IN: caminhos extras a ignorar na cópia de entrada (ex.: "./db").
set -uo pipefail

# Ctrl+C e afins vão para o programa; este script continua para copiar o resultado.
trap ':' INT TERM

BASE_EXCLUDES=(--exclude=./files --exclude=./prompt)
IN_EXCLUDES=("${BASE_EXCLUDES[@]}")
set -f  # os padrões (ex.: ./src/*/bin) vão para o tar sem serem expandidos aqui
for p in ${SYNC_EXCLUDE_IN:-}; do IN_EXCLUDES+=(--exclude="$p"); done
set +f

copiar() {  # copiar <origem> <destino> <excludes...>
  local de=$1 para=$2; shift 2
  # Copia os itens de dentro da pasta, não a pasta em si ("."): o tar do Debian tenta
  # mudar permissão/data da pasta de destino, e o Windows não deixa.
  local itens=()
  while IFS= read -r -d '' i; do itens+=("$i"); done \
    < <(cd "$de" && find . -mindepth 1 -maxdepth 1 ! -name files ! -name prompt -print0)
  [ ${#itens[@]} -eq 0 ] && return 0
  local saida reais
  saida=$( { tar -C "$de" "$@" -cf - "${itens[@]}" \
      | tar -C "$para" --no-same-owner --no-same-permissions --no-overwrite-dir --warning=no-timestamp -xf - ; } 2>&1 )
  # O Windows não deixa mudar data/permissão de pastas que já existem. Isso não afeta
  # o conteúdo copiado, então só conta como erro o que não for esse aviso.
  reais=$(printf '%s\n' "$saida" \
    | grep -v -E 'Cannot (utime|change mode|change ownership)|Exiting with failure status due to previous errors' \
    | grep -v '^[[:space:]]*$' || true)
  if [ -n "$reais" ]; then
    printf '%s\n' "$reais" >&2
    return 1
  fi
  return 0
}

for d in /in/* /out/*; do
  [ -d "$d" ] || continue
  nome=$(basename "$d")
  echo "Preparando /$nome..."
  if ! copiar "$d" "/$nome" "${IN_EXCLUDES[@]}"; then
    echo "ERRO ao copiar $d para dentro do container" >&2
    exit 98
  fi
done

# ---- Medição de recursos (cgroup do container) ------------------------------------
# Se TCC_RECURSOS estiver definido, grava em TCC_RECURSOS o consumo do comando principal:
# tempo de CPU, núcleos médios, pico de memória, mortes por falta de memória, disco e
# processos. Os contadores de CPU e disco são a diferença entre o início e o fim do comando;
# o pico de memória vale para o container inteiro e inclui o cache de arquivos.
# A medição nunca muda o código de saída.
cg=/sys/fs/cgroup
campo() { awk -v k="$2" '$1==k{print $2; exit}' "$cg/$1" 2>/dev/null; }
io_total() { awk -v k="$1" '{for(i=2;i<=NF;i++){split($i,a,"="); if(a[1]==k) s+=a[2]}} END{print s+0}' "$cg/io.stat" 2>/dev/null; }
recursos_inicio() {
  [ -n "${TCC_RECURSOS:-}" ] || return 0
  r_t0=$(date +%s.%N); r_ini=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  r_cpu0=$(campo cpu.stat usage_usec); r_usr0=$(campo cpu.stat user_usec); r_sys0=$(campo cpu.stat system_usec)
  r_thr0=$(campo cpu.stat throttled_usec); r_nthr0=$(campo cpu.stat nr_throttled)
  r_rb0=$(io_total rbytes); r_wb0=$(io_total wbytes)
}
recursos_fim() {  # recursos_fim <código de saída do comando>
  [ -n "${TCC_RECURSOS:-}" ] || return 0
  local t1 fim cpu1 usr1 sys1 thr1 nthr1 rb1 wb1 pico atual limite_mem cpumax oom pids
  t1=$(date +%s.%N); fim=$(date -u +%Y-%m-%dT%H:%M:%SZ)
  cpu1=$(campo cpu.stat usage_usec); usr1=$(campo cpu.stat user_usec); sys1=$(campo cpu.stat system_usec)
  thr1=$(campo cpu.stat throttled_usec); nthr1=$(campo cpu.stat nr_throttled)
  rb1=$(io_total rbytes); wb1=$(io_total wbytes)
  pico=$(cat "$cg/memory.peak" 2>/dev/null); atual=$(cat "$cg/memory.current" 2>/dev/null)
  limite_mem=$(cat "$cg/memory.max" 2>/dev/null); cpumax=$(cat "$cg/cpu.max" 2>/dev/null)
  oom=$(campo memory.events oom_kill); pids=$(cat "$cg/pids.peak" 2>/dev/null)
  mkdir -p "$(dirname "$TCC_RECURSOS")" 2>/dev/null
  awk -v ini="$r_ini" -v fim="$fim" -v t0="${r_t0:-0}" -v t1="$t1" -v cod="$1" \
      -v c0="${r_cpu0:-0}" -v c1="${cpu1:-0}" -v u0="${r_usr0:-0}" -v u1="${usr1:-0}" \
      -v s0="${r_sys0:-0}" -v s1="${sys1:-0}" -v h0="${r_thr0:-0}" -v h1="${thr1:-0}" \
      -v n0="${r_nthr0:-0}" -v n1="${nthr1:-0}" -v rb0="${r_rb0:-0}" -v rb1="${rb1:-0}" \
      -v wb0="${r_wb0:-0}" -v wb1="${wb1:-0}" -v pico="${pico:-NA}" -v atual="${atual:-NA}" \
      -v lm="${limite_mem:-NA}" -v cm="${cpumax:-NA}" -v oom="${oom:-NA}" -v pids="${pids:-NA}" '
    function mb(x) { return (x ~ /^[0-9]+$/) ? sprintf("%.1f", x/1048576) : "NA" }
    BEGIN {
      d = t1 - t0; cpu = (c1 - c0) / 1e6
      split(cm, q, " "); lim = (q[1] ~ /^[0-9]+$/ && q[2] > 0) ? sprintf("%.2f", q[1]/q[2]) : "sem_limite"
      print "inicio_utc=" ini; print "fim_utc=" fim; print "codigo_saida=" cod
      printf "duracao_s=%.1f\n", d
      printf "cpu_s=%.1f\n", cpu
      printf "cpu_usuario_s=%.1f\n", (u1 - u0) / 1e6
      printf "cpu_sistema_s=%.1f\n", (s1 - s0) / 1e6
      printf "nucleos_medios=%.2f\n", (d > 0 ? cpu / d : 0)
      print "cpus_limite=" lim
      printf "cpu_estrangulada_s=%.1f\n", (h1 - h0) / 1e6
      print "periodos_estrangulados=" (n1 - n0)
      print "mem_pico_mb=" mb(pico); print "mem_final_mb=" mb(atual)
      print "mem_limite_mb=" (lm == "max" ? "sem_limite" : mb(lm))
      print "mortes_falta_memoria=" oom
      printf "disco_lido_mb=%.1f\n", (rb1 - rb0) / 1048576
      printf "disco_gravado_mb=%.1f\n", (wb1 - wb0) / 1048576
      print "processos_pico=" pids
    }' > "$TCC_RECURSOS" 2>/dev/null || true
}
# -------------------------------------------------------------------------------------

cd /workspace 2>/dev/null || true
recursos_inicio
"$@"
codigo=$?
recursos_fim "$codigo" || true

for d in /out/*; do
  [ -d "$d" ] || continue
  nome=$(basename "$d")
  echo "Copiando resultado de /$nome para a pasta no Windows (não feche a janela)..."
  if copiar "/$nome" "$d" "${BASE_EXCLUDES[@]}"; then
    echo "Resultado copiado."
  else
    echo "ERRO: falha ao copiar /$nome de volta. O resultado desta execução pode estar incompleto." >&2
    codigo=99
  fi
done

exit $codigo
