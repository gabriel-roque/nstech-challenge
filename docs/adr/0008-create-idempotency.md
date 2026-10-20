# ADR 0008 — Idempotência da criação

**Status:** aceito

**Contexto:** retries de `POST` podem duplicar pedidos entre instâncias.

**Decisão:** exigir UUID em `Idempotency-Key`, armazenar hash SHA-256 canônico e impor unique `(customer_id, idempotency_key)`. Conflitos concorrentes recarregam o vencedor.

**Alternativas:** cache/lock local não coordena duas APIs; Redis seria infraestrutura extra sem necessidade.

**Consequências:** retry seguro e auditável; sem expiração, a tabela cresce e produção requer política de retenção.

