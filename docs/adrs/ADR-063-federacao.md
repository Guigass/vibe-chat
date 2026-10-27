# ADR-063 — Federação allowlisted entre instâncias

- Status: Accepted
- Data: 2026-10-27
- Relacionado: B-065, B-138, B-146, D-21, ADR-054

## Contexto

B-138 entrega a ponte limitada. D-21 pede, em seguida, federação
server-to-server só entre trust domains allowlisted, com identidade remota
visível, cópia consentida e revogação. A spec pede que o ADR compare um padrão
aberto com um protocolo mínimo e prefira interoperabilidade OSS comprovada.

## Decisão

1. Flag `Integrations:Federation:Enabled` nasce false. Desligada, as rotas
   respondem 404 `FederationDisabled`. O chat não depende desta flag.
2. **Protocolo.** O envelope é `vibechat.federation.envelope.v1`
   (schemaVersion 1), separado da API de cliente. A assinatura é
   `hmac-sha256=` sobre o JSON canônico, o mesmo primitivo já usado em B-132
   e B-138. Não há discovery pública.
3. **Por que não Matrix nem ActivityPub nesta fatia.** Matrix exige um
   homeserver à parte e estado fora do RLS. ActivityPub assume descoberta
   pública, o que D-21 proíbe. Os campos canônicos (origem, destino, event id,
   expiração, key id, assinatura, política de cópia) ficam estáveis para um
   adaptador futuro traduzir, sem segundo processo nesta entrega.
4. **Allowlist.** O workspace só confia num domínio que esteja em
   `AllowedDomains`. URL, usuário ou caminho responde `FederationEgressBlocked`.
   A API não abre socket nem segue redirect: o peer empurra eventos e puxa a
   reconciliação com o token do bot.
5. **Sala explícita.** Um trust liga uma ou mais salas a canais que não são
   DM. O bot entra só nesses canais. Mensagem de outro canal não sai.
6. **Chave.** O trust nasce em `k1`. Rotação emite outro token e aceita o
   anterior até `retire`. Assinatura com chave desconhecida ou MAC inválido
   responde `FederationSignatureInvalid` e não grava mensagem.
7. **Partição.** O mesmo `eventId` não cria outra mensagem. O pull avança o
   checkpoint e não reemite o corpo. Eco do `InstanceId` local é loop.
8. **Delete.** `deleteRemoteGuaranteed` permanece false. A limitação
   `remote-delete-and-retention-best-effort` volta na sala, no convite e no
   pull. Um delete remoto não apaga a cópia local.
9. **DLP.** Ação `federation` que não prossegue omite o corpo e audita
   `integration.federation.copy.blocked`.
10. **AuthZ.** Gestão exige `workspace.admin`. A leitura da sala exige
    membership e `message.read`. RLS FORCE nas quatro tabelas novas.

## Alternativas

- Homeserver Matrix (Synapse/Dendrite): interoperável, mas é um serviço novo
  e tira o estado do Postgres com RLS.
- ActivityPub: padrão aberto, porém a descoberta pública contradiz D-21.
- Cliente HTTP de saída: abre SSRF. O peer puxa, como na ponte.

## Rollback

1. `Integrations:Federation:Enabled=false`. As rotas respondem 404.
2. `POST …/revoke` desativa o bot e revoga os tokens. Mensagens já copiadas
   permanecem.
3. `Down` remove só `federation_trusts`, `federation_rooms`,
   `federation_identities` e `federation_events`.

## Consequências

- **+** Evento sem assinatura válida não entra. Revogar corta o tráfego novo.
- **+** Domínio, cópia e limitação de delete ficam visíveis na sala.
- **−** Não há compatibilidade de wire com Matrix ou ActivityPub nesta fatia.
