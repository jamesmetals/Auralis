# BRIEFING TÉCNICO — OptiCore: Aplicativo de Otimização de PC com IA Integrada

## CONTEXTO GERAL

Desenvolva um aplicativo desktop Windows chamado **OptiCore** com os seguintes objetivos:
- Executar um diagnóstico profundo e exaustivo do hardware e software do PC (muito além do dxdiag)
- Usar uma IA local/integrada para analisar os dados coletados e recomendar otimizações personalizadas
- Aplicar otimizações gerais de sistema operacional automaticamente
- Aplicar otimizações específicas por game (começando pelo **Rust da Facepunch**)
- Ter uma interface elegante com perfis de otimização, controle granular de processos e mínima interação do usuário após a análise inicial

---

## STACK TECNOLÓGICA RECOMENDADA

- **Frontend/UI**: Electron + React + TailwindCSS (interface desktop rica)
- **Backend/Scripts**: Node.js (orquestrador) + PowerShell scripts (operações Windows)
- **IA integrada**: Chamadas à API da Anthropic (Claude) para interpretar diagnósticos e gerar recomendações
- **Diagnóstico de hardware**: Biblioteca `systeminformation` (Node.js) + WMI queries via PowerShell + leitura direta de registros SMART, sensores, DMI/SMBIOS
- **Persistência**: SQLite local para histórico, perfis e base de processos

> Alternativa se preferir Python: PyQt6 ou Tkinter modernizado para UI, `psutil` + `wmi` + `pywin32` para coleta, `subprocess` para PowerShell

---

## MÓDULO 1 — DIAGNÓSTICO PROFUNDO DO SISTEMA

Este é o coração do aplicativo. Deve ser meticuloso, com coleta que pode durar **5 a 15 minutos**. O objetivo é ter dados suficientes para que a IA faça recomendações verdadeiramente personalizadas.

### 1.1 — Hardware Completo (via WMI + SMBIOS + PowerShell)

```
CPU:
  - Modelo exato, fabricante, geração
  - Número de núcleos físicos e lógicos
  - Distinguir P-cores e E-cores (Intel 12th gen+)
  - Frequência base e boost
  - Tamanho e velocidade do cache L1, L2, L3 (crítico para Rust)
  - TDP e limites de potência (PL1/PL2 via HWINFO/WMI)
  - Suporte a tecnologias: AVX, AVX2, AVX-512, SSE4

GPU:
  - Modelo exato, VRAM total e tipo (GDDR5/6/6X)
  - Driver atual instalado
  - Resolução e taxa de atualização do monitor conectado
  - Suporte a DirectX 12 Ultimate, Shader Model
  - Estado do HAGS (Hardware Accelerated GPU Scheduling)
  - Estado do ReBAR / Smart Access Memory

RAM:
  - Capacidade total, número de slots ocupados
  - Frequência real rodando (MHz) vs frequência XMP/EXPO configurada
  - Timings detalhados (CL, tRCD, tRP, tRAS)
  - Dual channel ativo ou não
  - Fabricante e modelo dos módulos (via SPD)

Armazenamento:
  - Todos os discos: modelo, tipo (HDD/SSD SATA/NVMe)
  - Disco onde o Windows está instalado
  - Interface: SATA II/III, NVMe PCIe 3.0/4.0/5.0
  - Saúde SMART: reallocated sectors, pending sectors, temperatura
  - Velocidade de leitura/escrita sequencial estimada
  - Fragmentação (para HDDs)

Placa-mãe:
  - Fabricante, modelo, versão do BIOS
  - Chipset
  - Data do BIOS instalado vs data do BIOS mais recente disponível (webscraping do site do fabricante)

Rede:
  - Adaptador de rede: modelo, fabricante
  - Estado do "Interrupt Moderation" e "Green Ethernet"
  - Tipo de conexão: Ethernet ou Wi-Fi (e qualidade do sinal se Wi-Fi)
  - Ping médio para servidores de referência (8.8.8.8, 1.1.1.1)

Fonte de Alimentação:
  - Detectar via WMI/fabricante quando possível
  - Consumo atual estimado por componente
```

### 1.2 — Software e Sistema Operacional

