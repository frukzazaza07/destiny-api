docker compose --env-file .env run --rm api --bootstrap-admin
docker compose --env-file .env -p prod-tarot-destiny  -f docker-compose.infra.yml  up nginx -d  --force-recreate
docker compose --env-file .env -p prod-registry  -f docker-compose.registry.yml  up -d  --force-recreate
docker compose --env-file .env -p prod-tarot-destiny  -f docker-compose.infra.yml  up -d  --force-recreate