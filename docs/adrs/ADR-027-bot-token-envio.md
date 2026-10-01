# ADR-027: Bot de integração com token opaco

## Status: Accepted

## Contexto

B-109 / W10-13 é o núcleo da trilha de plugins: uma identidade `Role.Bot` e um
token para sistemas externos postarem mensagem. Webhooks (B-048/B-108) só saem.
Não há decisão D-* nova. D-11 continua: sem loja pública. ADR-015 continua: sem
bus externo. O pacote `docs/architecture/pacotes-decisao-r3.md` (Plugins) pede
manifesto, assinatura e discovery. Esta fatia é só credencial + `messages.send`.
Manifesto, assinatura de pacote, discovery e registry ficam em B-110, B-066 e
B-137. Divergência registrada aqui, sem reabrir D-11.

## Decisão

1. **Identidade.** `workspace.admin` cria um bot no workspace. Nasce um
   `UserProfile` com subject `bot:{id}` (não entra por OIDC/DevAuth) e um
   `WorkspaceMember` `Role.Bot`.
2. **Token.** Prefixo `vc_int_`, 32 bytes aleatórios. O banco guarda SHA-256 e
   `last4`. O valor cru sai só no create e no rotate. Revogar seta `RevokedAt`
   e o próximo request é 401.
3. **Auth.** `Authorization: Bearer vc_int_…` ou `X-VibeChat-Integration-Token`.
   O scheme `Integration` não analisa o segredo como JWT, para o bearer não
   cair no log de falha do JwtBearer. O tenant sai do token, nunca do body.
   Antes do tenant ser conhecido, `SET LOCAL app.integration_token_hash`
   revela só a linha daquele hash (mesmo padrão do convite B-040).
4. **Escopo.** `channelIds` explícitos. Lista vazia não posta em canal.
   DM/GroupDm não entram no escopo. `allowDms` libera
   `POST /integrations/v1/dms` só com membro do mesmo workspace.
5. **Escrita.** O endpoint chama `IMessageWriter.SendAsync` (idempotência +
   `seq` + outbox). A chave de idempotência é prefixada com o id do bot. O
   evento `MessageCreated` leva `authorIsBot`. Não há caminho paralelo de insert.
6. **Rate-limit.** Chave Redis `t:{tenantId}:rl:integration:{botId}`, mesmo teto
   de send do tenant, balde separado do humano.
7. **Flag.** `Integrations:Bots:Enabled` default false. Off → 404
   `IntegrationDisabled`. Development liga para o lab, como convites e GroupDm.
8. **UI.** Formulário mínimo em `/admin/plugins`. A fachada de instalar plugin
   continua sendo B-110.
9. **Manifesto / assinatura / discovery.** Fora desta fatia. Plugin local
   continua sendo configuração + identidade; nenhum DLL/JS entra no processo.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| Reusar o JWT do Keycloak com um client confidencial | Acopla o bot ao IdP e ao login humano; a spec pede token opaco da API |
| Webhook inbound sem identidade | Não amarra tenant, escopo nem auditoria |
| Guardar o token cifrado em vez de hash | O admin não precisa reler o segredo; hash + rotate basta |
| Manifesto completo agora | É B-110; misturar as duas UIs é o risco que a spec evita |

## Rollback

1. `Integrations:Bots:Enabled=false`. Create, list, rotate, revoke e send
   respondem 404. Mensagens já gravadas permanecem.
2. A migration `Down` apaga `integrations.bot_channel_scopes`,
   `integrations.bot_tokens` e `integrations.bots`. Não apaga mensagens.
   Em dados reais, preferir o passo 1.

## Consequências

- **+** Um sistema externo posta em canal permitido e o membro vê em tempo real
- **+** Token vazado é revogável e não abre outro tenant
- **−** Revisão profunda de segurança fica para a rodada humana final
  (perfil econômico pré-release)
- **−** O bot não lê histórico nem edita mensagem; isso não é desta fatia
