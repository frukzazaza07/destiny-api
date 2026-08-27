param(
    [string]$WebBaseUrl = "http://127.0.0.1:3000",
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$AdminKey = "tarot-development-admin",
    [string]$ComposeProject = $env:COMPOSE_PROJECT_NAME,
    [string]$PostgresUser = $env:POSTGRES_USER,
    [string]$PostgresDatabase = $env:POSTGRES_DB
)

$ErrorActionPreference = "Stop"
$PostgresUser = if ($PostgresUser) { $PostgresUser } else { "tarot" }
$PostgresDatabase = if ($PostgresDatabase) { $PostgresDatabase } else { "tarot_destiny" }

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$composeArgs = @("compose")
if ($ComposeProject) {
    $composeArgs += @("--project-name", $ComposeProject)
}

function Invoke-Compose {
    & docker @composeArgs @args
}

$web = Invoke-WebRequest -UseBasicParsing -Uri $WebBaseUrl
Assert-True ($web.StatusCode -eq 200) "Web application did not return HTTP 200."

$health = Invoke-RestMethod -Uri "$ApiBaseUrl/health"
Assert-True ($health.success -and $health.data.status -eq "ok") "API health envelope is invalid."

$openApi = Invoke-RestMethod -Uri "$ApiBaseUrl/swagger/v1/swagger.json"
Assert-True ($null -ne $openApi.paths.'/api/readings/generate') "OpenAPI is missing the reading endpoint."
$swagger = Invoke-WebRequest -UseBasicParsing -Uri "$ApiBaseUrl/swagger/index.html"
Assert-True ($swagger.StatusCode -eq 200) "Swagger UI did not return HTTP 200."

Invoke-Compose exec -T classifier python scripts/health_check.py
if ($LASTEXITCODE -ne 0) { throw "Classifier health check failed." }
Invoke-Compose exec -T postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL health check failed." }
$redis = Invoke-Compose exec -T redis redis-cli ping
Assert-True ($LASTEXITCODE -eq 0 -and $redis.Trim() -eq "PONG") "Redis health check failed."

$migrationTableCount = Invoke-Compose exec -T postgres psql -U $PostgresUser -d $PostgresDatabase -tAc "SELECT count(*) FROM pg_catalog.pg_tables WHERE schemaname = 'public' AND tablename = '__EFMigrationsHistory';"
Assert-True ($LASTEXITCODE -eq 0 -and [int]$migrationTableCount.Trim() -eq 1) "Database migrations were not applied."

$readingBody = @{
    question = ""
    spread = "DAILY_1"
    locale = "en"
    readingMode = "STANDARD"
    cards = @(
        @{ position = "GUIDANCE"; cardId = "THE_FOOL"; orientation = "UPRIGHT" }
    )
} | ConvertTo-Json -Depth 6

$first = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/readings/generate" -ContentType "application/json" -Body $readingBody
$second = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/readings/generate" -ContentType "application/json" -Body $readingBody
Assert-True ($first.success -and $first.data.cacheStatus -in @("MISS", "HIT")) "First reading failed."
Assert-True ($second.success -and $second.data.cacheStatus -eq "HIT") "Second reading did not use the finished-answer cache."
Assert-True ($first.data.cacheKey -eq $second.data.cacheKey) "Repeated readings produced different cache keys."

$adminHeaders = @{ "X-Admin-Key" = $AdminKey }
$analytics = Invoke-RestMethod -Uri "$ApiBaseUrl/api/admin/cache/analytics" -Headers $adminHeaders
Assert-True ($analytics.success -and $analytics.data.runtime.cacheHits -ge 1) "Cache analytics did not record the smoke-test hit."

[pscustomobject]@{
    Web = "ok"
    Api = "ok"
    Swagger = "ok"
    Classifier = "ok"
    PostgreSQL = "ok"
    Redis = "ok"
    FirstReading = $first.data.cacheStatus
    SecondReading = $second.data.cacheStatus
    CacheKey = $second.data.cacheKey
}
