# Qualidade e Rastreabilidade Documental

Contrato de integridade para o conjunto de documentos que orienta agentes,
revisores e operadores. Este arquivo define o que deve ser validado; não substitui
evidência do código nem autoriza marcar feature como `Done`.

## Baseline auditada

Snapshot de 2026-07-27, após D-28:

| Controle | Resultado |
|----------|-----------|
| Decisões de produto | D-01…D-28, 28 IDs únicos, nenhuma aberta |
| Itens de produto `Planned` | 97 entre W7 e W19 (B-186 em W10-15 — membros do canal; B-184 em W9-11 — nav esquerda; B-185 em W11 — personalização visual; B-178…B-183 em W19 — organização do código; B-173/W9-10 Done; B-172 em W16 — backup de chat/destinos; B-171 em W9-9; B-170 em W16; B-169 em W14; B-168 em W9-8; B-167 em W11; B-165/W7-9 Done em 2026-08-10) |
| Specs de itens `Planned` | correspondência 1:1 |
| IDs `Planned` sem spec | 0 |
| Specs sem item `Planned` | 0 |
| IDs `Planned` duplicados | 0 |
| Specs com classe R0–R3 | 1:1 com Planned |
| Seções obrigatórias por spec | 1:1 com Planned |
| Findings UX abertos sem rota | 0 |
| Links locais | 446 verificados em 170 arquivos Markdown/MDC; 0 quebrados |
| `docker compose config --quiet` com `.env.example` | válido; profiles `apps`, `observability`, `proxy`, `tools` |

O comando de Compose emitiu apenas aviso local de acesso ao
`~/.docker/config.json`; o parse do projeto terminou com código zero. Isso prova
sintaxe e interpolação, não funcionamento dos containers nem validade de secrets.

## Invariantes

### IDs e estados

- D-* não é reutilizado nem renumerado.
- B-* aparece em no máximo uma linha `Planned`.
- W* identifica posição de execução; B-* identifica a capacidade.
- `Done` exige referência a implementação, teste, migration, configuração ou PR.
- `Moved` aponta para o destino.
- `Superseded` aponta para o sucessor.
- `External action` não bloqueia trilha independente.

### Rastreabilidade

Todo item `Planned` precisa de:

1. B-ID estável;
2. wave e trilha;
3. dependências;
4. link para spec existente;
5. classe R0–R3;
6. critérios de aceite e testes;
7. decisões D-* aplicáveis;
8. contratos, authZ e riscos explícitos.

Toda spec precisa das seções:

- Problema;
- Escopo;
- Fora de escopo;
- Contratos;
- UX;
- Multi-tenant e authZ;
- Aceite;
- Testes;
- Riscos.

Títulos estendidos como “Contratos e dados” não devem ser usados: verificadores
podem tratar a seção como ausente.

### Links

- Links locais são relativos ao arquivo.
- Todo link local aponta para arquivo ou diretório existente.
- Âncora deve existir no destino quando o checker suportar headings.
- URLs externas em pesquisa são evidência, não contrato de disponibilidade.
- Assets locais usam o inventário do design system.

### Decisões e arquitetura

- D-* prevalece sobre ADR, spec e roadmap.
- Arquitetura nova exige ADR; ADR não pode contrariar D-*.
- ADR 015–017 depende de gatilho medido.
- Pacotes R3 em `architecture/pacotes-decisao-r3.md` são defaults; o ADR final
  registra a escolha técnica compatível.

## Contrato do verificador automático (DOC-006)

Quando a fase de código autorizar automação documental, o checker deve ser
determinístico, offline e executável em Windows/Linux.

### Entradas

- `docs/roadmap/roadmap.md`;
- `docs/roadmap/horizonte-ambicioso.md`;
- `docs/roadmap/backlog.md`;
- `docs/roadmap/decisoes-pendentes.md`;
- `docs/product/specs/`;
- `docs/product/ux-findings.md`;
- `docs/product/bug-findings.md`;
- todos os arquivos Markdown/MDC para links.

### Falhas bloqueantes

- marcador de conflito Git;
- link local quebrado;
- B-ID `Planned` duplicado ou sem spec;
- spec órfã de item `Planned`;
- seção obrigatória ausente;
- classe de risco ausente;
- dependência apontando para ID inexistente;
- decisão referenciada inexistente;
- status desconhecido;
- tabela de wave sem coluna `Status`;
- `Done` sem evidência textual rastreável;
- finding `Alta` sem work item ou safety lane;
- secret provável em documento versionado.

### Saída

Formato de saída:

```text
DOC-CHECK PASS
decisions=28
planned=81
specs=81
links=<n>
warnings=<n>
```

Em falha:

```text
DOC-CHECK FAIL
rule=<stable-rule-id>
file=<relative-path>
line=<line>
message=<actionable explanation>
```

O processo retorna `0` em sucesso e código não zero em falha. Não corrige
automaticamente status ou decisões.

## Revisão após merge

A automação Docs:

1. confirma o merge e o SHA;
2. atualiza status e evidência do item fechado;
3. atualiza `estado-atual.md`;
4. fecha finding relacionado;
5. libera lease;
6. executa a auditoria documental;
7. abre PR R0 separado se a reconciliação não couber no fechamento.

## Pendências executáveis honestas

Este contrato e a baseline documental estão concluídos. Permanecem fora desta
entrega exclusivamente documental:

- implementar o checker e ligá-lo à CI (`OPS-DOC-CHECKER`);
- alinhar Compose/template nos gaps de B-105;
- comprovar serviços opt-in dentro dos containers;
- configurar required checks e permissões no GitHub
  (`OPS-QA-AUDIT`, `OPS-REQUIRED-CHECK`, `SEC-REVIEW-TEMPLATE`).

Essas pendências não podem ser marcadas como feitas apenas porque o comportamento
esperado está bem documentado.
