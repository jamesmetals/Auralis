# Pesquisa Rust - Otimizacoes e ganho de FPS

## Objetivo
- Consolidar uma base de conhecimento persistida para o modulo de Rust.
- Separar o que e regra forte, o que e condicional ao hardware e o que costuma ser placebo.
- Dar insumo para a IA recomendar ajustes do jogo, Windows e driver somente quando fizer sentido para o PC detectado.

## Escopo da pesquisa
- Fontes oficiais da Facepunch.
- Guias de comunidade na Steam.
- Discussao recorrente em Reddit.
- Sinal de videos do YouTube via guias citados pela comunidade e debates em torno desses videos.

## Fontes consolidadas
- Facepunch Support - GPU Skinning Test  
  https://support.facepunchstudios.com/hc/en-us/articles/23006747642525-GPU-Skinning-Test
- Facepunch Support - Managing your Virtual Memory/Pagefile  
  https://support.facepunchstudios.com/hc/en-us/articles/29737736165149-Managing-your-Virtual-Memory-Pagefile
- Facepunch Support - Verify Rust Files  
  https://support.facepunchstudios.com/hc/en-us/articles/360008398478
- Facepunch Support - Failed to initialize ReShade  
  https://support.facepunchstudios.com/hc/en-us/articles/24444483513373-Failed-to-initialize-Reshade
- Steam Community Guide - [2026] RUST FPS+  
  https://steamcommunity.com/sharedfiles/filedetails/?id=3081809156
- Reddit r/playrust - FPS issues  
  https://www.reddit.com/r/playrust/comments/1kyoavk/fps_issues/
- Reddit r/playrust - Stuttering issue on medium-end PC, no solution  
  https://www.reddit.com/r/playrust/comments/17ocpl2/stuttering_issue_on_mediumend_pc_no_solution/

## Leitura sintetica
- Rust continua muito sensivel a CPU, cache e RAM, principalmente em servidores cheios, bases densas e situacoes com muitas entidades.
- O jogo costuma escalar melhor com CPU/cache forte e memoria bem configurada do que com tweaks agressivos de GPU apenas.
- 16 GB ainda funcionam, mas 32 GB aparecem repetidamente como ponto de conforto para reduzir stutter e melhorar consistencia.
- XMP/EXPO ou memoria fora do perfil certo aparecem varias vezes como causa de desempenho anormalmente baixo.
- GPU Skinning nao e remedio universal, mas merece aparecer como teste reversivel quando o sintoma bate com queda forte ao segurar armas ou quando o jogo esta estranho para o nivel do hardware.
- Pagefile automatico e espaco livre em SSD/NVMe continuam relevantes para Rust, sobretudo em memoria mais apertada.

## Regras fortes
- Priorizar diagnostico de CPU, cache, RAM e frametime antes de presumir gargalo exclusivo de GPU.
- Validar memoria: capacidade total, carga no momento, pagefile e perfil XMP/EXPO.
- Cortar ruido de fundo: navegador, overlay, gravacao, RGB, launchers duplicados e qualquer app pesado.
- Trabalhar com launch options curtas e justificadas.
- Diferenciar ajuste reversivel de upgrade real de hardware.

## Regras condicionais

### Maquinas com 16 GB ou menos
- Pagefile automatico ganha prioridade.
- Background pesado e overlay viram alvo imediato.
- A IA deve evitar recomendar presets exageradamente altos e deve pesar mais stutter do que FPS medio isolado.

### CPUs X3D
- Evitar tratar prioridade alta e tweaks agressivos como obrigatorios.
- O proprio cache ja entrega parte importante do ganho; o foco passa a ser estabilidade, RAM e sintoma real.

### GPUs NVIDIA
- Faz sentido revisar painel/driver, gerenciamento de energia, overlays e filtros.
- Nao transformar NVIDIA Profile Inspector em resposta padrao.

### GPUs AMD
- Faz sentido revisar recursos do Adrenalin que adicionam overlay, gravacao, boost ou tuning automatico.
- Nao recomendar pacote de tweaks NVIDIA.

### Sintoma de queda anormal ao segurar armas ou microstutter estranho
- Elevar GPU Skinning como teste reversivel.
- Se falhar, voltar ao branch padrao e seguir diagnostico normal.

## Mitos e placebo que a IA deve evitar
- "Tudo no low sempre da mais FPS."
- "Empilha launch options antigas e o FPS sobe."
- "Problema de Rust e sempre GPU."
- "Qualquer video de YouTube com 20 tweaks vale para todo PC."
- "ReShade e filtros externos ajudam no desempenho."

## Como a IA deve usar isso
- Ler o diagnostico real do PC antes de citar qualquer regra.
- Cruzar CPU, GPU, RAM total e carga de memoria com a base persistida.
- Promover apenas regras compativeis com o hardware atual.
- Rotular:
  - ajuste imediato
  - teste reversivel
  - triagem/diagnostico
  - upgrade futuro
- Evitar recomendar algo que a propria base pesquisada classifica como incompativel, experimental ou placebo.

## Limite atual da pesquisa
- O acesso direto a comentarios de YouTube nem sempre e consistente via automacao.
- Por isso, o sinal de YouTube nesta consolidacao entrou principalmente por guias referenciados pela comunidade e por discussao em Reddit/Steam sobre o que realmente funcionou ou falhou.
