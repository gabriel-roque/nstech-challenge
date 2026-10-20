# Order Service

API REST de pedidos em .NET 10, EF Core e PostgreSQL. A solução aplica Clean Architecture, JWT, idempotência distribuída, controle transacional de estoque e consultas paginadas. O ambiente Docker inicia duas instâncias da API atrás de um Nginx e executa migrations/seed uma única vez antes de liberar tráfego.

## Início rápido

Pré-requisitos: Docker com Compose v2. Para iniciar toda a aplicação:

```bash
docker compose up --build
```

Depois do health check:

- API: `http://localhost:8080`
- Scalar: `http://localhost:8080/scalar`
- OpenAPI 3.1: `http://localhost:8080/openapi/v1.json`
- Liveness: `http://localhost:8080/health/live`
- Readiness/PostgreSQL: `http://localhost:8080/health/ready`

Cada resposta contém `X-Instance-Id: api-1|api-2`, permitindo observar o balanceamento. O Nginx usa `least_conn`, equivalente a round-robin quando as conexões têm duração semelhante.

Para encerrar sem apagar o banco:

```bash
docker compose down
```

Para recriar tudo, inclusive o volume PostgreSQL (operação destrutiva para dados locais):

```bash
docker compose down --volumes
docker compose up --build
```

## Dados de demonstração

O migrator cria somente os registros ausentes. Reexecuções não restauram estoque e não sobrescrevem usuários ou pedidos.

- Usuários: `user01` até `user10`
- Senha: `Demo@123456` (pode ser alterada com `DEMO_PASSWORD` antes do primeiro seed)
- IDs de usuário: `00000000-0000-0000-0000-000000000001` até `...0010`
- Produtos: `SKU-001` até `SKU-010`
- IDs de produto: `10000000-0000-0000-0000-000000000001` até `...0010`
- Moeda: `BRL`; preço: R$ 10,00 até R$ 100,00; estoque inicial: 100 unidades

Em ambientes compartilhados, defina uma chave JWT forte:

```bash
JWT_SIGNING_KEY='uma-chave-aleatoria-com-pelo-menos-32-caracteres' docker compose up --build
```

## Uso da API

Obtenha um token:

```bash
curl --request POST http://localhost:8080/auth/token \
  --header 'Content-Type: application/json' \
  --data '{"username":"user01","password":"Demo@123456"}'
```

A resposta contém `accessToken`, `tokenType` e `expiresIn`. Nos exemplos seguintes, salve-o em `TOKEN` e gere uma chave UUID em `KEY`.

### Criar pedido

`Idempotency-Key` é obrigatório e deve ser um UUID não vazio.

```bash
curl --request POST http://localhost:8080/orders \
  --header "Authorization: Bearer $TOKEN" \
  --header "Idempotency-Key: $KEY" \
  --header 'Content-Type: application/json' \
  --data '{
    "customerId":"00000000-0000-0000-0000-000000000001",
    "currency":"BRL",
    "items":[
      {"productId":"10000000-0000-0000-0000-000000000001","quantity":2}
    ]
  }'
```

A primeira chamada retorna `201 Created`. Uma repetição com a mesma chave e payload retorna `200 OK`, `Idempotency-Replayed: true`, o mesmo pedido e o mesmo `Location`. Reutilizar a chave com outro payload retorna `409 idempotency_key_reused`.

Produtos repetidos no payload são rejeitados. O preço é copiado do catálogo; o cliente nunca informa preço. A criação verifica o estoque disponível, mas não o reserva: a garantia definitiva contra overselling ocorre na confirmação.

### Consultar, confirmar e cancelar

```bash
curl --header "Authorization: Bearer $TOKEN" http://localhost:8080/orders/$ORDER_ID

curl --request POST \
  --header "Authorization: Bearer $TOKEN" \
  http://localhost:8080/orders/$ORDER_ID/confirm

curl --request POST \
  --header "Authorization: Bearer $TOKEN" \
  http://localhost:8080/orders/$ORDER_ID/cancel
```

Confirmar novamente um pedido confirmado e cancelar novamente um pedido cancelado retornam `200` sem repetir efeitos. Cancelar um pedido confirmado devolve o estoque exatamente uma vez. Confirmar um pedido cancelado retorna `409`.

Nas operações protegidas, o claim JWT `sub` é a identidade confiável. Um usuário não pode criar para outro `customerId`; detalhe, confirmação e cancelamento procuram por `(orderId, customerId)` e retornam `404` para recursos de outro usuário.

### Listar pedidos

A listagem é deliberadamente pública e exige `customerId`:

```bash
curl 'http://localhost:8080/orders?customerId=00000000-0000-0000-0000-000000000001&status=Confirmed&from=2026-01-01T00:00:00Z&to=2027-01-01T00:00:00Z&page=1&pageSize=20'
```

- `from` é inclusivo e `to` exclusivo.
- Datas exigem ISO 8601 com `Z` ou offset explícito.
- `page` começa em 1; `pageSize` aceita 1 a 100.
- Ordem estável: `createdAt DESC, id DESC`.
- A resposta contém somente resumos, nunca itens.

Esta exceção pública foi escolhida porque o contrato exige `customerId` na listagem e não define um papel administrativo. Conhecer um ID permite consultar os resumos daquele cliente; isso é um risco aceito apenas para o desafio. Em produção, a listagem deve usar o `sub` e remover o filtro redundante, ou exigir uma policy administrativa explícita. Veja [ADR 0003](docs/adr/0003-authorization-and-public-list.md).

