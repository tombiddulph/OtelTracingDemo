param(
    [Parameter(Mandatory = $true)]
    [string]$ScenarioPath
)

$ErrorActionPreference = "Stop"

$apiUrl = "http://localhost:8080"
$aspireUrl = "http://localhost:18888"
$url = "$apiUrl/$ScenarioPath"

Write-Host "Calling $url"
$response = Invoke-RestMethod -Method Post -Uri $url
$response | ConvertTo-Json -Depth 10

Write-Host ""
Write-Host "Aspire: $aspireUrl"
Write-Host "Search: $($response.workflowId)"

if ($response.messageIds.Count -gt 0) {
    Write-Host "First message id: $($response.messageIds[0])"
}
