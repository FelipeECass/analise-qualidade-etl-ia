# ETL Anatel

Console C# para .NET SDK 10.0.404. Execução sem argumentos ou interação:

```sh
dotnet build src/chatgpt_gpt6_zeroshot_r1 -c Release
dotnet run --project src/chatgpt_gpt6_zeroshot_r1 -c Release --no-build
```

No Windows, usa `D:\Projects\TCC`. No Linux, encontra a raiz que contém
`files` e `src/DDL.sql` a partir da localização do executável. A saída é
`db/chatgpt_gpt6_zeroshot_r1.db`, recriada em cada execução com o DDL original.
Os ZIPs são processados em ordem alfabética de UF, mantendo a ordem dos registros.
A referência de datas é fixa em 2026-10-15.

A leitura em fluxo aceita campos entre aspas, aspas duplicadas, delimitadores e
quebras de linha dentro de valores. A deduplicação global usa uma tabela SQLite
temporária; os comandos são preparados e cada ZIP é carregado em uma transação.
As contagens acumuladas são escritas em stdout. Falhas retornam código 1 e mensagem
em stderr. Uma execução interrompida pode deixar uma carga parcial; a próxima
execução sempre recria o banco.

Após compilar, `python3 src/chatgpt_gpt6_zeroshot_r1/validar.py` verifica somente
dados sintéticos em uma pasta temporária isolada, removida ao terminar. Não acessa
nem compara o banco real com um resultado esperado.
