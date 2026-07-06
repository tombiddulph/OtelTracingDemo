$ErrorActionPreference = "Stop"

$scenarios = @(
    "scenarios/linear-trace",
    "scenarios/linked-trace",
    "scenarios/batch",
    "scenarios/fanout",
    "scenarios/retry-then-success",
    "scenarios/external-call-failure"
)

foreach ($scenario in $scenarios) {
    & "$PSScriptRoot\Invoke-Scenario.ps1" -ScenarioPath $scenario
    Write-Host ""
}

Write-Host "Dead-letter is intentionally excluded from demo-all because it keeps failing until Service Bus dead-letters it."
