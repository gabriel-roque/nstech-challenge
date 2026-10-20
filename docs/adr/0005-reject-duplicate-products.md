# ADR 0005 — Rejeitar produtos repetidos

**Status:** aceito

**Contexto:** linhas duplicadas criam ambiguidade no fingerprint, preço e chave relacional.

**Decisão:** cada `productId` aparece uma vez; duplicatas retornam `400`.

**Alternativas:** consolidar quantidades silenciosamente esconderia erro do consumidor e alteraria o payload assinado.

**Consequências:** chave simples `(order_id, product_id)` e cálculo/fingerprint determinísticos.

