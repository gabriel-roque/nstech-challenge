# ADR 0009 — Índices e paginação

**Status:** aceito

**Contexto:** toda consulta de pedidos filtra cliente e pode filtrar status/período.

**Decisão:** índices compostos começam por `customer_id`; listagem usa `AsNoTracking`, projeção e ordem `created_at DESC, id DESC`. Paginação é offset com máximo 100.

**Alternativas:** keyset é mais estável em páginas profundas, mas o contrato exige `page/pageSize`.

**Consequências:** API simples e previsível; custos de `OFFSET` alto devem ser medidos e podem motivar endpoint cursor-based.

