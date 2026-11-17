# Vela — banco digital (projeto de portfólio)

Um banco digital simples, inspirado na experiência de apps como Nubank e Revolut: abrir conta, ver o saldo, depositar, transferir por e-mail ou número da conta e acompanhar o extrato.

**Stack:** .NET 10 / ASP.NET Core (Minimal APIs) · Entity Framework Core · PostgreSQL · React 19 + TypeScript (Vite) · Docker

> Projeto de estudo e portfólio. "Vela" é um nome fictício e não é um banco de verdade.

## Rodar com um comando

Pré-requisito: Docker.

```bash
cp .env.example .env
docker compose up --build
```

| O quê | Onde |
| --- | --- |
| App (React) | http://localhost:8080 |
| API | http://localhost:5080 |
| Documentação da API (Scalar/OpenAPI) | http://localhost:5080/scalar/v1 |
| PostgreSQL | `localhost:5433` (usuário/senha `bank`) |

## Destaques técnicos

- **Transferências sem saldo negativo, mesmo com concorrência.** O débito é um `UPDATE ... WHERE saldo >= valor`, que é atômico. Ele roda na mesma transação do crédito e dos lançamentos do extrato. O banco ainda tem uma `CHECK (Balance >= 0)` como segunda proteção.
- **Sem deadlock.** Em transferências simultâneas A→B e B→A, as duas contas são sempre bloqueadas na mesma ordem (pelo id).
- **Extrato tipo razão contábil.** Cada transferência gera dois lançamentos com sinal (saída e entrada).
- **Autenticação JWT.** Senhas usam o `PasswordHasher` do ASP.NET Core Identity (PBKDF2).
- **Validação nativa do .NET 10** (`AddValidation` + DataAnnotations) e erros no padrão **ProblemDetails (RFC 7807)**.
- **Testes de integração contra um PostgreSQL real** (`WebApplicationFactory`), incluindo 20 transferências em paralelo.

## Estrutura

```
backend/
  src/Bank.Api/            API ASP.NET Core
    Endpoints/             rotas (auth, conta) e DTOs
    Data/                  DbContext, entidades e migrations do EF Core
    Auth/                  emissão de JWT
  tests/Bank.Api.Tests/    testes de integração (xUnit)
frontend/                  React + Vite (servido por nginx no Docker)
docker-compose.yml         Postgres + API + web
```

## API

| Método | Rota | Descrição |
| --- | --- | --- |
| POST | `/api/auth/register` | cria o usuário e a conta e devolve o token |
| POST | `/api/auth/login` | devolve o token |
| GET | `/api/account` | nome, número da conta e saldo |
| GET | `/api/account/transactions` | últimos 50 lançamentos |
| POST | `/api/account/deposits` | depósito simulado (até R$ 10.000) |
| POST | `/api/account/transfers` | `{ "to": "email ou número", "amount": 10.50 }` |

## Desenvolvimento local (sem Docker para a API e o front)

Pré-requisitos: .NET SDK 10, Node 24 e Docker (só para o Postgres).

```bash
docker compose up -d db
cd backend && dotnet run --project src/Bank.Api      # http://localhost:5080
cd frontend && npm install && npm run dev            # http://localhost:5173
```

Testes (precisam do Postgres em `localhost:5433`):

```bash
cd backend && dotnet test
```

## O que ficou de fora (de propósito)

Refresh token, chave de idempotência nas transferências, paginação do extrato, rate limiting no login e PIX/cartões. A chave JWT do `docker-compose.yml` serve só para desenvolvimento.
