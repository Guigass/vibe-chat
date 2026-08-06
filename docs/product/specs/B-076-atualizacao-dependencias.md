# B-076 — Atualização automatizada de dependências

> Wave W7-3 · Trilha E/A · Deps: W0-7 · Decisões: D-04 · Risco R2

## Problema

NuGet, npm, imagens Docker e GitHub Actions não têm um fluxo automatizado de
detecção e proposta de atualização. O job atual de auditoria é informativo e
não reduz sozinho a janela de exposição.

## Escopo

- Configurar um único mecanismo OSS de atualização automatizada.
- Cobrir NuGet, npm, Docker e GitHub Actions.
- Agrupar patches compatíveis e limitar PRs simultâneos.
- Executar CI completa nos PRs gerados.
- Definir política para vulnerabilidade crítica, major upgrade e dependência
  abandonada.
- Documentar triagem, SLA interno e rollback.

## Fora de escopo

- Auto-merge irrestrito.
- Atualização major sem revisão humana.
- Ferramenta proprietária obrigatória.
- Alterar arquitetura para acomodar uma dependência.

## Contratos

Não muda API de produto. Muda o contrato operacional da CI e deve atualizar
`CONTRIBUTING.md` e os guias de manutenção quando implementado.

## UX

Não aplicável à UI do produto.

## Multi-tenant e authZ

Sem dado de tenant. Tokens do bot usam permissões mínimas; nunca são gravados em
arquivo versionado nem expostos em logs.

## Aceite

- [x] Atualizador abre PR de teste para ao menos um ecossistema —
  github-actions via [#87](https://github.com/Guigass/vibe-chat/pull/87)
  (`actions/checkout` v4→v7); também #85/#88/#89/#90.
- [x] NuGet, npm, Docker e Actions estão cobertos ou têm exceção documentada —
  `.github/dependabot.yml` + exceção Docker pins em
  [`dependencias.md`](../../operations/dependencias.md).
- [x] PR executa lint, build, testes de arquitetura, segurança e suítes relevantes —
  CI completa nos PRs Dependabot (evidência #87).
- [x] Majors não fazem auto-merge — política em `dependencias.md`; majors em PR
  separado.
- [x] Vulnerabilidade crítica tem fluxo de triagem explícito —
  tabela + SLA em `dependencias.md`.
- [x] Limite de PRs evita tempestade de updates —
  `open-pull-requests-limit` 3–5 por ecossistema.

## Testes

- Validar sintaxe/configuração do atualizador na CI.
- Executar um dry-run ou PR controlado.
- Demonstrar que falha de teste impede merge.
- Verificar que nenhum secret aparece nos logs.

## Riscos

- Ruído excessivo: agrupar patches e limitar concorrência.
- Update quebrar faixa do Angular/Node: respeitar o piso documentado e rodar build.
- Imagem Docker mutável: preservar pins explícitos.
- Supply-chain compromise: revisar provenance e changelog antes de major.

