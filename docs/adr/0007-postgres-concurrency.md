# ADR 0007 — Concorrência no PostgreSQL

**Status:** aceito

**Contexto:** duas APIs precisam coordenar status e estoque sem overselling.

**Decisão:** transações curtas `READ COMMITTED`; `SELECT ... FOR UPDATE` no pedido; updates condicionais de produto em ordem; rollback integral e até três retries da transação em deadlock `40P01`.

**Alternativas:** locks em memória não atravessam instâncias; `SERIALIZABLE` aumentaria abortos sem fortalecer as invariantes já expressas por locks/updates.

**Consequências:** PostgreSQL é a autoridade de concorrência. Mudanças para invariantes agregadas podem exigir nível mais forte.

