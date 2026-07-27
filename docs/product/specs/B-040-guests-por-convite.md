# B-040 — Guests por convite

> Wave W10-10 · Trilha B/D/E · Deps: P2-1, B-098 · Decisões: D-07 (revisado 2026-07-25) · Risco R3

## Problema

Não há como trazer alguém de fora para um canal. Hoje ou a pessoa vira membro do
workspace inteiro, ou fica de fora. D-07 estava bloqueando o item por falta de spec;
a revisão de 2026-07-25 fechou as regras.

## Escopo

Exatamente o que D-07 revisado autoriza, nada além:

- Admin gera convite para **um canal** (nunca para o workspace).
- Convite é um link com token de **uso único**, validade default 7 dias, máximo 30.
- Aceitar exige autenticação no IdP; o guest continua sendo uma identidade real.
- Guest pode ler o canal do convite, enviar mensagem, anexo e reação.
- Guest **não**: lista outros canais, usa busca global, abre DM com quem não esteja no
  mesmo canal, vê o diretório do workspace, lê configuração, convida alguém, cria canal.
- Admin lista guests, vê a validade e **revoga**; revogar encerra sessão e membership.
- Convite, aceite, revogação e expiração geram audit.
- Aviso visível no canal de que há convidado presente.

## Fora de escopo

- Guest em múltiplos canais com um convite; guest em DM em grupo; guest com upload
  desabilitado (configurável depois se houver demanda).

## Contratos

`directory.channel_invites`:

| Coluna | Tipo |
|--------|------|
| `TenantId`, `WorkspaceId`, `ChannelId` | uuid |
| `TokenHash` | text (hash do token; o valor cru **nunca** é persistido) |
| `CreatedByUserId` | uuid |
| `Email` | text? (opcional, restringe o aceite) |
| `ExpiresAt` | timestamptz |
| `AcceptedAt`, `AcceptedByUserId` | opcionais |
| `RevokedAt`, `RevokedByUserId` | opcionais |

- `POST .../channels/{channelId}/invites` — `{ email?, expiresInDays? }`; exige
  `workspace.admin`; devolve o link **uma única vez**
- `GET .../channels/{channelId}/invites` — lista (sem token)
- `DELETE .../invites/{inviteId}` — revoga
- `POST /api/v1/invites/{token}/accept` — autenticado; cria membership `Guest`
  no canal; consome o token
- `Directory:Invites:MaxExpiryDays` (default `30`)

`Role.Guest` já existe no catálogo de permissões e recebe um conjunto próprio:
`message.send`, `message.react`, `attachment.upload` no canal do convite. Nada mais.

`contratos.md`: tabela, quatro endpoints, permissões de `Guest`.

## UX

- Admin: “Convidar para o canal” no menu do canal; diálogo com e-mail opcional e
  validade; link copiável mostrado **uma vez**, com aviso.
- Guest após aceitar: cai direto no canal, sem sidebar de outros canais, com aviso de
  que o acesso é limitado a este canal.
- Canal com guest mostra um selo “Convidado presente” no cabeçalho.
- Link expirado ou revogado: página explicando, sem revelar se o canal existe.

## Multi-tenant e authZ

Este é o item de maior risco de authZ da wave. Regras que são requisito de aprovação:

- Todo endpoint valida a **membership do canal**, não a do workspace — guest não tem
  membership de workspace.
- Busca (B-098) força escopo ao canal do convite para guest.
- Diretório de membros e autocomplete de menção (B-082) devolvem só quem está no canal.
- Revogação encerra a sessão ativa, inclusive a conexão de hub.
- Token é comparado por hash e é de uso único; segunda tentativa → 410.
- Enumerar convite inválido devolve sempre a mesma resposta, sem distinguir
  “não existe” de “expirado”.
- `multi-tenant.md` ganha os casos de teste T-guest; `modelo-ameacas.md` ganha a
  superfície do convite por link.

## Aceite

- [ ] Admin gera link; aceitar dá acesso só àquele canal
- [ ] Guest não enxerga outros canais nem o diretório
- [ ] Busca global do guest é bloqueada / forçada ao canal
- [ ] Guest envia mensagem, anexo e reação
- [ ] Guest não convida, não cria canal, não lê configuração
- [ ] Segundo uso do token → 410
- [ ] Convite expirado → recusado
- [ ] Revogar encerra a sessão e derruba o hub
- [ ] Convite, aceite e revogação aparecem no audit
- [ ] Token não aparece em log nem na listagem

## Testes

- Security (suíte dedicada de guest): cada linha do “não pode” acima como teste
  negativo explícito, em API e no hub.
- Integration: ciclo completo criar → aceitar → revogar; expiração; uso único.
- Security: token de outro tenant → 404 genérico; enumeração indistinguível.
- E2E: admin convida, guest aceita em sessão separada e envia mensagem.

## Riscos

- Guest escalando para o workspace por endpoint esquecido → a suíte negativa tem de
  cobrir **todos** os endpoints de workspace, não uma amostra.
- Link vazando em encaminhamento de e-mail → uso único, validade curta e revogação.
- Guest esquecido no canal para sempre → listagem no admin com data de entrada e
  relatório de guests ativos.
