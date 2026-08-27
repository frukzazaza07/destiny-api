param(
    [string]$ApiBaseUrl = "http://127.0.0.1:3001",
    [Parameter(Mandatory = $true)][string]$AdminKey,
    [ValidateSet("STANDARD", "DEEP")][string]$ReadingMode = "STANDARD",
    [ValidateRange(1, 10)][int]$Variants = 1,
    [ValidateRange(1, 10000)][int]$BatchSize = 500,
    [int]$Offset = 0,
    [ValidateSet("", "en", "th")][string]$Locale = "",
    [string]$ModelTier = ""
)

$body = @{
    readingMode = $ReadingMode
    variants = $Variants
    maxCombinations = $BatchSize
    offset = $Offset
}
if ($Locale) { $body.locale = $Locale }
if ($ModelTier) { $body.modelTier = $ModelTier }

Invoke-RestMethod `
    -Method Post `
    -Uri "$ApiBaseUrl/api/admin/cache/warmups" `
    -Headers @{ "X-Admin-Key" = $AdminKey } `
    -ContentType "application/json" `
    -Body ($body | ConvertTo-Json)
