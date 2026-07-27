# B-122 — Digests e catch-up programado

> Wave 12 · Trilha C/D · Deps: B-117 · Risco R3
> Requisitos comuns: [Waves 11–17](long-term-common.md)

## Problema

Após ausência, o usuário precisa percorrer muitas conversas para descobrir o que
mudou e o que requer ação.

## Escopo

- Digest pessoal diário/semanal e catch-up sob demanda.
- Seleção determinística por unread, menção, anúncio, decisão e task.
- Resumo IA opcional quando B-121 estiver habilitado.
- Citações para mensagens/fontes.
- Preferência de canais, horário, timezone e meio de entrega.
- Estado de geração, partial failure e retry.

## Fora de escopo

- Enviar conteúdo sensível por e-mail sem política.
- Marcar fontes como lidas automaticamente.
- Digest global para admin vigiar usuários.

## Contratos

Schedule pessoal + job idempotente; snapshot de IDs, não body duplicado;
`digest.generated` audit/metric sem conteúdo.

## UX

Página de catch-up com seções, fontes e “abrir conversa”. Indica quando IA está
off ou falhou e oferece versão determinística.

## Multi-tenant e authZ

ACL revalidada na geração e abertura. Entrega externa usa preview mínimo
configurável.

## Aceite

- [ ] Digest sem IA funciona.
- [ ] IA falha sem perder lista determinística.
- [ ] Fonte revogada não aparece.
- [ ] Timezone/DND respeitados.
- [ ] Geração repetida não duplica entrega.

## Testes

Clock/schedule, fake AI, ACL revocation, email/push privacy e E2E catch-up.

## Riscos

Digest virar canal de exfiltração. Minimizar payload externo e revalidar acesso
no instante da geração. O caminho determinístico sem IA tem risco operacional
R2, mas a capacidade inteira permanece classificada no maior risco aplicável
porque o resumo por IA faz parte do escopo opcional.

