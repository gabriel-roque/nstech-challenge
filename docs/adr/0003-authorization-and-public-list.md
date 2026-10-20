# ADR 0003 — Propriedade e listagem pública

**Status:** aceito para o desafio

**Contexto:** JWT é obrigatório; a listagem exige `customerId`, mas não há papel administrativo definido.

**Decisão:** escritas e detalhe usam `sub` como proprietário. Acesso cruzado retorna `404`. A listagem é pública, exige `customerId` e devolve apenas resumos.

**Alternativas:** vincular a listagem ao `sub` tornaria `customerId` redundante; um administrador exigiria modelo de papéis não solicitado.

**Consequências:** quem conhece um ID pode ler resumos. Antes de produção, proteger pelo `sub` ou por policy administrativa explícita.

