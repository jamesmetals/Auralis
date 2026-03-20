# MelhorWindows

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
- preview do resultado do icone antes de salvar, com corte manual por coordenadas
- armazenamento protegido com DPAPI preparado para sessao e licenca local

## O que ainda falta

- autenticacao real e backend remoto
- sincronizacao em nuvem do historico
- licenciamento e vinculo por dispositivo
- crop visual por arrastar e ajuste fino mais amigavel
- integracao empacotada para o menu moderno do Windows 11

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
.\installer\dist\MelhorWindows-Setup.exe
.\installer\dist\MelhorWindows.Payload.zip
.\installer\dist\Install-MelhorWindows.ps1
.\installer\dist\MelhorWindows-Installer.zip
```

Instalacao padrao:

- `%LOCALAPPDATA%\Programs\MelhorWindows`
- atalho na area de trabalho
- atalho no menu Iniciar
- registro do menu de contexto em `HKCU\Software\Classes\Directory\shell\MelhorWindows.ChangeFolderIcon`
- bundle pronto para distribuicao em `.\installer\dist\MelhorWindows-Installer.zip`

## Execucao inicial

O app pode receber o caminho da pasta como argumento:

```powershell
MelhorWindows.Desktop.exe "C:\Caminho\Da\Pasta"
```

Argumentos auxiliares:

```powershell
MelhorWindows.Desktop.exe --register-folder-verb
MelhorWindows.Desktop.exe --unregister-folder-verb
```

BAT criado na area de trabalho:

```powershell
C:\Users\james\OneDrive\Área de Trabalho\MelhorWindows.bat
```

Variavel util para desenvolvimento:

```powershell
$env:MELHORWINDOWS_ACTIVE_ROLES = "admin"
```

## Seguranca

- a UI nao e a autoridade final de permissao
- roles e permissoes ja estao separadas no nucleo
- alteracoes sensiveis de Registry exigem permissao propria
- integracao com Explorer exige permissao propria
- features de Windows baseadas em Registry exigem permissao propria
- estado sensivel local deve ir para armazenamento protegido por usuario
- o proximo passo e mover identidade, licenca e sincronizacao para servidor