```
Windows:
  - Versão exata do Windows (build, release)
  - Versão do DirectX instalada
  - Versão do .NET Framework e .NET Core
  - Visual C++ Redistributables instalados
  - Estado do Game Mode, Game Bar, DVR (Xbox)
  - Plano de energia ativo
  - Configuração de memória virtual (pagefile)
  - Integridade do sistema: últimas verificações de SFC/DISM

Serviços em execução:
  - Lista completa com: nome, displayName, status, startType, PID, uso de CPU/RAM
  - Classificar cada serviço como: [ESSENCIAL / NÃO ESSENCIAL / PERIGOSO DESABILITAR]
  - Usar base de dados interna com descrições conhecidas

Processos de startup:
  - Itens no Registro (Run/RunOnce)
  - Itens na pasta Startup
  - Tarefas agendadas relevantes
  - Impacto no boot de cada item

Programas instalados:
  - Lista completa com versão e data de instalação
  - Identificar softwares de periféricos (Razer Synapse, Logitech G Hub, ASUS Armory Crate, etc.)
  - Identificar antivírus/antimalware ativos
  - Identificar apps com sobreposição ativa (Discord, Steam, GeForce Experience, MSI Afterburner)

Drivers críticos:
  - Driver de GPU (versão, data)
  - Driver de áudio
  - Drivers de chipset (AMD/Intel)
  - Identificar drivers desatualizados ou com versão problemática conhecida
```

### 1.3 — Análise de Desempenho em Tempo Real (coleta de 60 segundos em idle)

```
- Uso médio/pico de CPU por núcleo
- Uso de RAM e composição da memória (em uso / standby / livre)
- Temperatura de CPU e GPU em idle
- Uso de disco (leitura/escrita por segundo)
- Latência de interrupções (DPC latency via LatencyMon integrado ou equivalente)
- Processos com maior consumo de CPU e RAM durante o período
```

### 1.4 — Ferramenta de Diagnóstico Profundo Integrada

Integrar ou automatizar ferramentas que fazem análise minuciosa semelhante a:
- **WinAudit** (open source, pode ser executado silenciosamente com output XML)
- **Belarc Advisor** (executar e parsear output HTML)
- **ESET SysInspector** (snapshot completo: processos, serviços, registro, conexões de rede, arquivos de sistema)
- **HWiNFO64** (modo portable/silent para coleta de sensores)

O programa deve executar essas ferramentas em background, parsear os resultados e consolidar tudo em um único objeto JSON de diagnóstico que será enviado à IA.

---

## MÓDULO 2 — IA DE ANÁLISE E RECOMENDAÇÃO

Após a coleta, enviar o JSON de diagnóstico para a **API da Anthropic (Claude)** com um prompt de sistema especializado.

### 2.1 — Prompt de Sistema para a IA

```
Você é um especialista em otimização de performance de PCs para jogos. 
Receberá um JSON com diagnóstico completo de um PC. 
Analise CADA campo e gere recomendações personalizadas e priorizadas.

Para cada recomendação retorne um objeto JSON com:
{
  "id": "unique_id",
  "categoria": "sistema|gpu|cpu|ram|rede|game_rust|...",
  "titulo": "Nome curto da otimização",
  "descricao": "Explicação do que faz e por que se aplica a ESTE hardware específico",
  "impacto_estimado": "alto|medio|baixo",
  "risco": "seguro|moderado|avancado",
  "aplicavel": true/false,
  "motivo_nao_aplicavel": "se false, explicar por que não se aplica a este hardware",
  "script_powershell": "script pronto para executar (se aplicável)",
  "valor_atual": "valor detectado no sistema",
  "valor_recomendado": "valor que será aplicado",
  "reversivel": true/false,
  "comando_reverter": "script para desfazer"
}
```

### 2.2 — Lógica de personalização

A IA deve analisar especificamente:
- Se o processador tem cache L3 grande → priorizar otimizações de latência de memória
- Se GPU suporta HAGS → recomendar ativação
- Se RAM não está rodando em XMP → alertar e sugerir ativação no BIOS
- Se está em Wi-Fi → recomendar otimizações de rede específicas para wireless
- Se SSD NVMe está em slot PCIe 3.0 sendo limitado → informar
- Se DPC latency alta → identificar driver causador

---

## MÓDULO 3 — OTIMIZAÇÃO GERAL DO SISTEMA

Scripts PowerShell organizados por categoria, todos reversíveis:

### 3.1 — Plano de Energia

