# ADR 0004 — Sem carteira ou `Money`

**Status:** aceito

**Contexto:** o domínio solicitado calcula preço e total por moeda; não descreve crédito, saldo ou conversão.

**Decisão:** usar `decimal` com código de moeda normalizado. Não modelar carteira, limite nem value object `Money`.

**Alternativas:** `Money` seria útil com operações monetárias ricas ou múltiplas moedas; essas regras não existem aqui.

**Consequências:** modelo menor; qualquer futura conversão/arredondamento fiscal exigirá uma decisão nova.

