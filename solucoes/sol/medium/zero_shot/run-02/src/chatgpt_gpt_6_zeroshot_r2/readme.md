# ETL de licenciamento Anatel

Programa de console C# para .NET 10, implementado do zero. O SDK é 10.0.404 e a data de referência é 2026-10-15.

O projeto deve ficar em `src/chatgpt_gpt_6_zeroshot_r2`, abaixo da raiz que contém `src/DDL.sql`, `files/` e `db/`. No Windows, essa raiz é `D:\Projects\TCC`; neste ambiente Linux é `/workspace`. Os caminhos são resolvidos a partir da localização do executável, sem argumentos ou interação.

Na raiz do espaço de trabalho:

```sh
dotnet build src/chatgpt_gpt_6_zeroshot_r2 -c Release
dotnet run --project src/chatgpt_gpt_6_zeroshot_r2 -c Release --no-build
```

No Windows, os comandos da especificação também podem ser usados:

```powershell
dotnet build D:\Projects\TCC\src\chatgpt_gpt_6_zeroshot_r2 -c Release
dotnet run --project D:\Projects\TCC\src\chatgpt_gpt_6_zeroshot_r2 -c Release --no-build
```

O resultado é `db/chatgpt_gpt_6_zeroshot_r2.db`. O esquema é executado diretamente do DDL original, sem modificações. A tabela interna `sqlite_sequence` é criada automaticamente pelo SQLite por causa de `AUTOINCREMENT` no DDL.

Os 27 ZIPs são processados em ordem de sigla de UF, preservando a ordem interna dos registros. CsvHelper interpreta campos entre aspas, aspas escapadas, separadores e quebras de linha dentro dos campos. As colunas são localizadas pelos nomes do cabeçalho. Identificadores permanecem texto e as transformações antecedem a deduplicação. As chaves de emissão usam uma representação sem colisões por concatenação, com tamanho de cada componente e marcador distinto para NULL.

A deduplicação global usa um banco auxiliar em `db/`, evitando manter todas as emissões em memória. Cada ZIP é carregado em uma transação com comandos preparados e chaves estrangeiras habilitadas. O banco final é publicado somente depois de concluir todos os arquivos. Os arquivos auxiliares `.building` e `.dedup` são removidos ao terminar. O programa retorna 0 no sucesso; em caso de falha, escreve o erro em stderr e retorna 1.

As dependências têm versões fixas no arquivo do projeto. A referência explícita a SQLitePCLRaw 3.0.5 fornece o SQLite nativo atualizado.

As verificações sintéticas, independentes dos dados reais, podem ser executadas na raiz:

```sh
dotnet run --project tests/chatgpt_gpt_6_zeroshot_r2 -c Release
```

Elas cobrem limpeza, números não finitos, datas estritas, limites de palavra das operadoras, largura de banda, arredondamento, validações com três estados e integração com CSV dentro de ZIP e SQLite. A integração usa cabeçalhos invertidos, texto com aspas, barra vertical e quebra de linha, zeros à esquerda, duplicatas com números equivalentes, primeira ocorrência e a mesma identificação em UFs diferentes. Não usa um banco de resultado esperado.
