<#
  login.ps1
  Faz o login do Codex com a sua assinatura do ChatGPT e guarda em auth\auth.json.
  Precisa ser feito uma vez (e de novo só se o login expirar ou for revogado).

  Roda dentro da mesma imagem e do mesmo firewall do experimento, com o fluxo de
  "código de dispositivo": o Codex mostra um link e um código; abra o link no
  navegador do Windows, entre na sua conta do ChatGPT e digite o código.

  Uso (na pasta docker-openai):
    powershell -ExecutionPolicy Bypass -File .\login.ps1

  Se o login por código for recusado, ative "Device code authorization for Codex"
  nas configurações de segurança do ChatGPT, ou faça o login no Windows
  (npm install -g @openai/codex, depois codex login, com cli_auth_credentials_store = "file"
  no %USERPROFILE%\.codex\config.toml) e copie %USERPROFILE%\.codex\auth.json para auth\.

  O auth.json é uma senha: não mande prints dele e não suba para o GitHub (.gitignore).
#>
$ErrorActionPreference = 'Stop'
$Image   = 'tcc-openai'
$AuthDir = Join-Path $PSScriptRoot 'auth'
$Auth    = Join-Path $AuthDir 'auth.json'
New-Item -ItemType Directory -Force -Path $AuthDir | Out-Null

if (Test-Path $Auth) {
  $r = Read-Host "Já existe um login em $Auth. Fazer de novo e substituir? (s/n)"
  if ($r -notmatch '^[sS]') { Write-Host 'Nada foi alterado.'; exit 0 }
  Copy-Item $Auth "$Auth.anterior" -Force
}

$nome = 'tcc-openai-login'
$ErrorActionPreference = 'Continue'
if ((docker ps -a --format '{{.Names}}') -contains $nome) { docker rm -f $nome | Out-Null }
if ((docker network ls --format '{{.Name}}') -contains "$nome-net") { docker network rm "$nome-net" | Out-Null }
docker network create "$nome-net" | Out-Null
try {
  # A pasta auth vira o /codex do container (copiada para dentro e devolvida no final)
  & docker run --rm -it --name $nome --hostname tcc --network "$nome-net" `
    --cap-drop ALL --cap-add NET_ADMIN --cap-add NET_RAW --cap-add SETUID --cap-add SETGID `
    --security-opt no-new-privileges --pids-limit 256 `
    --mount "type=bind,source=$AuthDir,target=/out/codex" `
    $Image bash -c 'codex login --device-auth && codex login status'
  $codigo = $LASTEXITCODE
}
finally {
  if ((docker ps -a --format '{{.Names}}') -contains $nome) { docker rm -f $nome | Out-Null }
  docker network rm "$nome-net" | Out-Null
}

# Só o auth.json interessa; o resto que o Codex criou na pasta é removido
Get-ChildItem $AuthDir -Force | Where-Object { $_.Name -notin 'auth.json', 'auth.json.anterior' } |
  Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

if ($codigo -ne 0 -or -not (Test-Path $Auth)) { Write-Host 'O login não foi concluído.' -ForegroundColor Red; exit 1 }
$j = Get-Content $Auth -Raw -Encoding UTF8 | ConvertFrom-Json
if ($j.auth_mode -ne 'chatgpt' -or -not $j.tokens.refresh_token) {
  Write-Host "O auth.json não é de um login do ChatGPT com renovação (auth_mode = $($j.auth_mode))." -ForegroundColor Red
  exit 1
}
Write-Host "Login salvo em $Auth" -ForegroundColor Green
