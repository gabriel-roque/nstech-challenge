# Validação dos limites de CPU e memória — 2026-10-21

**Resultado global: PASS**

Esta execução validou o [`compose.yml`](../../compose.yml) com limites rígidos de CPU e memória, sem swap, usando duas APIs atrás do Nginx. A stack de teste usou projeto e volume Docker isolados; nenhum dado do ambiente local normal foi reutilizado.

## Limites validados

| Serviço | CPU máxima | RAM máxima | Swap |
|---|---:|---:|---:|
| `postgres` | `0.75` | `512 MiB` | desabilitado |
| `api-1` | `2.00` | `512 MiB` | desabilitado |
| `api-2` | `2.00` | `512 MiB` | desabilitado |
| `nginx` | `0.25` | `64 MiB` | desabilitado |
| `migrator` | `0.50` | `256 MiB` | desabilitado |

Teto durante operação normal: **5 CPUs e 1,56 GiB de RAM**. O migrator é one-shot e termina antes das APIs.

## Ambiente

| Componente | Versão/capacidade |
|---|---|
| Host | 12 CPUs lógicas, 31 GiB RAM |
| .NET SDK | 10.0.400 |
| Docker Engine | 29.8.2 |
| Docker Compose | 5.6.0 |
| k6 | 2.3.0 |
| Node.js / npm do Gatling | 24.22.0 / 11.21.1 |
| Gatling JS | 3.16.1 |

## Gates de build e qualidade

| Verificação | Resultado |
|---|---|
| `docker compose config --quiet` | PASS |
| Limites conferidos via `docker inspect` | PASS |
| Build Release | PASS — 0 warnings, 0 erros |
| Testes de domínio | PASS — 24/24 |
| Testes de arquitetura | PASS — 3/3 |
| Testes de integração | PASS — 6/6 |
| Total .NET | PASS — 33/33 |
| `dotnet format --verify-no-changes` | PASS |
| Modelo EF sem migration pendente | PASS |
| Auditoria NuGet | PASS — nenhuma vulnerabilidade |
| Build Gatling JS | PASS |
| Auditoria npm | PASS — 0 vulnerabilidades |
| Parsing dos scripts k6 | PASS |
| Health, OpenAPI e Scalar | PASS |
| Respostas observadas de `api-1` e `api-2` | PASS |

## k6 smoke

Comando:

```bash
k6 run tests/k6/smoke.js
```

| Métrica | Resultado |
|---|---:|
| Requisições | 7 |
| Checks | 7/7 (100%) |
| Falhas HTTP | 0% |
| Duração HTTP p95 | 377,18 ms |

## k6 stress

Comando:

```bash
k6 run tests/k6/stress.js
```

Perfil: cinco minutos, rampa até 100 VUs e um minuto sustentado no pico.

| Métrica | Resultado | Threshold |
|---|---:|---:|
| Requisições | 118.224 | — |
| Iterações | 19.704 | — |
| Throughput médio | 393,96 req/s | — |
| Checks | 118.224/118.224 (100%) | 100% |
| Falhas HTTP | 0% | < 1% |
| Leitura p95 | 110,09 ms | < 500 ms |
| Escrita p95 | 491,72 ms | < 1.000 ms |
| HTTP global p95 | 398,54 ms | — |

As URLs dinâmicas receberam tags `name` estáveis. A execução final não apresentou o aviso de alta cardinalidade observado antes dessa correção.

### Amostra no estágio de 100 VUs

| Serviço | CPU observada | RAM observada / limite |
|---|---:|---:|
| `postgres` | 52,02% | 211,9 / 512 MiB |
| `api-1` | 204,71% | 141,2 / 512 MiB |
| `api-2` | 206,71% | 140,1 / 512 MiB |
| `nginx` | 6,96% | 4,65 / 64 MiB |

O percentual do `docker stats` é uma amostra por intervalo e pode oscilar ligeiramente acima da quota no instante da leitura. Nenhum container sofreu OOM ou reinício automático.

## Gatling distribuído e ACID

A suíte [`verify-consistency.sh`](../../ops/verify-consistency.sh) foi executada antes e depois do stress. A segunda execução validou o banco já contendo mais de 19.700 pedidos criados pelo k6.

| Métrica final | Resultado |
|---|---:|
| Requisições concorrentes | 103 |
| Sucesso | 103/103 |
| KO / HTTP 5xx | 0 |
| Tempo médio | 123 ms |
| p95 | 190 ms |
| Máximo | 215 ms |
| Throughput | 103 req/s |
| Participação de ambas as APIs | PASS |
| Idempotência distribuída | PASS |
| Ausência de overselling | PASS |
| Confirmação/cancelamento concorrentes | PASS |
| Rollback atômico multi-item | PASS |
| Totais, estados e estoques no banco | PASS |
| Dados após `SIGKILL` do PostgreSQL | PASS |
| Leitura após restart | PASS |

Os artefatos detalhados locais foram gerados em:

```text
TestResults/consistency/
```

## Conclusão

Os limites definidos no Compose mantiveram margem de memória, atenderam todos os thresholds k6 e preservaram as garantias de atomicidade, consistência, isolamento prático e durabilidade sob concorrência distribuída. Os números são uma baseline local; hosts menores, armazenamento diferente ou carga externa podem exigir recalibração.
