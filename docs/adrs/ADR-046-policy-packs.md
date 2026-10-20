# ADR-046 — Policy packs

- Status: Accepted
- Data: 2026-10-20
- Relacionado: B-133, B-128, B-130, D-23, ADR-009, ADR-010, ADR-044

## Contexto

Configurar auth, compartilhamento, guests, retenção, IA, DLP, uploads e
auditoria um a um erra fácil e não se compara entre ambientes. D-23 pede
controles alinhados a LGPD/GDPR e SOC 2/ISO 27001, sem alegar certificação.
O pacote R3 diz que um policy pack nunca reduz controle obrigatório em silêncio.

## Decisão

1. Flag `Features:PolicyPacks:Enabled` nasce **false**. Desligada, a API
   responde 404 `PolicyPackDisabled` e o convite de guest não consulta pack.
2. Três manifestos `policy-pack.v1` no código, sem id de tenant e sem
   segredo: `small-team`, `enterprise-baseline`, `regulated`, todos na versão 1.
   O rótulo é baseline técnica (`certification: false`), não selo.
3. Uma linha por workspace em `compliance.policy_assignments`, com `TenantId`,
   RLS FORCE e WITH CHECK. O preview devolve o mesmo documento que o apply grava.
4. `compliance.policy` não entra no catálogo de papéis. `workspace.admin`
   nomeia outra pessoa. Sem a nomeação, o pack não muda.
5. Controle locked só aceita override igual ou mais restrito. Auditoria de
   conteúdo não pode ir para `false`. Override proibido responde 409
   `PolicyPackOverrideForbidden`.
6. Apply e import exigem `confirm: true` e `Idempotency-Key`. A mesma chave
   com o mesmo pedido devolve o documento gravado. Chave repetida com outro
   pedido responde 409. Import com `tenantId`, segredo ou código executável
   responde 400 e não grava.
7. O documento efetivo escreve os controles que já existem: IA do workspace,
   retenção e teto de upload do tenant, simulação de DLP e auditoria de
   conteúdo (só liga, nunca desliga por pack). `guests.enabled=false` recusa
   convite novo. Senha mínima e encaminhamento externo ficam no documento
   como baseline, com `enforced: false`, porque o realm e a federação não
   têm escritor aqui.
8. Versão nova do catálogo não se aplica sozinha. O drift marca
   `upgradeAvailable` e a diferença entre o documento e o valor vivo.
   Rollback só existe quando há versão anterior; senão 409
   `PolicyPackRollbackUnsafe`.
9. Export percorre o JSON e remove chave de segredo. A auditoria guarda
   before/after dos controles, sem segredo.

## Alternativas

- Aplicar o pack em silêncio no boot: a spec proíbe.
- Tratar o nome do pack como certificação: a spec proíbe.
- Deixar `workspace.admin` aplicar direto: junta operação e política.

## Consequências

- Rollback da API: `Features:PolicyPacks:Enabled=false`. A linha gravada
  permanece. Convite e demais efeitos deixam de consultar o pack.
- A migration `AddPolicyPacks` cria a tabela. Não há dado de produção para
  voltar atrás; dropar a tabela só com o schema vazio.
- UI em `/compliance`: simular, aplicar, restaurar, drift e export. O texto
  diz que não é selo.
- Não há parecer jurídico nem certificação.
