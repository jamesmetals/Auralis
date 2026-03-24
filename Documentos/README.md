# Auralis

Aplicativo Windows focado em personalizacao, automacao e operacoes controladas do sistema, com base nativa em .NET e autorizacao por RBAC.

## O que ja esta estruturado

- solucao em camadas para dominio, aplicacao, infraestrutura, integracao Windows e desktop
- modelo RBAC inicial com `admin`, `operator` e `user`
- servico de conversao de imagem para `.ico`
- workflow para aplicar icone em pasta e persistir historico por usuario
- integracao inicial com Explorer via verbo registrado em `HKCU`
- servico generico para alteracoes de Registry com trilha de auditoria
- catalogo inicial de features de Windows baseadas em Registry
- painel desktop inicial para habilitar/desabilitar features e consultar auditoria local
- dashboard com modulo JB GameBooster integrado ao menu lateral
- catalogo inicial de otimizacoes gerais para gaming com aplicacao por item ou em lote
- toggle opcional de ponto de restauracao antes de alteracoes de sistema
- reversao da ultima sessao aplicada pelo JB GameBooster
- analise local do JB GameBooster via Ollama, com endpoint e modelo configuraveis
- teste explicito de conexao com Ollama, incluindo dica de `ollama pull` quando o modelo configurado nao estiver baixado
- painel inicial de Rust com preset de launch options, comandos sugeridos de `client.cfg` e analise local dedicada
- preview do resultado do icone antes de salvar, com corte manual por coordenadas
- armazenamento protegido com DPAPI preparado para sessao e licenca local

## O que ainda falta

- autenticacao real e backend remoto
- sincronizacao em nuvem do historico
- licenciamento e vinculo por dispositivo
- crop visual por arrastar e ajuste fino mais amigavel
- integracao empacotada para o menu moderno do Windows 11
- tela dedicada por game no GameBooster (comecando por Rust)
- diagnostico profundo, monitoramento em tempo real e automacao de launch options

## Arquitetura

- `src/MelhorWindows.Domain`: entidades e regras centrais
- `src/MelhorWindows.Application`: contratos e casos de uso
- `src/MelhorWindows.Infrastructure`: armazenamento local, imagem e estado
- `src/MelhorWindows.WindowsIntegration`: Explorer, `desktop.ini` e Registry
- `src/MelhorWindows.Desktop`: app desktop WPF

## Build

Pre requisitos locais:

- Windows 10/11
- .NET 8 SDK

Com o SDK instalado, a sequencia esperada e:

```powershell
dotnet restore
dotnet build MelhorWindows.sln -c Release
dotnet test MelhorWindows.sln
```

Quando o Auralis ja estiver instalado em `%LOCALAPPDATA%\Programs\Auralis`, o build do projeto desktop sincroniza automaticamente a instalacao local com a versao compilada mais recente. Isso fecha o ciclo de desenvolvimento sem precisar reinstalar manualmente a cada ajuste.

Para desativar essa sincronizacao automatica em um build especifico:

```powershell
dotnet build src\MelhorWindows.Desktop\MelhorWindows.Desktop.csproj -c Release -p:AutoSyncInstalledAuralis=false
```

Validado neste ambiente:

```powershell
dotnet build MelhorWindows.sln -c Release
dotnet test MelhorWindows.sln -c Release --no-build
dotnet publish src\MelhorWindows.Desktop\MelhorWindows.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish\MelhorWindows.Desktop
```

## Instalador

Geracao do instalador local:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

Saida esperada:

```powershell
.\installer\dist\Auralis-Setup.exe
.\installer\dist\Auralis.Payload.zip
.\installer\dist\Install-Auralis.ps1
.\installer\dist\Auralis-Installer.zip
```

Instalacao padrao:

- `%LOCALAPPDATA%\Programs\Auralis`
- atalho na area de trabalho
- atalho no menu Iniciar
- registro do menu de contexto em `HKCU\Software\Classes\Directory\shell\Auralis.ChangeFolderIcon`
- bundle pronto para distribuicao em `.\installer\dist\Auralis-Installer.zip`

## MSIX

Geracao do pacote MSIX:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\msix\Build-MSIX.ps1
```

Arquivos gerados:

```powershell
.\installer\msix\dist\Auralis_*.msix
.\installer\msix\dist\Auralis.Dev.cer
.\installer\msix\dist\Install-Auralis-MSIX.ps1
.\installer\msix\dist\Uninstall-Auralis-MSIX.ps1
.\installer\msix\dist\Install-Auralis-MSIX.cmd
.\installer\msix\dist\Uninstall-Auralis-MSIX.cmd
.\installer\msix\dist\Auralis-MSIX.zip
```

Instalacao local do MSIX:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\msix\dist\Install-Auralis-MSIX.ps1
```

Atalho mais pratico para instalar com elevacao:

```powershell
.\installer\msix\dist\Install-Auralis-MSIX.cmd
```

## Execucao inicial

O app pode receber o caminho da pasta como argumento:

```powershell
Auralis.exe "C:\Caminho\Da\Pasta"
```

Argumentos auxiliares:

```powershell
Auralis.exe --register-folder-verb
Auralis.exe --unregister-folder-verb
```

Launcher rapido na area de trabalho:

```powershell
C:\Users\james\OneDrive\Área de Trabalho\Auralis.bat
```

Variavel util para desenvolvimento:

```powershell
$env:AURALIS_ACTIVE_ROLES = "admin"
```

Para usar a IA local no JB GameBooster:

```powershell
ollama serve
ollama pull gemma3:4b
```

O dashboard assume por padrao o endpoint `http://localhost:11434` e o modelo `gemma3:4b`, mas ambos podem ser alterados na tela do modulo.

## Seguranca

- a UI nao e a autoridade final de permissao
- roles e permissoes ja estao separadas no nucleo
- alteracoes sensiveis de Registry exigem permissao propria
- integracao com Explorer exige permissao propria
- features de Windows baseadas em Registry exigem permissao propria
- estado sensivel local deve ir para armazenamento protegido por usuario
- o proximo passo e mover identidade, licenca e sincronizacao para servidor
