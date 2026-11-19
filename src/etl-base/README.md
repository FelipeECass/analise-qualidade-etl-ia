# AnatelEtl

Exemplo de ETL (.NET 10 + Dapper + SQLite) para os ZIPs de licenciamento da Anatel.

```
src/AnatelEtl/
  Extract/AnatelZipReader.cs      lê o CSV (pipe, aspas) de dentro do ZIP, em streaming
  Transform/                      limpeza, tipagem, padronização e colunas derivadas
  Load/                           schema SQLite + repositório Dapper + consultas
  EtlPipeline.cs                  orquestra E → T → L e produz o EtlReport
tests/AnatelEtl.Tests/            xUnit (unidade + integração com SQLite em memória)
```

## Executar

```powershell
dotnet run --project src\AnatelEtl -c Release -- D:\Projects\TCC\files\SC.zip SC anatel_SC.db
```

Rodar de novo é idempotente: os dados da UF são apagados antes da carga.

## Testes

```powershell
dotnet test AnatelEtl.slnx
```

## SonarQube

```powershell
dotnet tool install --global dotnet-sonarscanner

dotnet sonarscanner begin /k:"anatel-etl" `
  /d:sonar.host.url="http://localhost:9000" `
  /d:sonar.token="<TOKEN>" `
  /d:sonar.cs.opencover.reportsPaths="**/coverage.opencover.xml"

dotnet build AnatelEtl.slnx --no-incremental
dotnet test AnatelEtl.slnx --no-build --collect:"XPlat Code Coverage" `
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

dotnet sonarscanner end /d:sonar.token="<TOKEN>"
```

## Trocar SQLite por SQL Server

Só o `Program.cs` e o `SqliteSchema` dependem do SQLite. O restante usa `IDbConnection`
e SQL parametrizado: troque `SqliteConnection` por `SqlConnection` e adapte o script de
criação das tabelas (`INTEGER PRIMARY KEY AUTOINCREMENT` → `INT IDENTITY`, `TEXT` → `NVARCHAR`).
