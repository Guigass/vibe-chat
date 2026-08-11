# B-099 — Paleta de comandos e atalhos

> Wave W10-5 · Trilha D · Deps: B-087, B-098, B-173 · Decisões: D-11 · Risco R1

## Problema

Existem dois atalhos (`Ctrl/Cmd+K` foca a busca, `Esc` fecha painel). Todo o resto é
mouse. Quem usa o produto o dia inteiro pede navegação por teclado.

## Escopo

- Paleta em `Ctrl/Cmd+K`: troca de canal, abre DM, executa comando (B-087), vai para
  Salvos/Admin, alterna tema e densidade.
- Busca difusa sobre canais, pessoas e ações, com recentes no topo.
- Conjunto de atalhos:

| Atalho | Ação |
|--------|------|
| `Ctrl/Cmd+K` | paleta |
| `Ctrl/Cmd+Shift+F` | busca com filtros |
| `Alt+↑` / `Alt+↓` | canal anterior / próximo |
| `Alt+Shift+↑` / `Alt+Shift+↓` | canal não lido anterior / próximo |
| `Esc` | fecha painel, cancela edição |
| `↑` no composer vazio | edita a própria última mensagem **no composer** (B-173; não na bolha) |
| `Ctrl/Cmd+Shift+M` | vai para menções |
| `Shift+Esc` | marca canal atual como lido |
| `?` | folha de atalhos |

- Folha de atalhos em modal, também acessível por `/ajuda`.
- Busca simples continua no cabeçalho; a paleta é navegação, não substitui a busca.

## Fora de escopo

- Atalho customizável pelo usuário; macro; modo Vim.

## Contratos

Nenhum novo. A paleta consome `GET /commands` (B-087) e as listas já carregadas de
canais e membros.

## UX

- Paleta centralizada, foco preso, `Esc` fecha e devolve o foco ao elemento anterior.
- Resultados agrupados por tipo (Canais, Pessoas, Ações) com navegação por setas.
- Cada item mostra o atalho equivalente, quando existe — a paleta ensina os atalhos.
- Atalhos não disparam dentro de campo de texto, exceto os explicitamente previstos.
- `prefers-reduced-motion` remove a animação de abertura.

## Multi-tenant e authZ

A paleta só lista o que o usuário já pode ver (canais com membership, membros
visíveis, comandos permitidos). Nenhuma ação nova ganha privilégio: cada uma chama o
endpoint que já valida.

## Aceite

- [ ] `Ctrl+K` abre e digitar “ger” encontra `#geral`
- [ ] `Enter` troca de canal e devolve o foco ao composer
- [ ] `Alt+↓` navega entre canais
- [ ] `↑` no composer vazio abre a edição da última mensagem própria **no composer**
      (B-173; política B-107 se já existir)
- [ ] `?` abre a folha de atalhos
- [ ] Atalho não dispara enquanto digita no composer
- [ ] Paleta não lista canal sem membership
- [ ] Ciclo de `Tab` fica preso dentro da paleta enquanto aberta

## Testes

- Unit (web): busca difusa, agrupamento, mapa de atalhos, guarda de campo de texto.
- E2E: navegar entre três canais só pelo teclado e enviar uma mensagem.
- A11y: foco preso e devolvido; leitor de tela anuncia a paleta.

## Riscos

- Conflito com atalho do navegador → evitar combinações reservadas e documentar as
  escolhidas na folha.
- Paleta virando “menu de tudo” → escopo fixo em navegação + comandos de B-087.
