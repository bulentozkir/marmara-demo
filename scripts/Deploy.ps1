#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $TerraformPath = 'terraform',
    [string] $PackagePath,
    [switch] $PackageOnly,
    [switch] $SkipInfrastructure
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$infra = Join-Path $repo 'infra'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TF_IN_AUTOMATION = '1'

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments)

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

if (-not $IsWindows) {
    throw 'Run this deployment script with PowerShell 7 on Windows.'
}
if ($PackageOnly -and $PackagePath) {
    throw 'PackageOnly cannot be combined with an existing PackagePath.'
}

Push-Location $repo
try {
    if (-not $PackagePath) {
        $work = Join-Path $repo ".copilot-azure\builds\$([guid]::NewGuid())"
        $source = Join-Path $repo 'src\TodoApp'
        $publish = Join-Path $work 'publish'
        $PackagePath = Join-Path $work 'app.zip'
        $project = Join-Path $source 'DotNetCoreSqlDb.csproj'
        New-Item -ItemType Directory -Path $work -Force | Out-Null

        Invoke-Checked 'dotnet' @('tool', 'restore', '--tool-manifest', (Join-Path $repo '.config\dotnet-tools.json'))
        Invoke-Checked 'dotnet' @('restore', $project, '--runtime', 'linux-x64')
        Invoke-Checked 'dotnet' @(
            'tool', 'run', 'dotnet-ef', '--', 'migrations', 'bundle',
            '--project', $project, '--startup-project', $project,
            '--configuration', 'Release', '--runtime', 'linux-x64',
            '--self-contained', '--force', '--output', (Join-Path $source 'migrationsbundle')
        )
        Invoke-Checked 'dotnet' @(
            'publish', $project, '--configuration', 'Release',
            '--runtime', 'linux-x64', '--self-contained', 'false',
            '--output', $publish
        )
        Copy-Item -LiteralPath (Join-Path $source 'LICENSE.md') -Destination (Join-Path $publish 'LICENSE.sample.md')
        Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $publish
        Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $PackagePath
    }

    $PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        if (-not $archive.GetEntry('DotNetCoreSqlDb.dll') -or -not $archive.GetEntry('migrationsbundle')) {
            throw 'The ZIP must contain the published tutorial app and its Linux migrations bundle at the root.'
        }
    }
    finally {
        $archive.Dispose()
    }

    if ($PackageOnly) {
        Write-Output $PackagePath
        return
    }

    if ($TerraformPath -eq 'terraform' -and -not (Get-Command terraform -ErrorAction SilentlyContinue)) {
        $TerraformPath = Join-Path $repo '.copilot-azure\tools\terraform.exe'
    }
    if (-not (Get-Command $TerraformPath -ErrorAction SilentlyContinue)) {
        throw 'Terraform is required. Install Hashicorp.Terraform or provide -TerraformPath.'
    }

    if (-not $SkipInfrastructure) {
        $planPath = Join-Path $infra 'deployment.tfplan'
        Invoke-Checked $TerraformPath @("-chdir=$infra", 'init', '-input=false')
        Invoke-Checked $TerraformPath @("-chdir=$infra", 'fmt', '-check')
        Invoke-Checked $TerraformPath @("-chdir=$infra", 'validate')
        Invoke-Checked $TerraformPath @("-chdir=$infra", 'plan', '-input=false', "-out=$planPath")
        $plan = (Invoke-Checked $TerraformPath @("-chdir=$infra", 'show', '-json', $planPath)) -join "`n" | ConvertFrom-Json
        if ($plan.resource_changes | Where-Object { $_.change.actions -contains 'delete' }) {
            throw 'The plan includes resource deletion or replacement. Review and apply those changes manually first.'
        }
        $sqlCreate = $plan.resource_changes | Where-Object {
            $_.type -eq 'azurerm_mssql_server' -and $_.change.actions -contains 'create'
        }
        if ($sqlCreate) {
            $targetSubscription = $plan.planned_values.outputs.subscription_id.value
            $sqlRegion = $sqlCreate.change.after.location
            $capabilityUrl = "https://management.azure.com/subscriptions/$targetSubscription/providers/Microsoft.Sql/locations/$sqlRegion/capabilities?api-version=2023-08-01"
            $capability = (Invoke-Checked 'az' @(
                'rest', '--method', 'get', '--url', $capabilityUrl,
                '--subscription', $targetSubscription, '--query', '{status:status,reason:reason}', '--output', 'json'
            )) -join "`n" | ConvertFrom-Json
            if ($capability.status -ne 'Available') {
                throw "SQL provisioning is not available in ${sqlRegion}: $($capability.reason). Set sql_location to an available region."
            }
        }
        Invoke-Checked $TerraformPath @("-chdir=$infra", 'apply', '-input=false', '-auto-approve', $planPath)
    }

    $outputs = (Invoke-Checked $TerraformPath @("-chdir=$infra", 'output', '-json')) -join "`n" | ConvertFrom-Json
    $subscription = $outputs.subscription_id.value
    $group = $outputs.resource_group_name.value
    $app = $outputs.web_app_name.value
    $azureTarget = @('--subscription', $subscription, '--resource-group', $group, '--name', $app)

    Invoke-Checked 'az' (@(
        'webapp', 'deploy', '--type', 'zip', '--src-path', $PackagePath,
        '--restart', 'true', '--track-status', 'false', '--timeout', '600000',
        '--only-show-errors', '--output', 'none'
    ) + $azureTarget)

    $refreshUrl = "https://management.azure.com$($outputs.web_app_id.value)/config/configreferences/appsettings/refresh?api-version=2024-11-01"
    Invoke-Checked 'az' @(
        'rest', '--method', 'post', '--url', $refreshUrl,
        '--subscription', $subscription, '--only-show-errors', '--output', 'none'
    )
    Invoke-Checked 'az' (@('webapp', 'restart', '--only-show-errors') + $azureTarget)

    $initialized = $false
    try {
        $token = (Invoke-Checked 'az' @(
            'account', 'get-access-token', '--subscription', $subscription,
            '--resource', 'https://database.windows.net/', '--query', 'accessToken', '--output', 'tsv'
        )) -join ''
        if ([string]::IsNullOrWhiteSpace($token)) {
            throw 'Azure CLI did not return a SQL access token.'
        }
        $bootstrapDeadline = [DateTime]::UtcNow.AddMinutes(15)
        while ([DateTime]::UtcNow -lt $bootstrapDeadline) {
            $bootstrap = $null
            try {
                $bootstrap = Invoke-WebRequest -Uri "$($outputs.app_url.value)/_deployment/initialize-database" `
                    -Method Post -Headers @{ Authorization = "Bearer $token" } `
                    -SkipHttpErrorCheck -TimeoutSec 180
            }
            catch [System.Net.Http.HttpRequestException] {
                Write-Host "Waiting for bootstrap endpoint: $($_.Exception.Message)"
            }
            catch [System.Threading.Tasks.TaskCanceledException] {
                Write-Host 'Bootstrap request timed out.'
            }
            if ($bootstrap) {
                if ($bootstrap.StatusCode -eq 200) {
                    $result = $bootstrap.Content | ConvertFrom-Json
                    if ($result.initialized -ne $true) {
                        throw 'The bootstrap endpoint did not confirm database initialization.'
                    }
                    if ($result.clientId -eq $outputs.managed_identity_client_id.value `
                        -and $result.database -eq $outputs.sql_database_name.value `
                        -and $result.schemaVersion -eq 2) {
                        $initialized = $true
                        break
                    }
                    Write-Host 'Waiting for the deployed bootstrap version to become active.'
                }
                if ($bootstrap.StatusCode -in 401, 403) {
                    throw "The deployment identity was denied SQL administrative access: HTTP $($bootstrap.StatusCode)."
                }
                Write-Host "Waiting for database initialization: HTTP $($bootstrap.StatusCode)"
            }
            Start-Sleep -Seconds 15
        }
    }
    finally {
        $token = $null
    }
    if (-not $initialized) {
        throw 'Database initialization failed. Inspect App Service logs before retrying.'
    }

    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    $ready = $false
    $lastStatus = 'No response'
    while ([DateTime]::UtcNow -lt $deadline) {
        $response = $null
        try {
            $response = Invoke-WebRequest -Uri "$($outputs.app_url.value)/ready" -SkipHttpErrorCheck -TimeoutSec 30
        }
        catch [System.Net.Http.HttpRequestException] {
            $lastStatus = $_.Exception.Message
        }
        catch [System.Threading.Tasks.TaskCanceledException] {
            $lastStatus = 'HTTP request timed out'
        }
        if ($response) {
            $lastStatus = "HTTP $($response.StatusCode)"
            if ($response.StatusCode -eq 200 -and $response.Content.Trim() -eq 'Healthy') {
                $ready = $true
                break
            }
        }
        Write-Host "Waiting for database readiness: $lastStatus"
        Start-Sleep -Seconds 10
    }
    if (-not $ready) {
        throw "The database readiness check did not become ready: $lastStatus. Inspect App Service logs before retrying."
    }

    Write-Host "App: $($outputs.app_url.value)"
    Write-Host "Resource group: $($outputs.resource_group_url.value)"
}
finally {
    Pop-Location
}
