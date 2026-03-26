# Historico de Commits

Registro resumido dos commits atuais do projeto, com foco no que foi alterado em cada etapa.

## 745f142 - 2026-03-26 - feat: add research-driven Rust optimization knowledge
- adicionou uma base persistida de conhecimento para Rust em JSON, com regras classificadas por confianca, tipo de ajuste e condicoes por CPU, RAM, GPU/vendor e carga de memoria.
- integrou essa base ao prompt da IA para que as recomendacoes do Rust deixem de ser genericas e passem a respeitar o hardware detectado no snapshot atual.
- criou a documentacao `Documentos/PESQUISA_RUST_OTIMIZACOES_2026.md` e a skill reutilizavel `SKILL_PESQUISA_OTIMIZACAO_DE_JOGOS.md` para repetir o mesmo fluxo em outros games.

## aff7577 - 2026-03-26 - refactor: refine dashboard copy and Rust panel UX
- refinou a hierarquia textual, os rotulos e a linguagem do dashboard para ficar mais orientada a produto e menos tecnica.
- ajustou a leitura do painel de Rust, melhorando o equilibrio visual dos blocos de resumo, acoes e diagnostico.
- preparou o terreno para a nova rodada de pesquisa por jogo sem mudar a logica central do modulo.

## f12eac7 - 2026-03-26 - feat: hide AI internals from UI and add designer skill
- removeu da interface principal detalhes tecnicos de IA que nao sao pertinentes ao usuario final, como referencias a modelo, provider, chave e configuracao operacional.
- refinou a aba de analise inteligente e o painel de Rust com linguagem mais orientada a beneficio, resumo e diagnostico.
- adicionou `SKILL_AGENTE_DESIGNER.md` na raiz como base reutilizavel para agentes de design com ou sem link de inspiracao visual.

## 354ab89 - 2026-03-26 - feat: refactor desktop UI with Pico-inspired design
- refez o design system do desktop com tipografia Figtree, acento laranja, superficies arredondadas, sombras suaves e fundo em camadas inspirado na referencia Pico.
- reorganizou estilos de botoes, campos, navegacao lateral, cards, overlays e telas do GameBooster sem alterar a logica de negocio.
- limpou mensagens e estados remanescentes da fase Ollama para o fluxo atual com Google Gemini.

## 5df9da6 - 2026-03-26 - feat: migrate JB GameBooster AI flow to Gemini
- trocou o provedor de IA do JB GameBooster para Google Gemini.
- adicionou `GoogleGeminiLocalAiGameBoosterService` e ajustou diagnostico, workflow, configuracao e UI para o novo fluxo.

## 20fb614 - 2026-03-24 - fix: restore point progress output bug and local AI background autostart
- corrigiu a exibicao de progresso do restore point.
- adicionou inicializacao em background do fluxo de IA no carregamento do dashboard.

## cb01507 - 2026-03-24 - optimize MainWindow: performance, UX and icon provider refactor
- refatorou `MainWindow`, reorganizou a UX do desktop e ajustou o provedor da biblioteca de icones.
- trouxe novos artefatos internos de apoio para design, verificacao e automacao local.

## 2033a99 - 2026-03-22 - feat: add JB GameBooster with local AI and dashboard launch mode
- criou o modulo JB GameBooster com snapshots, workflows, restore point, perfil de Rust e leitura de telemetria.
- expandiu o desktop para abrir em modo dashboard, integrar IA e exibir recomendacoes gerais e por jogo.

## 9875852 - 2026-03-22 - chore: snapshot before JB GameBooster update
- registrou um snapshot documental antes da grande expansao do GameBooster.
- adicionou e consolidou documentos de briefing e configuracao base do agente.

## 3b4e9de - 2026-03-22 - refine Auralis UX and icon persistence
- melhorou a UX do app, splash screen, fluxo principal e persistencia de icones.
- ajustou instalador, setup bootstrapper e integracao de icones no Explorer.

## 56c3387 - 2026-03-20 - clear stale explorer submenu flag
- limpou estado residual ligado ao submenu do Explorer.
- corrigiu o fluxo de registro para evitar configuracao antiga persistindo.

## 435b9db - 2026-03-20 - restore flat explorer menu entry
- removeu o submenu e restaurou a entrada plana no menu do Explorer.
- alinhou instaladores e scripts MSIX ao comportamento simplificado.

## 2c33a0d - 2026-03-20 - add explorer submenu and update checks
- adicionou submenu no Explorer e verificacao de atualizacoes via GitHub.
- expandiu desktop, launch options, instaladores e servico de update para esse fluxo.

## 7e0d72c - 2026-03-20 - show version in setup note
- passou a mostrar versao no bootstrapper de setup.

## 4c7d329 - 2026-03-20 - rebrand app as Auralis
- rebatizou o produto para Auralis em UI, manifestos, instaladores, assets e scripts.
- atualizou identidade visual, icones e referencias de marca em todo o projeto.

## bed5526 - 2026-03-20 - add MSIX packaging
- adicionou scripts de build, install e uninstall para empacotamento MSIX.
- preparou o projeto para distribuicao moderna no Windows.

## 515a7ab - 2026-03-20 - add installer bundle
- adicionou bundle de instalacao e scripts dedicados de install/uninstall.
- criou o bootstrapper inicial do instalador desktop.

## 2b185ff - 2026-03-20 - refactor image format handling
- refatorou o pipeline de formatos de imagem, rasterizacao SVG e conversao para icone.
- ajustou pontos de uso no desktop e em servicos de imagem.

## bf2ed50 - 2026-03-20 - fix folder verb default behavior
- consolidou a base inicial do app: solucao, camadas Domain/Application/Infrastructure/Desktop/WindowsIntegration e testes.
- implementou o fluxo principal de trocar icone de pasta, autorizacao, auditoria, recursos do Windows e integracao com Explorer.
