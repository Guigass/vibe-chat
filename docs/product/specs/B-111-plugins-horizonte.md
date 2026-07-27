# B-111 — Horizonte: capabilities avançadas de plugins (após B-066)

> P3 (follow-up) · Trilha B/C/D · Deps: B-066 · Decisões: D-11

## Papel deste item

Checklist de **horizonte** depois de B-066. Não é elegível ao Build enquanto
B-066 não estiver Done. Serve para não misturar “plugins legais” com a fatia
mínima (B-109 → B-110).

## Candidatos (por último na trilha)

| Capability / tema | Notas |
|-------------------|--------|
| Slash namespaced estável | Conflito com B-087; prefixo `pluginId:` |
| Interactive messages / botões | Exige contrato de action URL + authZ |
| Leitura limitada de histórico pelo plugin | Opt-in; membership + rate-limit |
| Anexos via API de integração | Depois de texto estável |
| Catálogo built-in ampliado no repo | Mais oficiais; ainda sem registry remoto |
| Registry URL opcional | **Bloqueado** até nova decisão D-* |

## Fora de escopo permanente (até D-* nova)

- App Directory público / marketplace comercial
- Execução de código não confiável in-process

## Quando abrir spec completa

Só após B-066 mergeado e demanda real de uma capability da tabela — então
promover linha a B-11x com spec própria (não implementar este arquivo como
escopo único).
