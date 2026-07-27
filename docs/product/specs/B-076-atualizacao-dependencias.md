# B-076 — Atualização automatizada de dependências

> Wave W7-3 · Trilha E/A · Deps: W0-7 · Decisões: D-04

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

- [ ] Atualizador abre PR de teste para ao menos um ecossistema.
- [ ] NuGet, npm, Docker e Actions estão cobertos ou têm exceção documentada.
- [ ] PR executa lint, build, testes de arquitetura, segurança e suítes relevantes.
- [ ] Majors não fazem auto-merge.
- [ ] Vulnerabilidade crítica tem fluxo de triagem explícito.
- [ ] Limite de PRs evita tempestade de updates.

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

