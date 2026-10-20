# ADR 0002 — Aggregate e CQRS-lite sem MediatR

**Status:** aceito

**Contexto:** regras de itens, total e status precisam permanecer coesas sem adicionar infraestrutura cerimonial.

**Decisão:** `Order` é aggregate root com factory/transições. Handlers separam comandos; um query service projeta DTOs. Repositories são específicos, sem generic repository ou MediatR.

**Alternativas:** MediatR adicionaria pipeline conveniente, mas uma dependência sem benefício suficiente neste tamanho; generic repository esconderia recursos necessários do EF/PostgreSQL.

**Consequências:** fluxo explícito e poucas abstrações, com stores especializados para transações complexas.