## Erros

Falhas esperadas usam `application/problem+json` com `code`, `traceId` e, quando aplicável, `field`:

- `400`: contrato inválido, quantidade, moeda, datas, paginação, produto repetido ou chave ausente.
- `401`: credencial ou JWT inválido.
- `403`: tentativa de criar para outro cliente.
- `404`: pedido ausente ou pertencente a outro usuário.
- `409`: estoque insuficiente, transição inválida ou conflito de idempotência.
- `422`: produto inexistente ou moeda incompatível.

## Desenvolvimento local

Requer .NET SDK 10.0.400+ e PostgreSQL. O SDK é fixado em `global.json`.

```bash
dotnet tool restore
dotnet restore OrderService.slnx --locked-mode
dotnet run --project src/OrderService.Migrator
dotnet run --project src/OrderService.Api
```

Variáveis reconhecidas:

- `ConnectionStrings__Postgres`
- `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey`, `Jwt__LifetimeMinutes`
- `Documentation__Enabled`
- `InstanceId`
- `Seed__DemoPassword` (migrator)

Migrations não são aplicadas pelas APIs. No Docker, um EF migration bundle one-shot termina antes das duas instâncias e o executável de seed roda em seguida.

Para criar uma nova migration:

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/OrderService.Infrastructure \
  --startup-project src/OrderService.Api \
  --output-dir Persistence/Migrations
```

## Testes

`dotnet test` executa testes unitários, de arquitetura e integração. Os testes de integração usam PostgreSQL real via Testcontainers e, portanto, exigem Docker disponível.

```bash
dotnet test OrderService.slnx
```

Categorias cobertas:

- invariantes, total decimal, snapshot de preço e transições;
- fingerprint e criação idempotente;
- limites da Clean Architecture;
- migration e seed reais;
- JWT, propriedade de pedidos e listagem pública;
- retries simultâneos com a mesma chave;
- 20 pedidos disputando 10 unidades, provando ausência de estoque negativo;
- health checks, OpenAPI e Scalar.

Os testes de carga exigem o [k6](https://grafana.com/docs/k6/latest/set-up/install-k6/) instalado na máquina e a aplicação disponível em `http://localhost:8080`. Inicie a stack em um terminal:

```bash
docker compose up --build -d
```

Em outro terminal, execute o smoke:

```bash
k6 run tests/k6/smoke.js
```

Ou execute o stress balanceado de cinco minutos:

```bash
k6 run tests/k6/stress.js
```

Os scripts usam `BASE_URL=http://localhost:8080` e `DEMO_PASSWORD=Demo@123456` por padrão. Para apontar para outro ambiente ou senha, informe as variáveis no próprio comando:

```bash
BASE_URL=http://localhost:8080 \
DEMO_PASSWORD='outra-senha' \
k6 run tests/k6/smoke.js
```

O k6 é executado diretamente no terminal; ele não faz parte do Compose. O stress sobe até 100 VUs, distribui os 10 usuários e mede p95 de leituras abaixo de 500 ms e escritas abaixo de 1 s. Os limites são uma baseline local e devem ser recalibrados com hardware, volume e SLOs reais.

## Arquitetura

```text
Domain <- Application <- Infrastructure
             ^               ^
             +------ API ----+
```

- **Domain:** aggregates e invariantes, sem EF/ASP.NET.
- **Application:** casos de uso, DTOs e portas.
- **Infrastructure:** EF Core/Npgsql, queries, autenticação de credenciais e transações.
- **API:** controllers, JWT, Problem Details, documentação e composition root.
- **Migrator:** seed idempotente após o migration bundle.

Consultas usam projeção direta e `AsNoTracking`; a listagem não materializa itens. Índices começam por `customer_id` nas consultas de autorização/filtro. A confirmação bloqueia o pedido com `FOR UPDATE` e baixa cada produto por `UPDATE ... WHERE available_quantity >= quantity`, ordenado por `product_id`. Qualquer falha reverte toda a transação. Cancelamento usa o mesmo lock e ordenação. Deadlocks PostgreSQL repetem a transação inteira no máximo três vezes com jitter.

Diagramas de dados e sequência estão em [docs/architecture.md](docs/architecture.md). As decisões e alternativas estão no [índice de ADRs](docs/adr/README.md).

## CI

O workflow de pull request restaura com lock files, compila com nullable/analyzers e warnings como erros, verifica formatação, executa testes/cobertura, detecta model changes sem migration, gera migration bundle, audita pacotes, constrói o Compose, valida health/OpenAPI/Scalar, executa o smoke k6 e confirma respostas das duas instâncias. O stress completo é manual.

Dependabot acompanha NuGet, Docker e GitHub Actions. Workflows têm permissões somente de leitura e cancelam execuções antigas da mesma branch.

## Limites conscientes

- Não há reserva temporária, expiração, ajuste administrativo de estoque, carteira ou crédito.
- Não há outbox porque não existem efeitos em brokers/serviços externos.
- Offset pagination atende ao contrato; em páginas profundas, keyset pagination é a evolução indicada.
- A chave de idempotência não expira nesta entrega; produção requer retenção e limpeza.
- O JWT local serve à demonstração. Produção deve usar um IdP, rotação de chaves, secrets manager e policies por escopo/papel.
- Logs não incluem senhas nem tokens; observabilidade distribuída e métricas de negócio são evoluções naturais.
