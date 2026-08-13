# ADR-020: Settings runtime e credenciais externas criptografadas

## Status: Accepted

## Contexto

`/admin/settings` (B-069) já persiste políticas de AI, SMTP não-secreto, webhooks e
retenção. Credenciais de OpenRouter e SMTP ficavam só em env; o secret de webhook
ficava em texto claro no banco. Operadores querem rotacionar integrações externas
pela UI sem redeploy, sem mover secrets de infraestrutura (Postgres, Redis,
Keycloak, MinIO) para o banco.

Isso emenda:

- **ADR-012** — “segredos de API só em env/secret store” passa a permitir
  OpenRouter API key criptografada por workspace no PostgreSQL;
- **D-04** — secrets de **infraestrutura/bootstrap** continuam só em
  `.env`/secret manager; credenciais de **integrações externas** podem ir ao DB
  com envelope AES-GCM e chave mestra no env;
- **B-069 / R-17** — PUT geral não aceita secrets; rotação usa endpoints
  dedicados; respostas só máscara.

## Decisão

1. **Settings tipados** (sem registry chave/valor):
   - `AiSettings` (workspace): flags + envelope OpenRouter;
   - `TenantEmailSettings` (tenant): SMTP + envelope senha;
   - `OutboundWebhookEndpoint` (tenant): URL + envelope signing secret;
   - `MessageRetentionSettings` (tenant): política de purge;
   - `TenantFilesSettings` (tenant): limites de anexos;
   - `TenantRateLimitSettings` (tenant): `SendPerMinute` / `HubPerMinute`.

2. **Criptografia AES-256-GCM** (`System.Security.Cryptography` apenas):
   - keyring versionado no env (`RuntimeSettings:Encryption:ActiveKeyVersion` +
     `Keys:{n}`);
   - envelope: ciphertext, nonce (12), tag (16), keyVersion, formatVersion,
     maskSuffix, rotatedAt;
   - AAD: `vibechat/runtime-secret/v1|{kind}|{tenant}|{workspace}|{entity}`;
   - nunca retornar/logar plaintext, ciphertext, nonce ou tag.

3. **Precedência**: kill switch/teto do env → override DB → fallback env (só se
   não houver envelope) → default seguro. Envelope inválido falha fechado.

4. **Feature flag** `RuntimeSettings:DatabaseOverridesEnabled` (default `false`).
   Com flag off, consumidores usam comportamento legado/env; envelopes já
   gravados continuam legíveis pelo binário novo.

5. **AuthZ**: `workspace.admin` em GET/PUT settings, rotate e reencrypt.
   Auditor/membro → 403. RLS FORCE nas novas tabelas.

6. **Fora de escopo no DB**: connection strings, OIDC, MinIO keys, Redis,
   CORS/proxy/TLS, seed, OTel, BaseUrl OpenRouter, kill switches
   `Ai:Enabled` / `MessageRetention:Enabled`.

## Alternativas consideradas

| Alternativa | Motivo de rejeição |
|-------------|-------------------|
| ASP.NET Data Protection com key ring em volume | Menos explícito para self-host multi-nó; keyring env é operacionalmente simples |
| Registry genérico `settings[key]=value` | Perde tipagem, RLS por domínio e validação |
| Secrets plaintext + RLS | Violação de defesa em profundidade (backups/DBA) |
| Mover Postgres/OIDC/MinIO para o DB | Lockout/indisponibilidade; viola D-04 infra |
| Enxugar `.env.example` dumpando config no DB | Mesmo lockout; B-187 enxuga o **template**, não a camada de infra |

## Rollback

1. Desligar `RuntimeSettings:DatabaseOverridesEnabled` no mesmo binário.
2. Downgrade para binário anterior **após** gravar envelopes **não** é seguro.
3. Remoção da coluna plaintext de webhook (`Secret`) só após release de
   observação com zero dual-read legado.

## Consequências

- **+** Rotação de OpenRouter/SMTP/webhook sem redeploy; Files/RateLimit por tenant
- **+** Chave mestra fora do banco; re-encryption por versão
- **−** Operador deve provisionar keyring antes de habilitar a flag
- **−** `workspace.admin` continua podendo alterar política **tenant-wide**
  (email/webhook/files/rate) — documentado e auditado
- Template `.env` de setup vs catálogo operacional: follow-up **B-187** (não
  amplia o que entra no DB)