```powershell
# Ativar Ultimate Performance (se não existir, criar)
$scheme = powercfg -list | Select-String "Ultimate"
if (-not $scheme) {
    powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61
}
$guid = (powercfg -list | Select-String "Ultimate" | ForEach-Object { $_ -match '[0-9a-f-]{36}' | Out-Null; $Matches[0] })
powercfg -setactive $guid

# Desabilitar Core Parking via registro
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\0cc5b647-c1df-4637-891a-dec35c318583" -Name "ValueMax" -Value 0
```

### 3.2 — Memória e Responsividade

```powershell
# SystemResponsiveness - priorizar games sobre background tasks
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" -Name "SystemResponsiveness" -Value 0

# Win32PrioritySeparation - priorizar foreground
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" -Name "Win32PrioritySeparation" -Value 38

# NetworkThrottlingIndex - desativar throttling de rede
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" -Name "NetworkThrottlingIndex" -Value 0xffffffff
```

### 3.3 — Serviços Para Desabilitar (com verificação antes)

```
Lista base de serviços não essenciais para gaming:
- DiagTrack (Connected User Experiences and Telemetry)
- dmwappushservice (WAP Push Message Routing)
- WSearch (Windows Search) — opcional, impacta indexação
- Print Spooler (se não usa impressora)
- Fax
- MapsBroker (Downloaded Maps Manager)
- RetailDemo
- wisvc (Windows Insider Service)
- XblAuthManager, XblGameSave, XboxGipSvc, XboxNetApiSvc (se não usa Xbox features)
- SysMain (Superfetch) — controverso, testar por hardware
```

O programa deve verificar o estado atual antes de desabilitar e salvar estado original para reversão.

### 3.4 — GPU e DirectX

```powershell
# Ativar HAGS via Registro (requer reinício)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" -Name "HwSchMode" -Value 2

# Shader Cache como ilimitado (NVIDIA)
Set-ItemProperty -Path "HKLM:\SOFTWARE\NVIDIA Corporation\Global\NVTweak" -Name "Coolbits" -Value 8 -ErrorAction SilentlyContinue

# Game Mode
Set-ItemProperty -Path "HKCU:\Software\Microsoft\GameBar" -Name "AutoGameModeEnabled" -Value 1

# Desabilitar Game Bar e DVR
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR" -Name "AppCaptureEnabled" -Value 0
Set-ItemProperty -Path "HKCU:\System\GameConfigStore" -Name "GameDVR_Enabled" -Value 0
```

### 3.5 — Rede para Baixa Latência

```powershell
# Desabilitar Nagle Algorithm
$adapters = Get-NetAdapter | Where-Object { $_.Status -eq "Up" }
foreach ($adapter in $adapters) {
    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\$($adapter.InterfaceGuid)"
    Set-ItemProperty -Path $regPath -Name "TcpAckFrequency" -Value 1 -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $regPath -Name "TCPNoDelay" -Value 1 -ErrorAction SilentlyContinue
}

# Desabilitar Interrupt Moderation e Green Ethernet via NetAdapter
Get-NetAdapter | Set-NetAdapterAdvancedProperty -RegistryKeyword "*InterruptModeration" -RegistryValue 0 -ErrorAction SilentlyContinue
Get-NetAdapter | Set-NetAdapterAdvancedProperty -RegistryKeyword "*EEE" -RegistryValue 0 -ErrorAction SilentlyContinue
```

### 3.6 — Limpeza de Memória RAM (Standby List)

```
Integrar EmptyStandbyList.exe (ferramenta da Sysinternals/Microsoft)
Executar automaticamente quando memória livre < 20%
Opção de executar antes de iniciar um game
```

---

## MÓDULO 4 — OTIMIZAÇÃO ESPECÍFICA: RUST (Facepunch Studios)

Rust é desenvolvido na engine Unity e tem comportamentos muito específicos que afetam performance.

### 4.1 — Parâmetros de Launch Options (Steam)

O programa deve modificar automaticamente os launch options do Rust no Steam via registro ou arquivo de configuração do Steam:

