# ADR 0010 — Duas APIs e migrator one-shot

**Status:** aceito

**Contexto:** migrations concorrentes no startup são arriscadas e o desenho precisa provar coordenação compartilhada.

**Decisão:** Nginx distribui entre exatamente duas APIs. Um EF migration bundle e seed idempotente terminam antes de ambas iniciarem.

**Alternativas:** cada API aplicar migration poderia disputar DDL; uma única API não exercitaria garantias distribuídas.

**Consequências:** startup ordenado e reproduzível; deploys reais precisam tratar compatibilidade rolling/expand-contract.

