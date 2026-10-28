# ADR-064 — Canais confidenciais com HPKE

- Status: Accepted
- Data: 2026-10-27
- Relacionado: B-064, B-129, B-169, D-26, ADR-043

## Contexto

D-26 pede canais opt-in em que o servidor não lê o body. B-169 já bloqueia
`ConfidentialE2EE` enquanto `contentAuditEnabled=true`. O pacote R3 manda
avaliar MLS (RFC 9420) antes de qualquer protocolo caseiro e usar biblioteca
auditada.

## Decisão

1. Flag `Messaging:E2ee:Enabled` nasce false. Desligada, criar o canal responde
   400 `ConfidentialE2eeUnavailable` e as rotas `/e2ee/*` respondem 404
   `E2eeDisabled`. O chat comum não depende desta flag.
2. **Por que não MLS nesta fatia.** OpenMLS e mlspp são as implementações
   maduras de RFC 9420. Nenhuma publica pacote auditado para o toolchain deste
   repositório (SDK .NET + Angular, sem Rust/WASM nem Delivery Service novo).
   Implementar TreeKEM aqui seria protocolo próprio, o que a spec proíbe.
3. **Protocolo.** O cliente usa `hpke-js` (HPKE base mode, RFC 9180) com a
   suíte DHKEM(P-256, HKDF-SHA256) / HKDF-SHA256 / AES-256-GCM. Cada mensagem
   é selada para a chave pública de cada dispositivo ativo. O servidor valida
   só a forma do envelope `v: 1` e exige que o conjunto de `kid` seja exatamente
   o dos dispositivos ativos naquele `epoch`. Não decifra.
4. **Epoch.** Membership e registro/revogação de dispositivo incrementam
   `E2eeGeneration`. Mensagem com epoch velho responde `E2eeEpochStale`.
   Quem entra recebe `JoinedSeq` no último seq e não ganha envelope antigo.
   Quem sai deixa de estar no conjunto de `kid` e perde o acesso HTTP.
5. **Backup.** O cliente cifra a chave privada com PBKDF2-SHA256 e AES-GCM.
   O servidor guarda o blob e devolve `E2eeBackupNoEscrow`. Não há chave da
   organização.
6. **Verificação.** A impressão é SHA-256 de `kty|crv|x|y`. O usuário marca
   o dispositivo; a marca não substitui membership.
7. **Capacidades.** Busca, IA, DLP, moderação, legal hold do body, prévia,
   transcrição, enquete, agendamento e encaminhamento ficam de fora. Push e
   índice usam corpo vazio quando `e2ee` é true.
8. **AuthZ.** Rotas exigem `message.read`. O tenant vem da membership. RLS
   FORCE em `messaging.e2ee_devices`, `e2ee_backups` e `e2ee_verifications`.

## Alternativas

- MLS via OpenMLS/WASM: é o candidato de longo prazo, mas pede toolchain e
  serviço de entrega que esta fatia não introduz.
- Cifra simétrica inventada no servidor: o servidor leria o body. Rejeitada.

## Rollback

1. `Messaging:E2ee:Enabled=false`. Canais novos não nascem. Mensagens já
   cifradas continuam ilegíveis no servidor.
2. `Down` remove `E2eeGeneration` e as três tabelas. Não há plaintext para
   restaurar.

## Consequências

- **+** Log, outbox, busca, IA e export não recebem o texto claro.
- **+** Cliente sem chave ou com envelope inválido falha fechado.
- **−** Não há sigilo futuro no sentido MLS (sem TreeKEM). A revisão
  criptográfica independente continua pendente; por isso a flag nasce off.
- **−** Perda da chave e da senha de backup é irrecuperável.
