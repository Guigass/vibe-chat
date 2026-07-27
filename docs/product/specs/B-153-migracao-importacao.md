# B-153 — Migração e importação assistida

> Wave 11 · Trilha B/C/D/E/G · Deps: B-089, B-115, B-046 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Uma organização não adota o VibeChat apenas porque o chat funciona. Ela precisa
migrar pessoas, estrutura e histórico com previsibilidade, sem atravessar tenants
nem importar conteúdo malicioso.

## Escopo

- Manifesto canônico versionado de importação.
- Importar users externos como mappings/pending profiles, nunca como credenciais.
- Importar spaces, channels, memberships, mensagens, threads e anexos.
- Adapters iniciais para exports suportados de Slack, Mattermost e Discord.
- Dry-run com inventário, mapping, conflitos, estimativa de storage e warnings.
- Execução em staging area com checkpoint, pause/resume e retry idempotente.
- Preservar timestamp/autor/origem quando possível, sempre marcando conteúdo
  importado.
- Mapa de IDs externos para IDs canônicos.
- Relatório final com importados, ignorados, redigidos e falhas.
- Rollback antes da publicação; depois da publicação, remoção por import batch
  com confirmação e audit.

## Fora de escopo

- Pedir senha de usuário da plataforma de origem.
- Copiar tokens, apps, workflows ou secrets.
- Prometer fidelidade para feature sem equivalente.
- Importação contínua/bidirecional; isso pertence a bridges.
- Burlar API, criptografia ou termos da origem.

## Contratos

Formato `vibechat.import.v1`:

- manifest, source e adapter version;
- tenant/workspace alvo resolvido pelo contexto;
- principals/mappings;
- resources com external IDs;
- batches/checkpoints;
- attachment inventory/checksums;
- warnings e policy decisions.

Comandos `validate`, `plan`, `execute`, `pause`, `resume`, `publish` e
`rollback`. Cada import possui ID, estado, versão e idempotency key. Adapter
produz modelo canônico; não escreve diretamente em tabelas internas.

## UX

Wizard administrativo:

1. escolher origem/arquivo;
2. validar e escanear;
3. mapear pessoas/canais;
4. revisar conflitos e conteúdo não suportado;
5. executar com progresso;
6. conferir amostra;
7. publicar ou reverter;
8. baixar relatório sanitizado.

Estado parcial é claro. Fechar o navegador não cancela o job.

## Multi-tenant e authZ

Exige capability `workspace.import`. Import fica preso ao tenant/workspace do
ator; manifest não escolhe `tenant_id`. Staging, object keys, checkpoints e
relatórios usam namespace próprio. Mapping nunca eleva papel acima da allowlist.

Conteúdo passa pelas mesmas regras de tamanho, MIME, malware, DLP e retenção do
destino. Autor externo não autenticado vira identidade histórica marcada, sem
membership ativa automática.

## Aceite

- [ ] Dry-run não altera domain state.
- [ ] Retry/resume não duplica recurso nem mensagem.
- [ ] Tenant do manifest não redireciona importação.
- [ ] Mapping de papel proibido falha fechado.
- [ ] Threads, timestamps e autoria suportada são preservados.
- [ ] Anexo malicioso permanece em quarentena.
- [ ] Publicação é atômica por batch/escopo documentado.
- [ ] Relatório não contém secret nem body desnecessário.
- [ ] Rollback pré-publicação remove staging e reservas.

## Testes

- Golden exports por adapter e versão.
- Arquivo truncado, schema desconhecido e encoding.
- Mapping duplicado/ambíguo.
- Checkpoint, crash, retry e cancel.
- Cross-tenant e mass assignment.
- Zip bomb/MIME spoofing/malware.
- Import grande com quotas.
- E2E dry-run → execute → publish.

## Riscos

- Corrupção ou duplicação de histórico.
- Conteúdo hostil em export.
- Autor/membership incorretos.
- Storage inesperado.
- Drift quando formato da origem mudar.

Mitigar com staging, adapters versionados, dry-run obrigatório, checksums,
checkpoint idempotente, quotas, amostragem e publicação explícita.
