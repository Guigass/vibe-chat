# ADR-044 — Classificação de dados e DLP

- Status: Accepted
- Data: 2026-10-20
- Relacionado: B-130, B-129, D-23, ADR-009, ADR-010, ADR-042, ADR-043

## Contexto

Organizações precisam rotular sensibilidade e impedir ações incompatíveis
(compartilhar, convidar guest, exportar, webhook, IA, download, federação)
sem enviar o conteúdo a um detector SaaS. D-23 pede controles alinhados a
LGPD/GDPR e SOC 2/ISO 27001, sem alegar certificação. O pacote R3 de
compliance manda DLP local e capability separada de `workspace.admin`.

## Decisão

1. Flag `Features:Dlp:Enabled` nasce **false**. Desligada, a API de DLP
   responde 404 `DlpDisabled` e o gate não altera share, guest, export,
   webhook, IA nem download.
2. Tabelas em `compliance` (`dlp_policies`, `data_labels`,
   `label_assignments`, `dlp_rules`, `dlp_detectors`, `dlp_exceptions`,
   `dlp_false_positives`, `dlp_decisions`), com `TenantId`, RLS FORCE e
   WITH CHECK. A decisão não guarda trecho do body.
3. Rótulos built-in: `public` (0), `internal` (1), `confidential` (2),
   `restricted` (3). Rótulo custom tem chave própria e rank 0–100.
   Herança: canal, senão space, senão workspace. Override que baixa o rank
   do pai recebe 409 `DlpDowngradeBlocked`.
4. `compliance.dlp` não entra no catálogo de papéis. `workspace.admin`
   nomeia outra pessoa. Sem a nomeação, a política não muda.
5. Uma regra por ação, com rank mínimo e efeito `warn`, `block` ou
   `quarantine`. Sem linha de política, o workspace fica em **simulação**:
   a decisão é medida e o efeito externo segue. Enforce é explícito.
6. Detector é regex local com timeout de 100 ms. Não altera o body. Timeout
   ou padrão inválido não eleva o rank. Falso positivo revisado ignora
   aquele detector naquele assunto.
7. Exceção tem prazo máximo de 30 dias, escopo e ação. Expirada não cobre.
   A criação é auditada, sem body.
8. O gate roda antes do efeito externo: encaminhar não grava outbox;
   convite, export, IA e download não seguem; webhook não faz HTTP.
   Federação ainda não tem escritor (B-065); o mesmo `EvaluateAsync`
   (`federation`) é o gancho obrigatório antes de qualquer escrita externa.
9. Canal `ConfidentialE2EE` (e o preview com `inspectable: false`) não é
   inspecionado. O motivo é `NotInspectable`. Com auditoria de conteúdo
   desligada, o detector também não lê o body.
10. Quarentena de DLP segura o efeito e grava a decisão. Não copia bytes
    para outro bucket — isso continua no scan de malware (B-131).

## Alternativas

- Detector SaaS por default: a spec proíbe envio de conteúdo.
- `workspace.admin` altera a política direto: junta operação e classificação.
- Enforce ao ligar a flag: o primeiro boot bloquearia o workspace sem
  simulação prévia.

## Consequências

- Rollback da API: `Features:Dlp:Enabled=false`. Decisões já gravadas
  permanecem. Efeitos novos deixam de passar pelo gate.
- A migration `AddDataClassification` cria as tabelas. Não há dado de
  produção para voltar atrás; dropar as tabelas só com o schema vazio.
- UI: rótulo no cabeçalho da conversa; bloqueio devolve `DlpBlocked` ou
  `DlpQuarantined` com `review`. Admin simula em `/compliance`.
- Não há parecer jurídico, certificação, nem inspeção de E2EE.
