# Skills de bots

A flag `Features:AiBots:Enabled` nasce **false** e cobre também as skills.
Com ela desligada, criar, publicar e importar respondem 404 `AiBotsDisabled`.
O chat segue. Uma versão já atribuída a um bot publicado não é reescrita.

## Publicar e voltar

1. O admin salva um rascunho e publica. A publicação grava uma
   `ai.skill_versions` nova e aponta `ActiveVersionNumber`.
2. Alterar o rascunho não muda a versão nem os bots que já a referenciam.
3. Rollback escolhe um número já publicado. As versões posteriores permanecem.
4. Deprecar impede atribuição nova e não apaga as versões.

## Falha

- 409 `SkillRevisionConflict`: outra gravação chegou antes. Recarregar a skill.
- 409 `SkillSlugVersionConflict`: o manifesto repete slug e versão.
- 400 `SkillCodeRejected`, `SkillSecretRejected`, `SkillInstructionRejected`,
  `SkillLinkRejected` ou `SkillGrantUnknown`: o manifesto foi recusado e não
  foi gravado.
- 409 `SkillQuotaExceeded`: o workspace passou de 50 skills.

O manifesto `vibechat.bot.skill.v1` não carrega segredo. A ordem efetiva é
policy da plataforma, policy do workspace, prompt do bot e só então as skills.
Desligar a flag não reescreve o histórico.
