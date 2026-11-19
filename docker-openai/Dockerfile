FROM node:22-bookworm

# Ferramentas básicas, libicu (para o .NET) e o necessário para o firewall.
# Adicione aqui o cliente do seu SGBD se o Codex precisar executar SQL
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

# Codex CLI, em versão fixa: o catálogo de modelos abaixo é gerado a partir dela.
# Para atualizar: docker build --build-arg CODEX_VERSION=<versão> -t tcc-openai .
ARG CODEX_VERSION=0.167.2
RUN npm install -g @openai/codex@${CODEX_VERSION} && codex --version

# Re-prompting (modo interativo): retoma a última sessão e envia o conteúdo do arquivo
RUN printf '#!/bin/sh\nexec codex resume --last "$(cat "$1")"\n' > /usr/local/bin/codex-reprompt \
 && chmod +x /usr/local/bin/codex-reprompt

# SpecStory CLI (build Linux; o specstory.exe do Windows não roda dentro do container)
RUN curl -fsSL https://specstory.com/install.sh | SPECSTORY_INSTALL_DIR=/usr/local/bin sh

# Config/sessões/login do Codex num diretório só, no disco interno do container.
# O run.ps1 monta a pasta de logs da execução em /out/codex e o sync-run.sh copia
# para /codex no início e de volta no final (o Codex guarda o estado em SQLite,
# que não funciona bem direto na pasta do Windows). ~/.codex aponta para lá
# para o SpecStory achar as sessões.
ENV CODEX_HOME=/codex
RUN mkdir -p /codex && chown node:node /codex \
 && ln -s /codex /home/node/.codex && chown -h node:node /home/node/.codex

# Catálogo de modelos sem as ferramentas de subagentes: o catálogo padrão da versão
# instalada, só com multi_agent_version apagado (usado pelo model_catalog_json).
# Gerado antes da configuração gerenciada, que aponta para este arquivo.
RUN mkdir -p /tmp/cx /etc/codex \
 && CODEX_HOME=/tmp/cx codex debug models > /tmp/models.json \
 && node -e 'const fs=require("fs");const d=JSON.parse(fs.readFileSync("/tmp/models.json","utf8"));for(const m of d.models){m.multi_agent_version=null;}fs.writeFileSync("/etc/codex/models.json",JSON.stringify(d));console.log("catálogo: "+d.models.map(m=>m.slug).join(", "))' \
 && rm -rf /tmp/cx /tmp/models.json

# Configuração gerenciada (prioridade máxima, o Codex não consegue alterar):
# sem subagentes, sem busca na web, sem conectores/plugins/navegador, sem aprovações.
COPY managed_config.toml requirements.toml /etc/codex/
RUN sed -i 's/\r$//' /etc/codex/managed_config.toml /etc/codex/requirements.toml \
 && chmod 644 /etc/codex/managed_config.toml /etc/codex/requirements.toml /etc/codex/models.json

# Firewall, troca para usuário sem privilégios e cópia para o disco interno
COPY init-firewall.sh entrypoint.sh sync-run.sh tcc-agente.sh /usr/local/bin/
RUN cd /usr/local/bin \
 && sed -i 's/\r$//' init-firewall.sh entrypoint.sh sync-run.sh tcc-agente.sh \
 && mv tcc-agente.sh tcc-agente \
 && chmod 755 init-firewall.sh entrypoint.sh sync-run.sh tcc-agente

# Pastas de trabalho no disco interno do container (rápido)
RUN mkdir -p /workspace /resultados /gabarito \
 && chown node:node /workspace /resultados /gabarito

WORKDIR /workspace
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]

# Sessões salvas só localmente em /workspace/.specstory/history
CMD ["specstory", "run", "codex", "--no-cloud-sync"]
