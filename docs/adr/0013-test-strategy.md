# ADR 0013 — Estratégia de testes e CI

**Status:** aceito

**Contexto:** invariantes em memória não bastam para provar SQL, JWT e concorrência. Testes com uma única `WebApplicationFactory` também não exercitam o Nginx nem a coordenação entre as duas instâncias da API.

**Decisão:** combinar xUnit unitário, testes de dependência, `WebApplicationFactory` + PostgreSQL Testcontainers, Gatling JS contra o Nginx do Compose e k6. A suíte Gatling força corridas de idempotência e transições, valida atomicidade e isolamento no banco e confirma durabilidade após queda abrupta do PostgreSQL. O CI trata warnings como erros e executa essa suíte sobre as duas APIs reais.

**Alternativas:** banco in-memory seria rápido, mas não reproduziria locks, constraints nem SQL PostgreSQL. Rodar Gatling dentro do Compose misturaria a infraestrutura sob teste com o executor; o runner local mantém o Compose da aplicação inalterado e consulta o banco com `docker compose exec`.

**Consequências:** confiança maior e suíte mais lenta; integração exige Docker, Node.js 24/npm 11 e reinicia os serviços durante a verificação de durabilidade. Relatórios ficam em `TestResults/consistency`.
