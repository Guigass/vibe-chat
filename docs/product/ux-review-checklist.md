# Checklist de Revisão de UI — VibeChat

Roteiro da automação **UX Review** (`.cursor/automations/04-ux-review.prompt.md`) e de
qualquer revisão manual. O objetivo é achar problema **observado**, não opinar sobre
gosto. Achado sem evidência de execução não entra em `ux-findings.md`.

## Como subir a interface

```bash
task ux:stack          # data plane + API (:5080) + Web (:4200), sem Playwright
```

O script instala o Node exigido pelo Angular quando falta e deixa os processos no ar.
Login sem Keycloak: botões DevAuth na tela de login (Alice/Bob/Demo) ou header
`X-Dev-User: alice|bob|demo`.

Para exercitar realtime, use duas sessões (janela normal + anônima), uma como Alice e
outra como Bob.

Em automação persistente, registrar antes quais portas já estavam ocupadas e,
ao final, encerrar somente API/Web iniciados pelo próprio run. Nunca usar
`docker compose down -v` para cleanup de revisão.

## Percurso obrigatório

Toda revisão passa por estas telas, nesta ordem:

1. **Login** — hero, CTA do Keycloak, botões DevAuth, link de demo offline
2. **Shell** — sidebar (spaces, canais, DMs, membros), cabeçalho, timeline, composer
3. **Envio** — mandar mensagem; observar status de entrega, avatar, hora, agrupamento
4. **Bolha** — reação, editar, apagar, abrir thread
5. **Thread** — painel lateral, estado vazio, envio de resposta
6. **Anexo** — anexar arquivo; progresso, prévia, download
7. **Busca** — `Ctrl/Cmd+K`, resultado, estado vazio
8. **Tema e densidade** — claro, escuro, compacto, confortável
9. **Admin** — `/admin`, com e sem permissão
10. **Realtime** — duas sessões: mensagem, edição, reação e “digitando”
11. **Reconnect** — derrubar a API por alguns segundos e observar o banner e o gap-fill
12. **Estreito** — 400 px de largura, e 1440 px

## O que procurar

### Funciona

- Ação principal alcançável e não coberta por nada
- Feedback imediato em toda ação (enviando, salvo, falhou)
- Estado vazio, de carregamento e de erro presentes e com texto útil
- Nada que pareça clicável e não seja, nem o contrário

### Legível

- Contraste AA (4.5:1 texto normal, 3:1 texto grande) nos **dois** temas
- Hierarquia clara: o que importa aparece primeiro
- Nada truncado sem indicação, nada sobreposto

### Consistente

- Tokens de `docs/architecture/design-system.md` — sem cor solta
- Sora + IBM Plex Sans; teal/charcoal
- Sem clonar Slack/Discord/WhatsApp
- Espaçamento e raio coerentes entre telas

### Operável

- Todo fluxo possível só com teclado
- Foco visível e ordem lógica
- `Esc` fecha o que abriu; foco volta para a origem
- Alvo de toque ≥ 24 px
- Toda ação de arrastar tem alternativa por clique (WCAG 2.2 · 2.5.7)

### Responsivo

- 400 px: sidebar não pode comer a conversa
- 1440 px: conteúdo não pode esticar sem limite de leitura
- Sem scroll horizontal

### Honesto

- Erro explica o que fazer, não só que falhou
- Nada de spinner infinito sem timeout
- Aviso de estado esperado (ex.: “sem permissão”) não usa cor de erro

## O que **não** reportar

- Preferência pessoal de cor, fonte ou espaçamento sem violação de token
- Feature ausente que já tem item no roadmap ou spec — isso é backlog, não achado
- Suposição sobre código sem ter observado o comportamento
- Problema do ambiente de teste (porta ocupada, seed vazio)

## Ao registrar

Cada achado em `docs/product/ux-findings.md` precisa de tela, descrição factual,
severidade e — quando houver — a pista de causa no código. Detalhe reversível
recebe recomendação concreta; somente dependência R4 fica `External action`.
