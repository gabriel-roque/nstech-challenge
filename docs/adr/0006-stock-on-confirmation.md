# ADR 0006 — Estoque baixado na confirmação

**Status:** aceito

**Contexto:** pedidos `Placed` podem permanecer sem prazo de expiração; reservar na criação exigiria liberação automática.

**Decisão:** criação apenas observa disponibilidade. Confirmação realiza a baixa atômica; cancelamento devolve somente estoque já confirmado.

**Alternativas:** reserva na criação melhoraria previsibilidade ao cliente, mas requer expiração e worker confiável.

**Consequências:** um pedido válido pode falhar ao confirmar; não há estoque preso por pedidos abandonados.

