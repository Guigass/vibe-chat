# Achados de UX — VibeChat

Registro das revisões de interface. A automação **UX Review**
(`.cursor/automations/04-ux-review.prompt.md`) escreve aqui; a automação de Build
consome os abertos como fonte prioritária no modo manutenção.

Regras do registro:

- Um `UX-<n>` por achado, numeração contínua, nunca reaproveitada.
- Só entra o que foi **observado** rodando a aplicação. Suspeita sem evidência não entra.
- Severidade: **Alta** (bloqueia ou quebra uma tarefa), **Média** (atrapalha), **Baixa** (polimento).
- Achado fechado vira `Done` com o PR, não é apagado.
- Detalhe reversível recebe recomendação concreta; somente dependência R4 fica
  `External action`.

## Abertos

| ID | Tela | Achado | Severidade | Status |
|----|------|--------|------------|--------|
| UX-002 | Todas | Banner vermelho fixo “Invalid PrimeUI License” no canto inferior direito, **cobrindo “Anexar” e “Enviar”** do composer | Alta | Aberto — D-15 sair do PrimeNG; D-27 kit = spartan/ui; fecha com **B-104** |
| UX-003 | Shell | Em viewport estreito (~400 px) a sidebar continua ocupando quase metade da largura; não há colapso nem botão de alternar | Alta | Aberto — safety lane `UX-003`; corrigir como hotfix de responsividade se ainda reproduzível |
| UX-004 | Sidebar | Rótulos de seção (`GERAL`, `ENGENHARIA`, `MENSAGENS DIRETAS`, `MEMBROS`) com contraste baixo no tema claro | Média | Aberto — fecha em **B-103** ou em correção R1 anterior com teste de contraste |
| UX-005 | Admin | Seções sem permissão exibem o aviso em laranja/vermelho, dando aparência de erro a um estado esperado | Média | Aberto — fecha com **B-106** (esconder nav/áreas sem claim; matriz por papel) |
| UX-006 | Header | Botões de ícone (buscar, tema, densidade, painel) sem estado de hover/foco perceptível | Média | Aberto — fecha em **B-103** ou em correção R1 anterior com teste de foco |

## Detalhamento

### UX-001 — Botões DevAuth sem rótulo visível

Observado em `http://localhost:4200/login`, tema claro, Chrome. O botão primário
“Entrar com Keycloak” aparece normalmente; os três DevAuth logo abaixo são molduras
vazias. Dá para clicar (o primeiro loga como Alice), mas não dá para saber qual é qual.

Causa provável, a confirmar na implementação: o hero do login é sempre escuro, enquanto
`vc-button` só define `color` nas variantes — a variante `ghost` usa `var(--vc-ink)`,
que no tema claro é tinta escura. Resultado: texto escuro sobre fundo escuro.
`apps/web/src/app/shared/ui/button/button.ts` não define `color` na classe base
`.vc-btn`.

Correção esperada: o hero do login precisa fixar o contexto de cor (ou usar tokens
invertidos), e `.vc-btn` deve ter uma cor de base em vez de depender só da variante.

Execução: safety lane `UX-001` (R1). **Done** via [#74](https://github.com/Guigass/vibe-chat/pull/74) —
`.login-hero` redefine `--vc-ink*`/`--vc-border` para o plano escuro; `.vc-btn` ganha
`color` base.

### UX-002 — Banner de licença do PrimeUI cobrindo o composer

`primeng@22` **não é mais OSS**. O `LICENSE.md` do pacote instalado diz, textualmente:
“This package is part of **PrimeUI**, a family of commercial UI libraries by PrimeTek
Informatics” e “A valid license key is required to use this software… A missing,
invalid, or expired key may cause the software to display a license notice”.

Sem chave, `primeng/fesm2022/primeng-license.mjs` injeta um `<div>` `position:fixed`
no canto inferior direito, dentro de um shadow root `mode: 'closed'` e com
`z-index: 2147483647` — deliberadamente difícil de esconder por CSS. Na tela do chat,
esse banner fica exatamente sobre os botões **Anexar** e **Enviar** do composer.

São dois problemas empilhados:

1. **Produto quebrado** — a ação principal do app fica coberta.
2. **Compliance** — `AGENTS.md` proíbe dependência proprietária sem decisão explícita,
   e o ADR-002 foi emendado para adotar PrimeNG (B-073) sem essa análise de licença.

**D-15 decidiu (c) sair do PrimeNG.** **D-27** escolheu o substituto OSS:
**spartan/ui** (não NG-ZORRO). A correção é **B-104** (spec
`docs/product/specs/B-104-remover-primeng.md`): desinstalar o pacote, adotar
`@spartan-ng/brain` + tokens `--vc-*`, e reescrever `/admin` (Select via spartan;
tabela HTML). O agente **não** deve gerar chave nem esconder o banner por CSS.

### UX-003 — Sidebar não colapsa em viewport estreito

Em 400 px de largura a sidebar mantém a largura fixa e sobra pouco espaço para a
timeline. O shell tem um botão de recolher, mas não há colapso automático por
breakpoint nem overlay para telas pequenas. Como o app é PWA instalável (B-029),
o uso em tela pequena é esperado.

Execução: safety lane `UX-003` (R1), com viewport 320/360/400 px, teclado, foco e
E2E responsivo. Não precisa aguardar mobile nativo.

### UX-004 — Contraste dos rótulos de seção

Os rótulos em maiúsculas da sidebar usam um cinza claro sobre superfície clara.
Verificar contra AA (4.5:1 para texto normal) e ajustar o token — entra junto de
B-103, mas é corrigível antes.

### UX-005 — Aviso de permissão com cara de erro

Em `/admin`, “Sem permissão para ler settings sensíveis” e “Sem permissão para auditar
conversas” aparecem em laranja/vermelho. Para um Auditor, esse é o estado **normal**,
não uma falha.

Correção: **B-106** — não renderizar nav, toolbar nem seções sem a claim; deep-link
sem permissão vai para a primeira área permitida (ou empty neutro). Sem banner de
“sem permissão”. Matriz Admin vs Auditor vs Member na spec.

### UX-006 — Botões de ícone sem estado de hover/foco

Os ícones do cabeçalho não deixam claro que são clicáveis. Relacionado a B-103
(indicador de foco visível), mas o hover é polimento independente.

## Fechados

| ID | Tela | Achado | Severidade | Status |
|----|------|--------|------------|--------|
| UX-001 | Login | Os três botões DevAuth (Alice/Bob/Demo) renderizam como retângulos vazios, sem rótulo visível | Alta | **Done** — [#74](https://github.com/Guigass/vibe-chat/pull/74); hero fixa tokens invertidos; `.vc-btn` com `color` base |