```
Arquivo: Steam/userdata/<steamid>/config/localconfig.vdf
Ou via: HKCU:\Software\Valve\Steam -> apps -> 252490

Launch options recomendados (personalizados por hardware detectado):
-high                          → Define processo com prioridade alta no Windows
-maxMem=<RAM_DETECTADA_MB>     → Ex: -maxMem=12288 para 16GB (deixar 4GB pro SO)
-malloc=system                 → Usa allocator do sistema em vez do Unity (testar)
-force-feature-level-11-0      → Força DirectX 11 feature level (mais estável)
-nolog                         → Desabilita logging para reduzir I/O
-no-browser                    → Desabilita browser integrado do Chromium
-headlerp 100                  → Reduz interpolação de cabeça (movimento mais responsivo)
```

**Lógica de personalização:**
- Se RAM >= 32GB: `-maxMem=24576`
- Se RAM = 16GB: `-maxMem=12288`
- Se RAM = 8GB: `-maxMem=6144` + alertar que 8GB é insuficiente para Rust moderno
- Se CPU AMD Ryzen X3D: não aplicar `-high` (pode conflitar com agendador do Windows 11)

### 4.2 — Comandos de Console In-Game (F1) — Salvar em arquivo CFG

```
Localização: %APPDATA%/Roaming/Facepunch/Rust/cfg/client.cfg
Ou via: steam launch -> executar após iniciar

gc.buffer 2048          → Buffer do Garbage Collector do Unity (CRÍTICO)
                          Para 16GB RAM: gc.buffer 2048
                          Para 32GB RAM: gc.buffer 4096
                          Reduz drasticamente os "freezes" de 1-2 segundos

graphics.drawdistance 1500     → Ajustar por GPU detectada (RTX 3060=1500, GTX 1060=800)
shadowquality 0                → Desabilitar sombras dinâmicas (maior ganho de FPS)
shadow.distance 0              → Distância das sombras = zero
grass.on false                 → Desabilitar grama (ganho significativo de FPS)
graphics.grassshadows false    → Sombras de grama off
occlusion true                 → Ativar occlusion culling
fps.limit <valor>              → Limitar FPS (recomendado: Hz do monitor + 10)
gfx.ssao false                 → Desativar Ambient Occlusion
graphics.damage false          → Desativar deformação por dano
graphics.branding false        → Remove marca d'água de debug
audio.master 0.8               → Volume geral (ajustar ao gosto)
```

**Lógica de personalização por GPU:**
- GPU High-End (RTX 4070+, RX 7800 XT+): manter qualidade moderada, focar em frametime consistency
- GPU Mid-Range (RTX 3060, RX 6600): balance performance/qualidade
- GPU Low-End (GTX 1060, RX 580): agressivo em desativar efeitos

### 4.3 — Otimizações Específicas de Processo para Rust

```powershell
# Após Rust iniciar, aplicar automaticamente:
$rustProcess = Get-Process "RustClient" -ErrorAction SilentlyContinue
if ($rustProcess) {
    # Afinidade de CPU: colocar em P-cores (se Intel 12th+)
    # Para Intel Core i7-12700K: P-cores = 0-19, E-cores = 20-27
    # Detectar topology via WMI e calcular máscara correta
    
    # Prioridade
    $rustProcess.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::High
    
    # Mover processos de background para E-cores automaticamente
    $backgroundProcesses = @("Discord", "chrome", "msedge", "OneDrive", "Teams")
    foreach ($proc in $backgroundProcesses) {
        $p = Get-Process $proc -ErrorAction SilentlyContinue
        if ($p) {
            # Definir afinidade apenas para E-cores
            $p.ProcessorAffinity = [IntPtr]::new(0xFF000000) # ajustar máscara por CPU
        }
    }
}
```

### 4.4 — Monitor de Rust em Tempo Real

```
Quando Rust estiver rodando, o OptiCore deve mostrar overlay ou janela lateral com:
- FPS atual e FPS médio
- Frametime e 1% Low / 0.1% Low
- Temperatura CPU e GPU
- Uso de VRAM
- Ping ao servidor conectado
- Alerta se GC spikes detectados (frametime > 50ms = possível GC pause)
```

---

## MÓDULO 5 — INTERFACE DO USUÁRIO (UI/UX)

### 5.1 — Telas Principais

**Tela 1: Dashboard / Home**
```
- Status geral do sistema (score de otimização 0-100)
- Perfil ativo atualmente
- Últimas otimizações aplicadas
- Botão: "Executar Diagnóstico Completo"
- Indicadores rápidos: CPU%, RAM%, GPU%, Temp, FPS se game rodando
```

