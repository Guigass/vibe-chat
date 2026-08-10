# Harness e Loop Engineering — VibeChat

Contrato de desenho do sistema que seleciona, executa, verifica e encerra o
trabalho dos agentes. Complementa [`autonomia.md`](autonomia.md) e
[`operacao-24x7.md`](operacao-24x7.md); não altera decisões de produto, classes
de risco nem os gates de segurança.

## Objetivo

O projeto deve avançar sem depender de prompts improvisados e sem produzir
trabalho infinito, duplicado ou impossível de auditar. Para isso, o VibeChat usa
loops fechados: o caminho, a evidência, os limites e as condições de parada são
definidos antes da execução.

```text
observar → selecionar → reservar → agir → verificar → persistir → parar
    ↑                                                        │
    └──────── nova execução, com contexto limpo ─────────────┘
```

“Crescer sozinho” significa consumir o roadmap autorizado com segurança. Não
significa inventar features, publicar em produção, ampliar gastos ou remover
controles para manter o fluxo andando.

## Comportamento desejado e mecanismo

| Comportamento | Mecanismo do harness |
|---------------|----------------------|
| Retomar depois de crash/context reset | Git, PR, roadmap, checks e lease durável |
| Não duplicar trabalho | Um Work-Item por PR + busca de PR + lease + recheck |
| Não parar cedo | Aceite determinístico da spec e gates por risco |
| Não iterar para sempre | Limite de tentativas, no-progress e stop reasons |
| Não aprovar a própria mudança | Build separado de QA e Security |
| Não degradar com contexto enorme | Leitura progressiva e handoff estruturado |
| Não esquecer comandos perigosos | Hook `beforeShellExecution` versionado |
| Não quebrar o próprio harness | Validador offline + hook `stop` limitado |
| Não confundir texto com entrega | `Done` somente após merge e evidência |
| Aprender com falhas reais | Mudança R0 no harness ligada a incidente/finding |

## Fontes de verdade

A memória do modelo é cache, nunca autoridade. Cada execução reconstrói o estado
nesta ordem:

1. decisões D-* e ADRs aceitos;
2. contratos e regras de segurança;
3. specs e roadmap;
4. Git/PR/checks para estado factual da implementação;
5. findings operacionais/UX;
6. Memories para lease, ponte de contexto e sinais ainda não consolidados.

Se Memory divergir de Git, PR ou documento canônico, prevalece a fonte
versionada/observável e o drift é registrado.

## Loop interno de uma execução

Cada run executa um único ciclo fechado:

### 1. Gather

- capturar `origin/main`, branch designada e estado do worktree;
- ler o núcleo mínimo: `AGENTS.md`, visão geral, contratos, diagrama de módulos,
  glossário e decisões;
- carregar somente a spec, ADRs, regras e módulos afetados;
- verificar PRs, checks, lease e finding do mesmo ID;
- registrar fatos faltantes como `UNKNOWN`, nunca como sucesso implícito.

### 2. Plan

Antes da primeira mutação, registrar no resumo do run:

```text
Goal:
Work-Item:
Risk:
Done when:
Files/surfaces:
Tests/gates:
Stop if:
```

Mudança grande mantém um plano atualizado. A conclusão deve ser verificável por
teste, check, consulta, screenshot ou evidência equivalente.

### 3. Act

- uma intenção e um Work-Item;
- mutações em série dentro da branch designada;
- leituras independentes podem ser paralelas;
- nenhuma escrita direta em `main`;
- nenhuma ação R4;
- sem follow-up necessário para o caminho principal funcionar.

### 4. Verify

Aplicar primeiro o feedback barato e determinístico, depois o caro:

1. parser/format/diff check;
2. teste unitário ou checker específico;
3. build/architecture/security;
4. integração/E2E/UX quando exigidos;
5. CI e revisão independente no SHA imutável.

Sucesso silencioso é preferível; falha deve trazer comando, arquivo e mensagem
acionável. Nunca repetir um comando idêntico sem uma hipótese nova.

### 5. Persist

Antes de encerrar:

- commit/PR/check preservam a mudança e a evidência;
- roadmap/finding só muda segundo o papel da automação;
- lease e Memory recebem estado compacto, sem secret;
- o próximo run deve conseguir retomar sem o transcript anterior.

### 6. Stop

Todo run termina com um motivo canônico:

