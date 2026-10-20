# ADR 0001 — .NET 10 e Clean Architecture

**Status:** aceito

**Contexto:** a entrega exige .NET 8+ e separação Domain/Application/Infrastructure/API.

**Decisão:** usar .NET 10 LTS fixado por `global.json`, controllers e dependências apontando para dentro. Testes de arquitetura protegem os limites.

**Alternativas:** .NET 8 reduziria novidade, mas teria uma janela de evolução menor; uma única aplicação seria mais curta, porém acoplaria domínio, HTTP e EF.

**Consequências:** mais projetos e composição explícita; domínio testável sem infraestrutura e atualização reproduzível.

