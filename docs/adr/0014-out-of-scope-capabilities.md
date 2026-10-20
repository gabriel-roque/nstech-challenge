# ADR 0014 — Capacidades fora de escopo

**Status:** aceito

**Contexto:** reserva, expiração, ajuste administrativo, eventos e cache adicionam estados e falhas distribuídas.

**Decisão:** não implementar essas capacidades, nem outbox, Redis, event sourcing ou microsserviços.

**Alternativas:** cada mecanismo pode ser adequado quando houver integração externa, throughput medido ou regras operacionais concretas.

**Consequências:** solução focada nas invariantes pedidas. Novas necessidades devem começar por requisitos, SLOs e ADR próprio.
