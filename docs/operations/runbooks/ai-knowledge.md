# Fontes de conhecimento para bots

A flag `Features:BotKnowledge:Enabled` nasce **false**. Com ela desligada,
criar, pausar e perguntar respondem 404 `BotKnowledgeDisabled`. O chat e a
busca FTS seguem. O Qdrant continua no profile Compose `ai`, sem porta
publicada. Sem `SemanticSearch:QdrantUrl`, a resposta sai do Postgres.

## Operar

1. O admin envia arquivo, página ou URL HTTPS. A allowlist fica em
   `Features:BotKnowledge:UrlAllowlist`. Vazia, toda URL é recusada.
2. Arquivo e URL passam pelo scan. EICAR fica `Failed` /
   `MalwareQuarantined` e não grava trecho.
3. URL para loopback, rede privada, link-local ou metadata fica
   `EgressBlocked`, inclusive depois de redirect.
4. `Ready` mostra contagem e checksum. A mesma texto produz o mesmo checksum.
5. O grant liga a fonte a um bot do workspace. Revogar tira a citação na hora.
6. Remover zera trecho e embedding e devolve `evidenceChecksum`.

## Falha

- Qdrant fora: a fonte permanece `Ready` e o job fica `QdrantDeferred`.
- 404 `KnowledgePageNotFound` ou `KnowledgeSourceNotFound`: o id não é deste
  workspace. A resposta não traz o texto.
- 403: quem não tem `ai.knowledge.manage`.

## Rollback

Desligar a flag. O chat não lê essas tabelas. Parar o profile `ai` descarta
só a projeção. As linhas no Postgres permanecem até um purge explícito.
