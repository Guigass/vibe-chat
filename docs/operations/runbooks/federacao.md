# Federação entre instâncias (B-065 / ADR-063)

Flag `Integrations:Federation:Enabled` (**off** por default). Com a flag
desligada as rotas respondem 404 `FederationDisabled` e o chat segue.

## Ligar

1. Defina `Integrations__Federation__InstanceId` com um id estável desta
   instalação.
2. Liste só os domínios permitidos em `Integrations__Federation__AllowedDomains__N`.
   Não use URL.
3. `Integrations__Bots__Enabled=true` (o trust usa um bot de canal).
4. `Integrations__Federation__Enabled=true` e reinicie a API.
5. O admin cria o trust, reconhece a cópia e liga a sala ao canal. O token
   `vc_int_` aparece uma vez.

## Operação

- Pause corta os dois lados sem apagar a sala. Resume volta ao ativo.
- Rotate emite a chave seguinte e ainda aceita a anterior. Retire descarta a
  anterior. Revoke desliga o bot e bloqueia tráfego novo.
- O peer assina `vibechat.federation.envelope.v1` com HMAC-SHA256 do hash do
  token. Evento inválido não grava mensagem.
- O pull (`reconcile`) é idempotente: o checkpoint não reenvia o corpo.
- Delete remoto não é garantido.

## Rollback

1. `Integrations__Federation__Enabled=false` e reinicie a API.
2. Ou `POST /api/v1/admin/workspaces/{workspaceId}/federation/trusts/{trustId}/revoke`.
3. A migration `Down` remove só as quatro tabelas `integrations.federation_*`.
   Mensagens e canais ficam.
