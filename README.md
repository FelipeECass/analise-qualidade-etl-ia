# TCC

Experimento comparando LLMs (Claude e OpenAI) na geração de um ETL dos dados da Anatel.

| Pasta | Conteúdo |
|---|---|
| `src/etl-base` | ETL de referência (gabarito) em C# |
| `comparator` | Comparador das bases geradas contra o gabarito |
| `db` | `DDL.sql` e `few_shot.sql` (o `base.db` de ~2 GB não é versionado) |
| `docker`, `docker-openai` | Ambientes de execução dos agentes |
| `docs` | Prompts e scripts de métricas (tokens, SonarQube) |
| `solucoes` | Código gerado por cada modelo/condição/run (`src` + `prompt`) |
| `resultados`, `resultados_reavaliacao` | Saídas do Comparador por modelo/condição/run |
| `analise` | Scripts de análise estatística e CSVs finais (ver `analise/LEIAME.md`) |
| `metricas` | Tokens, custo e logs do experimento |

Não versionados: bancos `.db`, `files/*.zip` (dados públicos da Anatel, ver `src/etl-base/README.md`), `.env` e `auth/` (credenciais).
