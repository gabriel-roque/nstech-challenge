# ADR 0011 — JWT local para o desafio

**Status:** aceito para o desafio

**Contexto:** autenticação JWT é obrigatória, sem IdP fornecido.

**Decisão:** emitir HMAC JWT local com `sub`, `unique_name`, `jti`, `iat`, issuer, audience e expiração; senha seed usa PBKDF2-SHA256.

**Alternativas:** Keycloak/Auth0/Entra seriam mais próximos de produção, mas ampliariam operação e configuração.

**Consequências:** demonstração autocontida. Produção requer IdP, rotação assimétrica, secrets manager, MFA e revogação/policies.