| Stop reason | Quando usar |
|-------------|-------------|
| `GOAL_MET` | Critério de saída e gates foram satisfeitos |
| `NO_ELIGIBLE_WORK` | Fila válida, mas nenhum item está elegível |
| `DUPLICATE_ACTIVE` | Já existe PR, lease ou writer para o ID |
| `WAITING_CHECK` | Check externo ainda não concluiu no tempo limitado |
| `MAX_ATTEMPTS` | Três abordagens materiais falharam |
| `NO_PROGRESS` | Nova iteração não alteraria evidência, diff ou diagnóstico |
| `SAFETY_GATE` | Main, RLS, secret, cross-tenant ou required gate bloqueia |
| `EXTERNAL_ACTION` | Só uma condição R4 pode liberar a etapa |
| `TOOLING_BLOCKED` | Ferramenta/permissão obrigatória está indisponível |
| `CONTEXT_HANDOFF` | Contexto precisa ser reiniciado com handoff persistido |

`MAX_ATTEMPTS`, `NO_PROGRESS` e `WAITING_CHECK` encerram a execução atual. Um
schedule futuro só retoma se a evidência externa ou a abordagem tiver mudado.

## Orçamentos e circuit breakers

Defaults:

| Controle | Limite |
|----------|--------|
| Work-Item por run/PR | 1 |
| Ciclos materiais act → verify | até 3 |
| Mesma falha com mesma abordagem | 1; a próxima muda a hipótese |
| Espera de checks no QA | até 45 min |
| Build concorrente sem lease atômico | 1 |
| R3 concorrente | 1 |
| PRs Build ativos | até 3 somente após concorrência comprovada |
| Mudança sem teste/evidência | proibida |

Se custo, quota ou duração não puderem ser lidos de forma confiável, o harness
não inventa números. O dashboard deve fornecer alertas; o Watchdog reduz a
cadência quando detectar anomalia.

## Contexto e reinício

Contexto é orçamento:

- não carregar toda a documentação “por segurança”;
- começar pelo núcleo e expandir por superfície afetada;
- resumir logs extensos em head/tail + caminho do artefato;
- não colar novamente conteúdo já versionado; referenciar o arquivo;
- após contexto degradado, persistir um handoff e iniciar run novo.

Handoff mínimo:

```text
Work-Item:
Goal / done condition:
Base/head SHA:
Changed paths:
Evidence passed:
Failure / hypothesis:
Next safe action:
Lease / PR:
```

## Maker/checker e permissões

- Build produz; QA e Security julgam em contexto fresco.
- PR Repair é maker: altera o PR reprovado e invalida os vereditos do SHA
  anterior; nunca aprova nem mergeia.
- QA não altera o SHA revisado.
- Security não implementa nem mergeia.
- Docs fecha o estado somente após merge.
- Watchdog recupera o ciclo, mas não inventa produto.
- Retrospective aprende com evidência acumulada, mas não aprova a própria
  mudança no harness.
- Hooks aplicam invariantes mecânicas; prompts explicam julgamento.

Prompt não é controle de acesso. Branch protection, token scopes, sandbox e
hooks precisam impor a fronteira fora do modelo.

## Hooks versionados

`.cursor/hooks.json` registra:

- `beforeShellExecution`: bloqueia força em Git, push direto em `main`, remoção
  de volumes e comandos destrutivos explícitos;
- `stop`: executa o checker do harness e permite no máximo um follow-up corretivo
  por geração quando o próprio `.cursor` estiver inválido.

O checker é offline, não lê `.env`, não acessa rede e não altera arquivos.
Executar manualmente com:

```bash
task agent:check
```

Em uma workstation sem `go-task`, executar diretamente:

```bash
node .cursor/hooks/validate-harness.mjs
node --test .cursor/hooks/guard-shell.test.mjs
```

Hooks reduzem erro mecânico; não substituem testes, revisão ou branch
protection.

## Evolução do harness

O harness é vivo, mas não se autoedita silenciosamente:

1. observar falha concreta ou atrito reproduzível;
2. registrar finding ou citar run/PR;
3. derivar a menor regra, hook ou checker que previne recorrência;
4. mudar por PR R0 separado;
5. rodar `task agent:check`;
6. validar o novo comportamento em uma execução;
7. remover regra que ficou redundante ou gera falso positivo recorrente.

Nova automação só entra quando tiver trigger, entrada, saída, owner, permissão,
budget, stop conditions, estado durável e modo de falha definidos.

## Referências aplicadas

O desenho adota os pontos comuns das referências estudadas em 2026-07-30:

- loops repetem trabalho até condição de parada; critérios quantitativos e
  limites de turnos tornam o objetivo verificável;
- harness reúne contexto, ferramentas, estado, permissões, observação e
  recuperação ao redor do modelo;
- loops longos precisam de estado fora do contexto e reinícios limpos;
- produção prefere loops fechados; exploração ambígua fica fora do ciclo
  autônomo até ganhar spec e critério verificável;
- um item por iteração, testes automáticos e maker/checker reduzem erro composto;
- hooks transformam uma instrução recorrente em enforcement.

Links externos e notas de adoção ficam no
[`runbook de 05/08`](go-live-2026-08-05.md).