**Tela 2: Diagnóstico**
```
- Progress bar com etapas do diagnóstico (Coleta de Hardware... Análise de Serviços... Teste de Rede... IA Analisando...)
- Log em tempo real do que está sendo coletado
- Resumo do hardware detectado após conclusão
- Relatório exportável (JSON/HTML/PDF)
```

**Tela 3: Otimizações — Geral**
```
- Listagem de todas as otimizações disponíveis
- Filtros: categoria, impacto, risco
- Para cada item: toggle on/off + ícone de risco + descrição + valor atual vs recomendado
- Badges: "Recomendado pela IA para seu hardware"
- Botão aplicar selecionadas
```

**Tela 4: Games**
```
- Lista de games instalados (detectados automaticamente via Steam/registros)
- Para cada game: status de otimização (otimizado/pendente/não suportado)
- Clicar em Rust → abrir painel de otimizações específicas
- Preview dos launch options que serão aplicados
- Botão "Aplicar e Lançar Game"
```

**Tela 5: Perfis**
```
Perfis disponíveis:
  [PERFORMANCE MÁXIMA]  — Aplica tudo: desabilita serviços, mata processos, Ultimate Performance
  [BALANCEADO]          — Otimizações seguras sem desabilitar recursos do Windows
  [CUSTOM]              — Usuário seleciona cada item individualmente
  [MODO NORMAL]         — Reverte todas as alterações para estado original

Criar/salvar perfis customizados
Exportar/importar perfis (compartilhar com amigos)
```

**Tela 6: Processos**
```
- Lista de todos os processos em execução
- Classificação automática: [ESSENCIAL / DESNECESSÁRIO / PERIFÉRICO / DESCONHECIDO]
- Uso de CPU e RAM por processo
- Checkbox para incluir no "matar antes do game"
- Salvar lista customizada por perfil
- Identificar automaticamente: Razer Synapse, Logitech G Hub, Discord, etc.
```

**Tela 7: Monitoramento**
```
- Gráficos em tempo real: CPU, GPU, RAM, temperaturas, frametime
- Histórico de sessões
- Alertas configuráveis (throttling térmico, uso de RAM > X%)
```

### 5.2 — Estética Visual

```
Tema: Dark mode industrial/tech
Cores: Fundo #0D0D0D, cards #141414, accent #00D4FF (ciano), 
       warning #FF6B35, danger #FF3333, success #00FF88
Tipografia: Display = "Orbitron" ou "Share Tech Mono", Body = "Inter"
Animações: Transições suaves, scan lines sutis nos headers, 
           loading animado durante diagnóstico
```

---

## MÓDULO 6 — GESTÃO DE SEGURANÇA E REVERSÃO

```
REGRA CRÍTICA: Antes de qualquer modificação, o programa deve:
1. Criar ponto de restauração do Windows automaticamente
2. Salvar snapshot do registro afetado em arquivo JSON local
3. Registrar em log: o que mudou, quando, valor anterior, valor novo

Sistema de reversão:
- "Desfazer última sessão de otimização" — reverte tudo da última vez
- "Restaurar configurações originais" — volta ao estado do primeiro diagnóstico
- Por item individual: toggle simplesmente reverte o valor salvo

Armazenamento:
- SQLite: profiles.db com tabelas: snapshots, applied_optimizations, process_lists, game_configs
- Arquivo de log: logs/optimization_YYYYMMDD.log
```

---

## MÓDULO 7 — AUTOMAÇÃO DE LAUNCH (Game Launcher)

```
Fluxo ao clicar "Jogar Rust":
1. Verificar se está no perfil correto (se não, perguntar se quer ativar)
2. Matar processos marcados como "desnecessários" pelo usuário
3. Limpar Standby Memory
4. Aplicar prioridade de processo pré-configurada
5. Verificar e aplicar launch options no Steam (se mudaram)
6. Lançar o game via Steam (steam://rungameid/252490)
7. Aguardar processo iniciar
8. Aplicar afinidade de CPU ao processo do game
9. Mover processos de background para E-cores
10. Iniciar monitoramento em segundo plano
11. Ao fechar o game: reverter processos, restaurar estado normal
```

---

## ESTRUTURA DE ARQUIVOS DO PROJETO

