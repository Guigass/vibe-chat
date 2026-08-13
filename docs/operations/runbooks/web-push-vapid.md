# Web Push — chaves VAPID (B-095 / ADR-022)

Kill switch: `Push__Enabled=false` (default). Sem isso, o worker não envia e o
cliente não pede permissão.

## Gerar um par

No repositório, o helper `VapidKeyGenerator.Create()` (infra) produz o par no
formato VAPID (P-256, base64url). Em lab:

```bash
./infra/scripts/generate-vapid-keys.sh
```

O script roda num container `mcr.microsoft.com/dotnet/sdk` e imprime as linhas
para o `.env`. **Não** redirecione a saída para um arquivo versionado.

Coloque no `.env` (nunca no git):

```text
Push__Enabled=true
Push__Vapid__PublicKey=...
Push__Vapid__PrivateKey=...
Push__Vapid__Subject=mailto:ops@example.com
```

Reinicie **api** e **worker**. `Subject`: `mailto:` de contato da instância ou a
URL pública.

## Rotação

1. Gerar um par novo.
2. Atualizar o `.env` / secret manager.
3. Reiniciar api + worker.
4. Assinaturas antigas deixam de funcionar; o usuário opta de novo.
5. Não logar a chave privada.

Rollback imediato: `Push__Enabled=false`.
