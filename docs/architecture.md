# Arquitetura

## Modelo de dados

```mermaid
erDiagram
    USERS ||--o{ ORDERS : owns
    ORDERS ||--|{ ORDER_ITEMS : contains
    PRODUCTS ||--o{ ORDER_ITEMS : snapshots

    USERS {
        uuid id PK
        varchar username
        varchar normalized_username UK
        varchar password_hash
        boolean is_active
    }
    PRODUCTS {
        uuid id PK
        varchar sku UK
        char currency
        numeric unit_price
        int available_quantity
    }
    ORDERS {
        uuid id PK
        uuid customer_id FK
        smallint status
        char currency
        numeric total
        uuid idempotency_key UK
        char request_hash
        timestamptz created_at
    }
    ORDER_ITEMS {
        uuid order_id PK,FK
        uuid product_id PK,FK
        numeric unit_price
        int quantity
    }
```

## Criação idempotente

```mermaid
sequenceDiagram
    participant C as Client
    participant A as API instance
    participant P as PostgreSQL
    C->>A: POST /orders + Idempotency-Key
    A->>A: Validate owner + canonical SHA-256
    A->>P: Find (customer, key)
    alt existing key, same hash
        P-->>A: Existing order
        A-->>C: 200 + same id + Location
    else existing key, different hash
        A-->>C: 409 idempotency_key_reused
    else new key
        A->>P: Load products in one query
        A->>P: INSERT order/items
        alt concurrent unique conflict
            A->>P: Reload winner in clean context
        end
        A-->>C: 201 (winner: 200)
    end
```

## Confirmação

```mermaid
sequenceDiagram
    participant A as API
    participant P as PostgreSQL
    A->>P: BEGIN READ COMMITTED
    A->>P: SELECT order FOR UPDATE by owner
    alt already Confirmed
        A->>P: COMMIT
        A-->>A: Return current state
    else Placed
        loop items ordered by product_id
            A->>P: UPDATE product SET stock=stock-q WHERE stock>=q
        end
        alt every update affected one row
            A->>P: UPDATE order to Confirmed + COMMIT
        else any update affected zero rows
            A->>P: ROLLBACK all stock updates
        end
    end
```

## Cancelamento

```mermaid
sequenceDiagram
    participant A as API
    participant P as PostgreSQL
    A->>P: BEGIN + SELECT order FOR UPDATE by owner
    alt Canceled
        A->>P: COMMIT without effects
    else Placed
        A->>P: UPDATE order to Canceled + COMMIT
    else Confirmed
        loop items ordered by product_id
            A->>P: Restore product quantity
        end
        A->>P: UPDATE order to Canceled + COMMIT
    end
```

Os locks são do PostgreSQL, portanto coordenam todas as instâncias. Não existe mutex em memória. A constraint de estoque e os updates condicionais formam a última linha de defesa contra valores negativos.

