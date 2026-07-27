# B-078 — Limite de tamanho do body de mensagem

> Wave W7-5 · Trilha C/E · Deps: W2-1 · Decisões: — · Risco R2

## Problema

O banco limita `Message.Body` a 8.000 caracteres, mas a entrada HTTP não rejeita
o excesso antes da persistência. Um payload grande pode virar 500, piorar a
experiência e ampliar risco de abuso.

## Escopo

- Definir 8.000 caracteres como limite canônico do body textual.
- Validar antes da transação nos endpoints de canal e thread.
- Retornar erro 400 estável e útil.
- Manter mensagem vazia válida somente quando houver anexo pronto.
- Expor contador/estado de limite no composer.
- Aplicar limite equivalente a edit e integrações futuras.

## Fora de escopo

- Limite global de request do proxy.
- Alterar o tamanho da coluna.
- Truncar silenciosamente.
- Contabilizar bytes de anexos como body.

## Contratos

Erro esperado:

```json
{
  "error": "MessageBodyTooLong",
  "message": "A mensagem excede o limite de 8000 caracteres.",
  "maxLength": 8000
}
```

Quando implementado, atualizar `architecture/contratos.md` para envio, reply e
edição.

## UX

Composer mostra contagem próximo ao limite, impede submit inválido e preserva o
rascunho. Erro do servidor continua sendo tratado caso outro cliente ignore a
validação local.

## Multi-tenant e authZ

Validação ocorre sem consultar outro tenant e antes de trabalho caro. Rate-limit
e authZ continuam obrigatórios; tamanho válido não concede permissão.

## Aceite

- [ ] 8.000 caracteres são aceitos.
- [ ] 8.001 caracteres retornam 400, nunca 500.
- [ ] Canal, thread e edição têm a mesma regra.
- [ ] Body vazio sem anexo é rejeitado.
- [ ] Body vazio com anexo pronto é aceito.
- [ ] UI informa o limite e preserva o texto.

## Testes

- Unitários nos limites 0, 1, 8.000 e 8.001.
- Integração para send, reply e edit.
- Regressão garantindo ausência de outbox/mensagem em request inválido.
- Caso cross-tenant continua negado independentemente do tamanho.

## Riscos

- Divergência entre contagem JavaScript e .NET para Unicode; especificar e testar
  a unidade de contagem adotada antes da implementação.
- Validação apenas no frontend; servidor é a autoridade.
- Integrações futuras ignorarem a regra; reutilizar política central.

