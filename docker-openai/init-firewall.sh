#!/bin/bash
# Firewall de saída do container.
# Libera SOMENTE HTTPS para os domínios abaixo. Todo o resto é bloqueado:
# internet, o seu Windows (host.docker.internal) e outros containers.
set -euo pipefail

ALLOWED_DOMAINS=(
  "chatgpt.com"           # API do Codex com login da assinatura do ChatGPT
  "auth.openai.com"       # login e renovação do token do ChatGPT
  "api.nuget.org"         # NuGet: dotnet restore dos projetos gerados
  "globalcdn.nuget.org"   # NuGet: download dos pacotes
)

fail() { echo "ERRO no firewall: $*" >&2; exit 1; }

# 1) Resolve os domínios permitidos antes de fechar a rede
ipset destroy allowed-domains 2>/dev/null || true
ipset create allowed-domains hash:net
for domain in "${ALLOWED_DOMAINS[@]}"; do
  ips=$(dig +short A "$domain" | grep -E '^[0-9]+(\.[0-9]+){3}$' || true)
  [ -n "$ips" ] || fail "não consegui resolver $domain"
  for ip in $ips; do ipset add allowed-domains "$ip" -exist; done
done

# 2) Limpa só a tabela filter (a tabela nat guarda as regras do DNS interno do Docker)
iptables -F
iptables -X

# 3) Regras permitidas
iptables -A INPUT  -i lo -j ACCEPT
iptables -A OUTPUT -o lo -j ACCEPT
iptables -A INPUT  -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
iptables -A OUTPUT -m conntrack --ctstate ESTABLISHED,RELATED -j ACCEPT
iptables -A OUTPUT -p udp --dport 53 -j ACCEPT
iptables -A OUTPUT -p tcp --dport 53 -j ACCEPT
iptables -A OUTPUT -p tcp --dport 443 -m set --match-set allowed-domains dst -j ACCEPT

# 4) Todo o resto: bloqueado
iptables -P INPUT DROP
iptables -P FORWARD DROP
iptables -P OUTPUT DROP
iptables -A OUTPUT -j REJECT --reject-with icmp-admin-prohibited

# IPv6 totalmente fechado (se o kernel suportar)
if ip6tables -L >/dev/null 2>&1; then
  ip6tables -F
  ip6tables -A INPUT  -i lo -j ACCEPT
  ip6tables -A OUTPUT -o lo -j ACCEPT
  ip6tables -P INPUT DROP
  ip6tables -P FORWARD DROP
  ip6tables -P OUTPUT DROP
fi

# 5) Confere se funcionou; se não, o container NÃO sobe
if curl -s --connect-timeout 5 -o /dev/null https://example.com; then
  fail "example.com ainda está acessível"
fi
for d in chatgpt.com auth.openai.com; do
  if ! curl -s --connect-timeout 10 -o /dev/null "https://$d"; then
    fail "$d não está acessível"
  fi
done
echo "Firewall ativo: só ${ALLOWED_DOMAINS[*]} liberado."
