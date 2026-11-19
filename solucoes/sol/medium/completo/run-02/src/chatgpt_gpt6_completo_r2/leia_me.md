# ETL Anatel

Console C# com SDK **.NET 10.0.404**, target `net10.0` e Microsoft.Data.Sqlite 10.0.15.

No Windows, a raiz é `D:\Projects\TCC`. No Linux, o programa encontra a raiz que contém `files/` e `src/DDL.sql` a partir da localização do executável. Não recebe argumentos nem solicita interação.

```sh
dotnet build src/chatgpt_gpt6_completo_r2 -c Release
dotnet run --project src/chatgpt_gpt6_completo_r2 -c Release --no-build
```

Entrada: os 27 arquivos `<UF>.zip` em `files/`. Saída: `db/chatgpt_gpt6_completo_r2.db`. O DDL é lido diretamente de `src/DDL.sql`, sem alterações. A data de referência é fixa em **2026-10-15**.

O CSV é lido diretamente do ZIP, com cabeçalho por nome, UTF-8, separador `|`, escapes de aspas e campos com múltiplas linhas. As chaves de emissão são armazenadas em uma tabela temporária SQLite, com codificação binária sem colisões; não é necessário manter todas as emissões na memória. As estações usam um conjunto de chaves em memória para preservar os atributos da primeira linha. A estrutura temporária fica em `db/` e não integra o banco entregue.

O processamento usa transações em lotes de 25 mil linhas. Antes de publicar o banco, verifica integridade, referências, contagens e quantidade de tabelas de aplicação. Um banco intermediário é renomeado para o destino somente após sucesso. Uma saída anterior é substituída apenas nesse momento. Falhas retornam código 1 e mensagem em stderr; sucesso retorna 0.

O primeiro build restaura dependências do NuGet. O plano completo está em `../../PLANO.md`; os testes auxiliares, fora deste projeto, estão em `../../verificacao/`.
