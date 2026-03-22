# CONTEXTO DO AGENTE - MelhorWindows

## Status atual
- fase: app desktop compilando e testado localmente, com dashboard expandido para o modulo JB GameBooster
- proximo passo: aprofundar o GameBooster por game (comecando por Rust) e retomar autenticacao/licenciamento remoto

## Objetivo do produto
- Aplicativo Windows para personalizar, facilitar e automatizar configuracoes e estilizacao do sistema.
- Primeiro modulo: adicionar uma acao no menu de contexto de pastas para trocar o icone da pasta a partir de uma imagem escolhida pelo usuario.
- Segundo modulo: oferecer um painel JB GameBooster dentro do dashboard para diagnostico inicial, otimizacoes gerais seguras e reversao controlada.
- O fluxo deve permitir preview, crop ou ajuste quando a imagem nao for quadrada, conversao para `.ico` e salvamento do historico por usuario.

## Escopo inicial do primeiro modulo
- entrada "Alterar icone da pasta" ao clicar com botao direito em uma pasta
- abertura do app com o caminho da pasta selecionada
- selecao de imagem local
- preview antes de salvar
- opcao de crop focal ou ajuste completo da imagem
- conversao para `.ico` em multiplos tamanhos
- aplicacao do icone na pasta alvo
- historico de icones convertidos por usuario
- restauracao do historico apos reinstalacao ou troca de maquina, se o usuario fizer login
- painel inicial para habilitar e desabilitar features do Windows baseadas em Registry
- trilha local de auditoria para mudancas sensiveis de Registry
- menu do dashboard com entrada dedicada para o JB GameBooster
- catalogo inicial de otimizacoes gerais para gaming com aplicacao por item ou em lote
- opcao de criar ponto de restauracao antes de alteracoes de sistema
- reversao da ultima sessao aplicada pelo JB GameBooster
- configuracao de IA local no JB GameBooster via Ollama
- analise local do snapshot do booster com modelo configuravel e recomendacoes em portugues
- botao explicito para testar conexao com Ollama e sugerir `ollama pull` quando o modelo configurado estiver ausente
- primeiro painel especifico de Rust com launch options sugeridos, comandos para `client.cfg` e analise local dedicada

## Stack recomendada
- app desktop principal implementado: .NET 8 + WPF
- integracao com Explorer: componente nativo para contexto de pasta + empacotamento MSIX ou sparse package
- banco local: SQLite
- servicos de imagem: biblioteca nativa para resize, crop e geracao de `.ico`
- IA local atual: Ollama em `http://localhost:11434`, com modelo padrao `gemma3:4b`
- backend remoto: API propria para autenticacao, licenciamento, sincronizacao e auditoria
- alternativa se quiser UI web: Tauri + Rust, mantendo a integracao do Explorer em componente nativo separado

## Decisoes arquiteturais
- Nao usar Electron como base principal do produto.
- Motivo: o ponto critico do projeto e a integracao profunda com o Windows, nao a tela. Electron pode servir como UI, mas nao resolve bem a parte nativa e aumenta superficie de ataque, consumo de memoria e tamanho do instalador.
- Preferir arquitetura em camadas:
  - camada 1: UI desktop
  - camada 2: servicos de negocio
  - camada 3: integracao com Windows
  - camada 4: backend de identidade, licenca e sincronizacao

## RBAC aprovado
- modelo: RBAC obrigatorio para todo sistema multiusuario
- papeis iniciais:
  - admin
  - operador
  - usuario
- permissoes iniciais:
  - gerir usuarios e papeis
  - gerir licencas e dispositivos
  - alterar configuracoes globais
  - aplicar icones
  - gerenciar historico proprio
  - sincronizar historico proprio
- regra: a UI nao decide autorizacao sozinha; a acao precisa ser validada no backend ou na camada nativa responsavel

## Regras de seguranca
- autenticacao e autorizacao separadas
- menor privilegio por padrao
- licenciamento validado no servidor
- tokens e segredos fora da UI
- estado local sensivel protegido com DPAPI por usuario quando armazenado no dispositivo
- trilha de auditoria para login, troca de papel, ativacao de licenca e acoes administrativas
- protecao contra manipulacao local de estado de licenca
- assinatura de codigo do app e do instalador
- atualizacao automatica assinada

## Modelo de dados inicial
- User
- Role
- Permission
- UserRole
- FolderIconHistory
- IconAsset
- DeviceActivation
- License
- AuditLog

## Fluxos principais
- fluxo 1: usuario clica com botao direito em pasta -> app abre no contexto da pasta -> usuario escolhe imagem -> faz preview/crop -> app converte e aplica icone
- fluxo 2: usuario autenticado acessa historico proprio -> reaplica icones antigos
- fluxo 3: admin gerencia usuarios, papeis, licencas e dispositivos
- fluxo 4: admin ou papel autorizado habilita/desabilita feature de Windows -> app grava auditoria local da mudanca
- fluxo 5: usuario abre o dashboard -> entra no JB GameBooster -> aplica recomendacoes gerais com restore point opcional e possibilidade de desfazer a ultima sessao

## Riscos e limitacoes
- Integracao com o novo menu de contexto do Windows 11 exige abordagem nativa e, em cenarios modernos, package identity.
- "Nao crackeavel" nao existe; o objetivo realista e elevar muito o custo da quebra e manter decisao critica de acesso no servidor.
- Se o produto precisar operar offline por longos periodos, sera necessario desenhar uma politica de licenca offline com expiracao curta e revalidacao.
- O JB GameBooster ainda esta na fase inicial e cobre apenas ajustes gerais e reversiveis via Registry; perfis por game, launch options e monitoramento continuo continuam em aberto.
- A analise local depende do Ollama estar ativo e com pelo menos um modelo baixado; o app ja detecta indisponibilidade e modelos ausentes.
- O modulo de Rust ainda esta em fase de recomendacao e analise; a automacao de escrita em `localconfig.vdf` e `client.cfg` ainda nao foi implementada.

## O que validar no scaffold inicial
- abrir janela recebendo caminho de pasta como argumento
- registrar/desregistrar integracao com Explorer sem exigir admin quando possivel
- carregar imagem, gerar preview e aplicar corte manual coerente
- gerar `.ico` corretamente em 16, 32, 48, 64, 128 e 256 px
- aplicar icone em pasta de teste
- persistir historico local
- persistir auditoria local de Registry
- listar e acionar features de Windows com verificacao de permissao
- sincronizar historico por usuario autenticado
- status atual: build release, testes e publish local executados com sucesso