```
opticore/
├── src/
│   ├── main/                     # Electron main process
│   │   ├── index.js              # Entry point
│   │   ├── ipc-handlers.js       # IPC entre UI e backend
│   │   └── auto-updater.js       # Atualizações automáticas
│   ├── renderer/                 # React frontend
│   │   ├── components/
│   │   ├── pages/
│   │   ├── store/                # Estado global (Zustand/Redux)
│   │   └── App.jsx
│   ├── core/
│   │   ├── diagnostics/
│   │   │   ├── hardware.js       # Coleta de hardware via WMI
│   │   │   ├── software.js       # Serviços, processos, programas
│   │   │   ├── network.js        # Análise de rede
│   │   │   ├── performance.js    # Coleta de métricas em tempo real
│   │   │   └── tools-runner.js   # Orquestra ferramentas externas
│   │   ├── ai/
│   │   │   ├── analyzer.js       # Integração com API Anthropic
│   │   │   └── prompt-builder.js # Monta prompts com dados do diagnóstico
│   │   ├── optimizations/
│   │   │   ├── general/
│   │   │   │   ├── power.ps1
│   │   │   │   ├── memory.ps1
│   │   │   │   ├── services.ps1
│   │   │   │   ├── gpu.ps1
│   │   │   │   └── network.ps1
│   │   │   └── games/
│   │   │       └── rust/
│   │   │           ├── launch-options.js
│   │   │           ├── client-cfg.js
│   │   │           └── process-manager.js
│   │   ├── profiles/
│   │   │   ├── manager.js        # CRUD de perfis
│   │   │   └── presets.js        # Perfis pré-definidos
│   │   ├── monitoring/
│   │   │   ├── realtime.js       # Coleta contínua de métricas
│   │   │   └── overlay.js        # Overlay in-game
│   │   └── safety/
│   │       ├── restore-point.js  # Cria pontos de restauração
│   │       ├── snapshot.js       # Salva estado do registro
│   │       └── revert.js         # Reverte alterações
│   └── db/
│       ├── schema.sql
│       ├── migrations/
│       └── queries.js
├── scripts/                      # PowerShell scripts standalone
├── tools/                        # Ferramentas externas (HWiNFO, WinAudit, etc.)
├── package.json
└── electron-builder.config.js
```

---

## PRIORIDADE DE DESENVOLVIMENTO (FASES)

**Fase 1 — MVP (Diagnóstico + Aplicar Otimizações Básicas)**
1. Diagnóstico de hardware com `systeminformation` + WMI
2. Integração com API da Anthropic para análise
3. Scripts PowerShell para otimizações gerais
4. UI básica com Electron + React
5. Sistema de reversão/snapshot

**Fase 2 — Rust + Perfis**
1. Módulo de otimização do Rust (launch options + client.cfg)
2. Sistema de perfis
3. Gerenciador de processos com classificação automática
4. Launcher integrado com automação completa

**Fase 3 — Monitoramento + Diagnóstico Profundo**
1. Monitoramento em tempo real com gráficos
2. Integração com ferramentas externas (HWiNFO64, WinAudit)
3. Overlay in-game
4. Análise de DPC latency integrada

**Fase 4 — Polimento + Expansão**
1. Suporte a mais games
2. Exportar/importar perfis
3. Auto-updater
4. Base de dados de processos expandida (crowdsourced)

---

## OBSERVAÇÕES IMPORTANTES PARA O AGENTE

1. **Segurança**: Todas as modificações de registro e serviços devem ter reversão garantida. Criar restore point SEMPRE antes de aplicar.

2. **Privilégios**: O app precisa rodar como Administrador. Implementar UAC prompt no início e manifest de elevação.

3. **Diagnóstico demorado é desejável**: A coleta deve ser completa mesmo que demore 10-15 minutos. Mostrar progresso detalhado para o usuário entender o que está sendo analisado.

4. **API Key da Anthropic**: Implementar tela de configuração onde usuário insere sua API key. Armazenar de forma segura no sistema (Windows Credential Manager).

5. **Não se limitar às otimizações listadas**: As listas acima são ponto de partida. A IA deve poder identificar e sugerir otimizações não previstas com base nos dados coletados.

6. **Perfil por game deve ser isolado**: Ativar/desativar o perfil de Rust não deve afetar configurações de outros games ou o perfil geral do sistema.

7. **O app deve detectar novos games automaticamente** ao escanear a biblioteca Steam e sugerir quando há otimizações disponíveis para eles.
