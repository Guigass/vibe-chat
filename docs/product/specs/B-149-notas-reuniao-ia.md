# B-149 — Transcrição e notas de reunião

> Wave 17 · Trilha C/D/AI/E · Deps: B-147, D-22 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Participantes perdem decisões e ações de reuniões, mas transcrição envolve voz,
PII, consentimento e custo de IA.

## Escopo

- Opt-in por sessão com consentimento explícito.
- Transcription provider-neutral, local preferido quando disponível.
- Transcript segmentado por tempo/speaker quando suportado.
- Summary, decisions e action item suggestions com citações temporais.
- Confirmação humana antes de publicar decisões/tasks.
- Retention, delete, export, budget e audit.

## Fora de escopo

- Transcrever sem consentimento.
- Reconhecimento biométrico de speaker.
- Criar decisão/task automaticamente sem confirmação.

## Contratos

Transcript/Note ligados à LiveSession/Recording; job assíncrono; source timestamps;
provider recebe somente media autorizada e policy metadata.

## UX

Indicator de transcription, cancel/delete, revisão e publicação. Confidence/erro
visíveis; nota linka ao trecho.

## Multi-tenant e authZ

ACL da sessão/conversa; provider external opt-in; transcript não indexa em RAG
sem policy separada.

## Aceite

- [ ] Sem consentimento, nenhum áudio é enviado.
- [ ] Nota tem citações temporais.
- [ ] AI failure preserva gravação conforme policy.
- [ ] Delete propaga ao provider/projeções quando suportado.
- [ ] Budget e audit funcionam.

## Testes

Fake transcription/LLM, consent, cancellation/delete, ACL, budget e E2E review.

## Riscos

PII e hallucination. Consent, citations, confirmação humana de artefatos e
provider contract.

