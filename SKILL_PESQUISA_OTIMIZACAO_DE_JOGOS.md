# Skill - Pesquisa de Otimizacao por Jogo

## Objetivo
Este arquivo define como o agente deve pesquisar, consolidar e transformar conhecimento de otimizacao de um jogo em uma base reutilizavel para IA, sem empurrar placebo, sem copiar guias cegamente e sem sugerir ajustes incompativeis com o hardware do usuario.

## Quando usar
- Ao adicionar suporte para um novo jogo no projeto.
- Ao revisar um jogo ja suportado com base em novas fontes.
- Ao transformar pesquisa comunitaria em regras condicionais que a IA possa aplicar depois do diagnostico do computador.

## Resultado esperado
Para cada jogo novo, o agente deve entregar:
- uma pesquisa consolidada em `.md`
- uma base persistida estruturada para uso pela IA, preferencialmente em `.json`
- regras separadas entre:
  - gerais
  - condicionais por CPU
  - condicionais por GPU/vendor
  - condicionais por RAM
  - testes reversiveis
  - triagem/diagnostico
  - upgrade futuro
- prompts da IA atualizados para usar essa base antes de sugerir mudancas

## Fontes obrigatorias
- fonte oficial do jogo ou do estudio
- Steam Community, quando houver
- Reddit relevante do jogo
- videos/guias de YouTube quando fizer sentido
- comentarios/comunidade em torno desses videos, quando acessivel

## Regra de confianca
- fonte oficial tem peso maior que comunidade
- comunidade com repeticao alta pode virar regra condicional
- dica isolada, tweak obscuro ou promessa agressiva deve entrar como:
  - experimental
  - baixa confianca
  - ou ser descartado

## Processo
1. Entender o jogo e seus gargalos mais comuns.
2. Pesquisar fontes oficiais e comunidade.
3. Separar:
   - achados fortes
   - achados condicionais
   - mitos/placebo
4. Registrar links e resumo da pesquisa em um `.md`.
5. Converter a pesquisa em uma base estruturada reutilizavel, preferencialmente JSON.
6. Modelar cada regra com:
   - id
   - prioridade
   - tipo
   - titulo
   - recomendacao base
   - motivo
   - quando usar
   - nivel de confianca
   - condicoes
7. Ligar essa base ao prompt da IA.
8. Fazer a IA decidir apenas depois de cruzar:
   - CPU
   - GPU/vendor
   - RAM total
   - carga de memoria
   - versao do Windows
   - sintomas atuais detectados

## Modelo de condicoes
- `minRamGb`
- `maxRamGb`
- `minMemoryLoadPercent`
- `maxMemoryLoadPercent`
- `cpuContainsAny`
- `cpuExcludesAny`
- `gpuVendorsAny`
- `windowsContainsAny`

## Regra central
Nao basta salvar "melhores configuracoes do jogo".  
A IA deve responder:
- isso vale para este PC?
- isso vale agora ou so em caso especifico?
- isso e ajuste imediato, teste reversivel, triagem ou upgrade futuro?

## Regras de escrita da IA
- nao inventar benchmark
- nao prometer ganho de FPS fixo sem base
- nao recomendar ajuste de NVIDIA para GPU AMD
- nao recomendar ajuste de AMD para GPU NVIDIA
- nao sugerir launch options antigas sem justificar
- nao empurrar placebo de video/comunidade como se fosse regra universal
- nao expor informacoes tecnicas internas do provedor de IA ao usuario final

## Estrutura sugerida para cada pesquisa
```md
# Pesquisa - [NOME DO JOGO]

## Objetivo
- ...

## Fontes
- link
- link

## Leitura sintetica
- ...

## Regras fortes
- ...

## Regras condicionais
- ...

## Mitos e placebo para evitar
- ...

## Como a IA deve usar isso
- ...
```

## Estrutura sugerida para a base JSON
```json
{
  "gameKey": "nome-do-jogo",
  "gameTitle": "Nome do Jogo",
  "researchDate": "YYYY-MM-DD",
  "researchSummary": "Resumo curto",
  "sources": [],
  "corePrinciples": [],
  "mythsToAvoid": [],
  "recommendationRules": []
}
```

## Exemplo de regra
```json
{
  "id": "game-pagefile-managed",
  "priority": "Alta",
  "ruleType": "windows-memory",
  "title": "Manter pagefile automatico",
  "recommendation": "Priorizar pagefile automatico em maquinas com 16 GB ou menos.",
  "reason": "Stutter recorrente em cenarios de memoria apertada.",
  "applicability": "Subir quando houver 16 GB ou menos e sintoma de travada longa.",
  "confidence": "Alta",
  "conditions": {
    "maxRamGb": 16
  }
}
```
