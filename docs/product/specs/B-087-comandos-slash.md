# B-087 — Comandos slash

> Wave W8-9 · Trilha C/D · Deps: B-082 · Decisões: D-11

## Problema

Toda ação exige caçar um botão. Os quatro apps de referência resolvem com `/comando`
no composer, que também é a porta de entrada para automações depois.

## Escopo

Conjunto pequeno e útil, todos mapeando para capacidades que **já existem**:

| Comando | Efeito |
|---------|--------|
| `/dm @pessoa` | abre ou cria a DM |
| `/topico <texto>` | altera a descrição do canal (exige permissão) |
| `/convidar <email>` | convida para o workspace (exige `workspace.admin`) |
| `/resumir` | dispara summarize da IA (se habilitada) |
| `/apagar` | apaga a própria última mensagem do canal |
| `/ajuda` | lista os comandos disponíveis para o usuário |

- Autocomplete ao digitar `/` no começo da mensagem, com descrição e argumentos.
- Comando desconhecido não é enviado como mensagem: erro inline explicando.
- Comando some da lista quando o usuário não tem a permissão correspondente.

## Fora de escopo

- Comandos definidos por integração/app externo — depende de um modelo de app que
  está fora por D-11.
- Comando com formulário/modal.

## Contratos

Sem endpoint novo: o cliente traduz o comando para as chamadas existentes. Isso mantém
authZ e audit exatamente iguais aos do caminho por botão.

Exceção: `GET /api/v1/workspaces/{workspaceId}/commands` devolve os comandos que o
usuário pode usar, para que a lista respeite permissão sem lógica duplicada no cliente.

`contratos.md`: endpoint de descoberta e a tabela de comandos.

## UX

- `/` no início abre a lista; setas navegam; `Tab` completa; `Esc` cancela.
- Argumento inválido → erro inline, texto preservado.
- Resultado de comando aparece como mensagem efêmera só para quem executou
  (não persiste, não vai para o outbox).
- `/ajuda` abre um painel com os comandos e atalhos.

## Multi-tenant e authZ

Nenhum comando ganha privilégio: cada um chama o endpoint que já valida permissão e
tenant. A lista vem do servidor para não induzir o usuário a tentar o que não pode.

## Aceite

- [ ] `/dm @bob` abre a DM com Bob
- [ ] `/convidar` não aparece para quem não é admin, e falha com 403 se forçado
- [ ] `/xpto` mostra erro inline e não vira mensagem
- [ ] `/resumir` com IA desligada explica em vez de dar 503 cru
- [ ] Lista navegável só por teclado

## Testes

- Unit (web): parser de comando e argumentos; filtro por permissão.
- Integration: `GET /commands` reflete o papel do usuário.
- Security: comando privilegiado sem permissão → 403 no endpoint alvo.

## Riscos

- Comando destrutivo por engano → `/apagar` age só na própria última mensagem e pede
  confirmação.
- Divergência entre lista do cliente e permissão real → a lista vem do servidor.
