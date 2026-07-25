# B-081 — Formatação de texto

> Wave W8-3 · Trilha C/D · Deps: — · Decisões: D-11

## Problema

O corpo da mensagem é string crua renderizada com `pre-wrap`. Não dá para destacar uma
palavra, colar um trecho de código legível nem fazer uma lista. Em canal de engenharia,
bloco de código é o mínimo.

## Escopo

- Subconjunto de Markdown, deliberadamente pequeno: `**negrito**`, `*itálico*`,
  `~~riscado~~`, `` `código` ``, bloco ```` ``` ```` com linguagem opcional, `> citação`,
  lista com `-` e `1.`, link automático de URL.
- Barra de formatação no composer aplicando a marcação na seleção.
- Atalhos: `Ctrl/Cmd+B`, `I`, `Shift+E` (código), `Shift+X` (riscado).
- Prévia ao vivo do bloco de código (destaque de sintaxe) na bolha.
- Escapar HTML sempre; nada de `innerHTML` com conteúdo do usuário.
- Copiar mensagem copia o texto original em Markdown, não o HTML renderizado.

## Fora de escopo

- Editor WYSIWYG, tabela, imagem inline por Markdown, menção via Markdown (é B-082).
- Markdown em nome de canal, tópico ou descrição.

## Contratos

`Message.Body` continua sendo o texto original em Markdown — **a renderização é do
cliente**. Nada muda no schema nem no envio. Isso mantém busca FTS, export (B-046) e
auditoria (B-067) legíveis.

`contratos.md`: registrar que `body` é Markdown restrito e listar a gramática aceita.

## UX

- Barra de formatação compacta acima do textarea, só ícones com `aria-label`.
- Bloco de código com fonte mono dos tokens, fundo `--color-surface-2`, botão copiar.
- Destaque de sintaxe carregado sob demanda; sem linguagem, renderiza sem destaque.
- Markdown malformado nunca quebra o layout: cai para texto puro.

## Multi-tenant e authZ

Nada novo. Risco é de renderização, não de acesso: sanitizar é obrigatório e o teste
de XSS entra na suíte de segurança.

## Aceite

- [ ] `**a**` vira negrito na bolha e continua `**a**` no banco
- [ ] Bloco com ```` ```sql ```` renderiza com destaque e botão copiar
- [ ] `<script>alert(1)</script>` aparece como texto, sem executar
- [ ] `Ctrl+B` com texto selecionado envolve a seleção
- [ ] Busca FTS ainda encontra a palavra formatada
- [ ] Export e auditoria mostram o Markdown original

## Testes

- Unit (web): parser/renderer por regra; casos malformados; sanitização.
- Security: payloads XSS clássicos em `body` renderizados como texto.
- Integration: busca encontra termo dentro de `**termo**`.

## Riscos

- Biblioteca de Markdown pesada no bundle → escolher uma pequena e carregar o
  destaque de sintaxe sob demanda.
- Divergência entre o que o usuário digita e o que vê → a barra insere a marcação
  no texto, não um estado paralelo.
