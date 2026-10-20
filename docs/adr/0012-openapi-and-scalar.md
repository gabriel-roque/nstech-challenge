# ADR 0012 — OpenAPI e Scalar

**Status:** aceito

**Contexto:** consumidores precisam descobrir e exercitar contratos.

**Decisão:** gerar OpenAPI 3.1 nativo em `/openapi/v1.json` e Scalar em `/scalar`, habilitados no ambiente do desafio e desabilitados por padrão em Production.

**Alternativas:** Swagger UI é comum; Scalar oferece UI moderna com integração direta ao gerador nativo.

**Consequências:** documentação executável sem refletir regras em uma especificação manual; a exposição é configuração operacional.

