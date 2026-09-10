# Separate infrastructure and application stacks

`docker-compose.infra.yml` owns Nginx, PostgreSQL, Redis, RabbitMQ, their persistent data volumes, and the shared `edge` and `backend` networks. `docker-compose.yml` owns the application, migrations, and optional inference services. `docker-compose.jobs.yml` remains an application overlay enabling queued API processing and workers.

The app references the infrastructure networks as external networks. Nginx, API, and web share `edge`; PostgreSQL, API, migrations, Redis, RabbitMQ, classifier, and queued workers share the private `backend` network. Service addresses remain `api:5000`, `web:3000`, and `postgres:5432`. The application still owns its separate outbound `egress` network.

## Start

Run from the repository root, using the same deployment configuration for both commands. Infrastructure now requires `RABBITMQ_PASSWORD`, even when the queued application overlay is not enabled. Supply the same password to the queued API/worker configuration. RabbitMQ stays private at `rabbitmq:5672`, with no published host port.

```sh
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml up -d --wait
docker compose -f docker-compose.yml up -d --build --wait
```

For queued DEEP and astrology, replace the second command with:

```sh
docker compose -f docker-compose.yml -f docker-compose.jobs.yml up -d --build --wait
```

Always specify the infrastructure project with `-p`; an existing `COMPOSE_PROJECT_NAME` must not put it in the app project. Do not merge the infrastructure and application files into one Compose invocation. Infrastructure, including PostgreSQL, Redis, and RabbitMQ, must be healthy before starting the app: cross-project `depends_on` is unavailable. The application retains its migration-completion and classifier health dependencies. Redis remains reachable at `redis:6379` on `backend`, with its existing host binding and persistence settings.

The existing Nginx configuration requires TLS certificates under `/etc/letsencrypt/live/dooduang.cc/`. It publishes ports 80 and 443; API/web container ports remain private. Its health check sends the configured Host header. Nginx uses Docker DNS to discover API/web when they start and refresh their addresses after app replacements. Before the app is ready, proxy requests can return 502 while Nginx's own health endpoint remains available.

## Resource names

| Configuration key | Default | Purpose |
| --- | --- | --- |
| `APP_EDGE_NETWORK` | `tarot-destiny_edge` | Shared proxy/application network |
| `APP_BACKEND_NETWORK` | `tarot-destiny_backend` | Shared internal service network |
| `POSTGRES_VOLUME_NAME` | `tarot-destiny_postgres-data` | Persistent PostgreSQL storage |
| `REDIS_VOLUME_NAME` | `tarot-destiny_redis-data` | Persistent Redis storage |
| `RABBITMQ_VOLUME_NAME` | `tarot-destiny_rabbitmq_jobs` | Persistent RabbitMQ storage |
| `RABBITMQ_HOSTNAME` | `rabbitmq` | Stable broker hostname; preserve the old hostname when reusing existing broker data |

Set network names identically for both stacks. Set all volume names to the existing data volumes when migrating a deployment with a custom project name. These explicit resource names do not change with `-p` or `COMPOSE_PROJECT_NAME`. Separate environments need distinct resource names and nonconflicting Nginx/Redis port bindings/configuration.

## Move an existing deployment

The default PostgreSQL volume name matches the previous default app project's volume, so the split reuses its data. Before the first deployment of this split, plan a short outage and stop/remove the old combined stack using its previous Compose files and project name, **without `--volumes` / `-v`**. This releases ports 80/443 and the old Compose-owned networks before infrastructure recreates those networks. Never run old and new PostgreSQL containers against the same volume simultaneously. Retain the existing database name, username, and password in deployment configuration.

Redis likewise reuses its previous default volume, `tarot-destiny_redis-data`. If Nginx/PostgreSQL have already been separated, stop/remove the old app-owned Redis container using the previous app Compose file before starting infrastructure with Redis. Preserve its volume and do not run two Redis containers against the same data directory.

After that one-time transition, use the startup commands above. Ordinary app updates and `docker compose -f docker-compose.yml down` leave Nginx, PostgreSQL, Redis, and RabbitMQ running. Stop the app before taking down infrastructure. The production workflow starts/health-checks infrastructure without forcing its recreation, then rebuilds/recreates the app. The dev workflow uses server-owned `docker-compose.dev*.yml` files absent from this repository; adapt those files to this layout separately if needed.

RabbitMQ reuses the previous default volume name, `tarot-destiny_rabbitmq_jobs`. Stop/remove the old app-owned broker using the previous app/jobs Compose files before starting the infrastructure broker, and preserve the volume. RabbitMQ data is tied to its node identity: when reusing existing data, set `RABBITMQ_HOSTNAME` to the previous broker's hostname before startup. Fresh deployments use the stable default `rabbitmq`. Keep that hostname unchanged across subsequent replacements, and never run both brokers against the same volume.

## Verification

Validate each configuration without printing resolved values:

```sh
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml config --quiet
docker compose -f docker-compose.yml config --quiet
docker compose -f docker-compose.yml -f docker-compose.jobs.yml config --quiet
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml exec nginx nginx -t
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml exec postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml exec redis redis-cli ping
docker compose -p tarot-destiny-infra -f docker-compose.infra.yml exec rabbitmq rabbitmq-diagnostics -q ping
```

The smoke script accepts `-InfraComposeProject` (default `tarot-destiny-infra`) for database, Redis, and RabbitMQ checks. Its direct API/Swagger checks require an operator-accessible Development API endpoint; the public Nginx configuration intentionally hides Swagger.

Compose configurations were validated locally with example-only configuration. Container startup, Nginx runtime validation, and database connectivity could not be run because the local Docker daemon is unavailable.

References: [Compose shared networks](https://docs.docker.com/compose/how-tos/networking/#connecting-multiple-compose-projects), [Nginx dynamic upstream resolution](https://nginx.org/en/docs/http/ngx_http_upstream_module.html#server).
