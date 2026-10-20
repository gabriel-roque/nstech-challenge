# ADR 0013 — Estratégia de testes e CI

**Status:** aceito

**Contexto:** invariantes em memória não bastam para provar SQL, JWT e concorrência.

**Decisão:** combinar xUnit unitário, testes de dependência, `WebApplicationFactory` + PostgreSQL Testcontainers, disputa concorrente e k6. CI trata warnings como erros e valida Docker.

**Alternativas:** banco in-memory seria rápido, mas não reproduziria locks, constraints nem SQL PostgreSQL.

**Consequências:** confiança maior e suíte mais lenta; integração exige Docker.

