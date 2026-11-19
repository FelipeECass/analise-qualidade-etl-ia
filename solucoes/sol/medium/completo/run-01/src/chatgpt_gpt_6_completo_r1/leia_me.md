# ETL Anatel

Console C# para .NET SDK **10.0.404**, referência fixa **2026-10-15**.

A solução lê os 27 ZIPs em fluxo, aplica as transformações e conserva a primeira ocorrência de cada estação e emissão. O banco é criado executando integralmente `src/DDL.sql`. A deduplicação utiliza uma tabela temporária do SQLite em disco, fora do esquema persistente. O banco final possui as duas tabelas de usuário exigidas e a tabela interna `sqlite_sequence`, criada pelo `AUTOINCREMENT` do DDL.

## Execução

Na pasta do projeto, no Windows:

```powershell
dotnet build D:\Projects\TCC\src\chatgpt_gpt_6_completo_r1 -c Release
dotnet run --project D:\Projects\TCC\src\chatgpt_gpt_6_completo_r1 -c Release --no-build
```

No ambiente Linux fornecido:

```bash
dotnet build /workspace/src/chatgpt_gpt_6_completo_r1 -c Release
dotnet run --project /workspace/src/chatgpt_gpt_6_completo_r1 -c Release --no-build
```

Sem argumentos. No Windows a raiz é `D:\Projects\TCC`. No Linux a raiz é localizada nos ancestrais do executável pela presença de `files` e `src/DDL.sql`. Saída: `db/chatgpt_gpt_6_completo_r1.db`. Cada execução recria esse arquivo; os ZIPs e o DDL são preservados. Os 27 arquivos obrigatórios e o DDL são verificados antes da recriação. As UFs são percorridas em ordem alfabética e os registros mantêm a ordem do CSV.

A execução retorna 0 em caso de sucesso; falhas retornam 1 com mensagem em stderr. Há progresso por UF e contagens finais. O pacote Microsoft.Data.Sqlite 10.0.15 é restaurado automaticamente, com cache em `.nuget/packages` na raiz. Arquivos temporários do SQLite ficam em `db/.temp`. As transações são confirmadas por ZIP; em caso de falha o banco parcial deve ser recriado por nova execução.

## Componentes

- `csv_reader.cs`: leitor streaming, validação de estrutura e localização das colunas por nome.
- `transform.cs`: regras puras de tipagem, normalização, derivação e validação.
- `database_writer.cs`: DDL, comandos preparados, transações e chave binária de deduplicação.
- `pipeline.cs`: caminhos, leitura dos ZIPs, mapeamento e contadores.
- `program.cs`: execução e código de saída.

## Verificação

Depois de compilar, a partir da raiz:

```bash
python3 verificacao/verificar.py
python3 verificacao/auditar_banco.py
```

O primeiro script executa testes sintéticos isolados em `verificacao/fixture`, incluindo reexecução e falhas. O segundo verifica o banco real: esquema, integridade, chaves estrangeiras, ausência de duplicatas e invariantes. Nenhum deles compara a base real linha a linha com um banco esperado. O plano e o registro da validação estão na raiz do espaço de trabalho.
