FROM node:22-bookworm

# Ferramentas básicas, libicu (para o .NET) e o necessário para o firewall.
# Adicione aqui o cliente do seu SGBD se o Claude precisar executar SQL
# (ex.: sqlite3, postgresql-client, default-mysql-client).
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      git ca-certificates libicu72 iptables ipset dnsutils \
 && rm -rf /var/lib/apt/lists/*

# .NET 10 SDK (compila/executa os projetos gerados e roda o Comparador.dll;
# já inclui os runtimes .NET e ASP.NET Core)
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
 && bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
 && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
 && rm /tmp/dotnet-install.sh
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
RUN dotnet --info

# Atalho: "comparador <args>" = "dotnet /opt/comparador/Comparador.dll <args>"
RUN printf '#!/bin/sh\nexec dotnet /opt/comparador/Comparador.dll "$@"\n' > /usr/local/bin/comparador \
 && chmod +x /usr/local/bin/comparador

# Claude Code
RUN npm install -g @anthropic-ai/claude-code

# Re-prompting: retoma a última sessão e envia o conteúdo do arquivo como mensagem
RUN printf '#!/bin/sh\nexec claude --continue "$(cat "$1")"\n' > /usr/local/bin/claude-reprompt \
 && chmod +x /usr/local/bin/claude-reprompt

# SpecStory CLI (build Linux; o specstory.exe do Windows não roda dentro do container)
RUN curl -fsSL https://specstory.com/install.sh | SPECSTORY_INSTALL_DIR=/usr/local/bin sh

# Config/sessões do Claude num diretório só (montado por execução pelo run.ps1).
# Sem telemetria, relatório de erro nem atualização automática: o único tráfego
# é para a API.
ENV CLAUDE_CONFIG_DIR=/home/node/.claude \
    CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1 \
    DISABLE_AUTOUPDATER=1 \
    BASH_DEFAULT_TIMEOUT_MS=1800000 \
    BASH_MAX_TIMEOUT_MS=3600000
# (comandos podem rodar até 30 min, ou 60 se o Claude pedir, sem irem para o segundo plano)
RUN mkdir -p /home/node/.claude && chown -R node:node /home/node/.claude

# Configuração gerenciada (prioridade máxima, o Claude não consegue alterar):
# sem subagentes, sem busca e sem acesso à web.
COPY managed-settings.json /etc/claude-code/managed-settings.json

# Firewall, troca para usuário sem privilégios e cópia para o disco interno
COPY init-firewall.sh entrypoint.sh sync-run.sh tcc-agente.sh /usr/local/bin/
RUN cd /usr/local/bin \
 && sed -i 's/\r$//' init-firewall.sh entrypoint.sh sync-run.sh tcc-agente.sh \
 && mv tcc-agente.sh tcc-agente \
 && chmod 755 init-firewall.sh entrypoint.sh sync-run.sh tcc-agente \
 && chmod 644 /etc/claude-code/managed-settings.json

# Pastas de trabalho no disco interno do container (rápido)
RUN mkdir -p /workspace /resultados /gabarito \
 && chown node:node /workspace /resultados /gabarito

WORKDIR /workspace
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]

# Sessões salvas só localmente em /workspace/.specstory/history
CMD ["specstory", "run", "claude", "--no-cloud-sync"]
