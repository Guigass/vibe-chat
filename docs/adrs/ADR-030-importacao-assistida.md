# ADR-030: Importação assistida para o workspace

## Status: Accepted

## Contexto

B-153 / W11 pede migrar pessoas, estrutura e histórico com dry-run, staging,
publicação explícita e rollback, sem atravessar tenants nem importar conteúdo
malicioso. A spec é R3. Não há seção própria em
`docs/architecture/pacotes-decisao-r3.md`; vale a regra comum (flag off, ADR,
threat model, rollback). Não há decisão D-* nova. B-131 (malware) ainda não
existe: a quarentena deste item é local (MIME, tamanho, checksum, zip bomb).

## Decisão

1. **Flag** `Features:Import:Enabled`, default **false** (`appsettings.json`).
   Development e o TestHost ligam. Off: qualquer rota de importação responde
   **404** `ImportDisabled`. O chat não depende da flag.
2. **Permissão** `workspace.import`, só nos papéis que já têm
   `workspace.admin` (PlatformOwner, WorkspaceOwner, Admin). Member, Auditor,
   Moderator, Guest e Bot recebem 403.
3. **Tenant** vem do workspace do ator. `tenantId` no arquivo é ignorado e
   vira aviso `TenantIgnored`. O manifesto não escolhe tenant.
4. **Papel** só `Member`, `Moderator`, `Auditor` ou `Admin`. Qualquer outro
   valor, inclusive owner de Slack/Mattermost, falha fechado com
   `ImportRoleForbidden` e não grava job.
5. **Adapters** `vibechat`, `slack`, `mattermost` e `discord` são funções puras
   que produzem `vibechat.import.v1`. Não escrevem tabelas e não chamam a
   origem. Campos sem equivalente viram aviso, não fidelidade prometida.
6. **Estados** `validated → planned → staged → published`, com `paused` e
   `rolled_back`. `plan` não grava spaces, canais nem mensagens. `execute`
   só reserva `import.id_map`. `publish` e `rollback` rodam na transação do
   request (escopo atômico = o job inteiro). Repetir `execute` ou `publish`
   não duplica.
7. **Autor externo** sem `mappedUserId` vira `identity.user_profiles` com
   subject `import:{job}:{externalId}` e e-mail sintético `@import.invalid`,
   sem membership. `mappedUserId` precisa ser membro do workspace; senão
   `ImportMappingUnknown`.
8. **Marca** de conteúdo importado é a linha em `import.id_map`
   (`resource_type = message`). Timestamp de origem vai para `CreatedAt`;
   `seq` segue essa ordem. Não há evento `MessageCreated`: histórico importado
   não dispara push, webhook nem fetch de link preview.
9. **Anexo** fora da allowlist de MIME do destino, com nome inseguro, checksum
   divergente, tamanho acima do limite ou razão zip bomb fica em quarentena:
   o payload é descartado e nenhuma linha entra em `files.attachments`.
10. **Relatório** tem contagens, avisos e conflitos. Não devolve body, e-mail,
    payload nem segredo. O JSON canônico fica só na tabela do job, sob RLS, e
    o rollback apaga esse JSON.
11. **Rollback** antes da publicação remove staging. Depois da publicação exige
    `{ "confirm": true }` e apaga somente recursos criados por este job
    (canal reutilizado não é apagado).

## Alternativas

- Worker assíncrono com checkpoint por lote: adia o caminho principal e exige
  outro processo. A cota (1000 mensagens, 1,5 MB) cabe na transação do request.
- Reusar o template B-115: não carrega histórico, autor nem anexo.
- Antivírus externo: é B-131 e pode ser R4 se depender de serviço pago.

## Rollback

`Features:Import:Enabled=false` esconde a API. A migration `AddImportPipeline`
remove só `import.jobs`, `import.id_map` e `import.historical_principals`.
Conteúdo já publicado sai pelo comando `rollback` com confirmação, não pelo
Down da migration.

## Consequências

- Operador vê o wizard em `/admin/import`.
- Quarentena não substitui B-131.
- Importação contínua/bidirecional continua fora (bridges, B-138).
