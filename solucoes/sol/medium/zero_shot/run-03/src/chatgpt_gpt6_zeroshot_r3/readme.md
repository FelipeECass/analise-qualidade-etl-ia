# ETL Anatel

Programa de console em C# para .NET SDK 10.0.404, com Microsoft.Data.Sqlite 10.0.15.

A raiz é localizada a partir da pasta do executável procurando `files/` e `src/DDL.sql`.
Neste ambiente, `/workspace` corresponde a `D:\Projects\TCC`. Os mesmos caminhos relativos
funcionam em Windows. Não são necessários argumentos ou interação.

```sh
dotnet build src/chatgpt_gpt6_zeroshot_r3 -c Release
dotnet run --project src/chatgpt_gpt6_zeroshot_r3 -c Release --no-build
```

A saída é `db/chatgpt_gpt6_zeroshot_r3.db`. Cada execução recria esse banco com o
conteúdo original de `src/DDL.sql`. A referência temporal é fixa: 2026-10-15.
Os ZIPs são lidos em ordem alfabética de UF; a ordem dos registros dentro de cada
CSV é preservada. O primeiro registro prevalece tanto para estação quanto para emissão.

O leitor processa os CSVs diretamente dos ZIPs, sem extraí-los. A deduplicação usa
uma tabela temporária do SQLite, com equivalência entre valores nulos e entre
representações numéricas equivalentes. Somente `estacao` e `emissao` são tabelas
de aplicação persistentes; `sqlite_sequence` é criada pelo próprio SQLite em
razão do `AUTOINCREMENT` definido no DDL. As transações são confirmadas por ZIP.

O console informa contagens acumuladas por arquivo. Sucesso retorna código 0;
falha retorna código 1 e informa o erro em stderr. Após uma falha, uma nova execução
recria a saída desde o início.

Para executar a verificação de integração com dados sintéticos após compilar:

```sh
python3 src/chatgpt_gpt6_zeroshot_r3/verificar.py
```

A verificação cria e remove seu próprio diretório temporário dentro da raiz,
sem modificar as entradas ou o banco de produção. Ela cobre limpeza, normalização,
derivações, datas, validações com três estados, campos CSV multilinha, cabeçalhos
reordenados, deduplicação, chave com UF, atributos da primeira linha, integridade,
reexecução e código de falha. Não compara a base real com um resultado esperado.
