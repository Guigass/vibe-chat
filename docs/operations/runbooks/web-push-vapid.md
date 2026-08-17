# Web Push — chaves VAPID (B-095 / ADR-022)

Kill switch e chaves VAPID: `/admin/settings` quando overrides on (B-187).
Sem o gate (ou sem VAPID válido) o worker não envia e o cliente não pede
permissão. Keyring continua só no env.

## Gerar um par

No repositório, o helper `VapidKeyGenerator.Create()` (infra) produz o par no
formato VAPID (P-256, base64url). Em lab:

```bash
./infra/scripts/generate-vapid-keys.sh
```

O script roda num container `mcr.microsoft.com/dotnet/sdk` e imprime o par.
**Não** redirecione a saída para um arquivo versionado.

Cole em `/admin/settings` (Substituir VAPID) — nunca no git:

```text
PublicKey=...
PrivateKey=...
Subject=mailto:ops@example.com
```

`Subject`: `mailto:` de contato da instância ou a URL pública.

## Rotação

1. Gerar um par novo.
2. Rotate em `/admin/settings` (não precisa reiniciar).
3. Assinaturas antigas deixam de funcionar; o usuário opta de novo.
4. Não logar a chave privada.

Rollback imediato: desligar o kill switch no admin **ou**
`RuntimeSettings:DatabaseOverridesEnabled=false` (volta ao default: push off).
