# ETL de licenciamento Anatel

Programa de console C# / .NET SDK **10.0.404**, referência fixa **2026-10-15**.
Biblioteca SQLite: Microsoft.Data.Sqlite 10.0.15. Não requer argumentos nem interação.

## Executar no Windows

```powershell
dotnet build D:\Projects\TCC\src\chatgpt_gpt_6_completo_r3 -c Release
dotnet run --project D:\Projects\TCC\src\chatgpt_gpt_6_completo_r3 -c Release --no-build
```

## Executar neste ambiente Linux

```bash
dotnet build /workspace/src/chatgpt_gpt_6_completo_r3 -c Release
dotnet run --project /workspace/src/chatgpt_gpt_6_completo_r3 -c Release --no-build
```

A raiz é localizada pelos ancestrais da pasta do executável, contendo `src/DDL.sql` e `files/`.
No Windows também existe fallback para `D:\Projects\TCC`. Neste ambiente, a raiz é `/workspace`.
Os 27 arquivos `files/<UF>.zip` são obrigatórios. Cada um deve conter um único CSV.
O DDL original é executado integralmente. Saída: `db/chatgpt_gpt_6_completo_r3.db`.
O programa exige banco inexistente, como especificado; não sobrescreve banco anterior.
Em falha, escreve em stderr, retorna 1 e remove apenas o banco parcial que criou.
Em sucesso, retorna 0 e mostra contadores por UF e totais.

## Organização

- `Program.cs`: caminhos, código de saída e remoção de banco parcial.
- `CsvSource.cs`: leitura CSV UTF-8 diretamente do ZIP, mapeada pelo cabeçalho.
- `Transform.cs`: limpeza, tipagem, normalização e regras de transformação.
- `Pipeline.cs`: deduplicação global, primeira ocorrência e gravação parametrizada em lotes de 25 mil emissões.
- `PLANO.md`: decisões registradas antes da implementação.
- `VERIFICACAO.md`: resultados da revisão e execução.

`HashSet` conserva as chaves de estações e emissões durante toda a execução para garantir deduplicação entre arquivos. A memória cresce com a quantidade de registros únicos; o CSV é lido em streaming.
O AUTOINCREMENT do DDL cria a tabela interna `sqlite_sequence`; as únicas tabelas de domínio são `estacao` e `emissao`.

## Verificação

Os scripts auxiliares exigem Python 3, somente biblioteca padrão:

```bash
python3 /workspace/src/chatgpt_gpt_6_completo_r3/verificar.py
python3 /workspace/src/chatgpt_gpt_6_completo_r3/validar_banco.py
```

O primeiro testa casos sintéticos em uma pasta temporária local, removida ao final. O segundo abre o banco real em modo somente leitura, verifica DDL, integridade, referências, tipos de texto/datas, duplicidades e invariantes; grava o relatório `validacao_banco.json`. Nenhum compara a saída real linha a linha com resultado esperado externo.
