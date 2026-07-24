# Contribuindo para o VibeChat

Obrigado por contribuir. Este guia resume o fluxo esperado para humanos e agentes.

## Antes de começar

1. Leia `docs/product/visao.md`, `docs/product/glossario.md` e `AGENTS.md`.
2. Consulte ADRs em `docs/adrs/` e contratos em `docs/architecture/contratos.md`.
3. Verifique `docs/roadmap/decisoes-pendentes.md` — itens D-* não devem ser decididos unilateralmente.

## Ambiente local

```bash
task setup
task dev
task seed
```

Detalhes em `docs/operations/desenvolvimento.md`.

## Fluxo de contribuição

1. Crie uma branch a partir da branch de trabalho atual / `main`.
2. Implemente no módulo correto; atualize contratos e docs se a superfície pública mudar.
3. Adicione ou atualize testes (unit / architecture / integration / security / e2e conforme o impacto).
4. Rode a verificação relevante:

```bash
task lint
task test
task test:architecture
# se tocar persistência / auth:
task test:integration
task test:security
```

5. Abra um PR pequeno e revisável, com evidência de funcionamento.

## Convenções

- **Docs / ops:** português (PT-BR)
- **Código:** inglês (tipos, APIs, ids)
- Sem microserviços, Kafka, OpenSearch ou Kubernetes “por precaução” (ver ADRs)
- Sem secrets no repositório; use `.env.example`
- Preserve isolamento multi-tenant (RLS + authZ)

## Checklist de PR

- [ ] Escopo claro; sem mudanças arquiteturais sem ADR
- [ ] Testes da trilha passando
- [ ] Docs atualizadas quando necessário
- [ ] Sem secrets / credenciais reais
- [ ] Evidência (logs CI, screenshots, trace ids)

## Código de conduta

Ver `CODE_OF_CONDUCT.md`.

## Dúvidas

Prefira issues/discussões no repositório. Decisões de licença, marca e retenção ficam com o owner (D-01+).
