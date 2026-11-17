param(
    [Parameter(Mandatory = $false)]
    [string]$Token = $env:GITHUB_TOKEN,

    [Parameter(Mandatory = $false)]
    [string]$RepoName = "CloudPulse-APM",

    [Parameter(Mandatory = $false)]
    [switch]$Private
)

if (-not $Token) {
    Write-Host "`n[ERROR] GitHub Personal Access Token is required to create a repository on your account." -ForegroundColor Red
    Write-Host "Please pass your token via -Token or set `$env:GITHUB_TOKEN." -ForegroundColor Yellow
    Write-Host "`nTo generate a token with 'repo' scope:" -ForegroundColor Cyan
    Write-Host "1. Visit: https://github.com/settings/tokens/new"
    Write-Host "2. Note: 'CloudPulse APM Repo Creator'"
    Write-Host "3. Check: [x] repo (Full control of private repositories)"
    Write-Host "4. Generate token and rerun:`n"
    Write-Host "   .\publish-to-github.ps1 -Token 'ghp_yourTokenHere'`n"
    exit 1
}

$headers = @{
    "Authorization" = "Bearer $Token"
    "Accept"        = "application/vnd.github.v3+json"
    "User-Agent"    = "CloudPulse-APM-Uploader"
}

$body = @{
    name        = $RepoName
    description = "Distributed Cloud Observability & Statistical Anomaly Detection Platform built on .NET 10 microservices, Serilog, custom middlewares, and Next.js / React dashboard."
    private     = $Private.IsPresent
    auto_init   = $false
} | ConvertTo-Json

Write-Host "--> Creating GitHub repository '$RepoName'..." -ForegroundColor Yellow

try {
    $createResponse = Invoke-RestMethod -Uri "https://api.github.com/user/repos" -Method Post -Headers $headers -Body $body -ContentType "application/json"
    $cloneUrl = $createResponse.clone_url
    $htmlUrl = $createResponse.html_url
    Write-Host "[SUCCESS] Repository created at: $htmlUrl" -ForegroundColor Green
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode -eq 422) {
        Write-Host "[INFO] Repository '$RepoName' might already exist on GitHub. Proceeding to push..." -ForegroundColor Yellow
        # Get authenticated user info to determine clone url
        $userResponse = Invoke-RestMethod -Uri "https://api.github.com/user" -Method Get -Headers $headers
        $username = $userResponse.login
        $cloneUrl = "https://github.com/$username/$RepoName.git"
        $htmlUrl = "https://github.com/$username/$RepoName"
    } else {
        Write-Host "[ERROR] Failed to create repository: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

# Extract username for authenticated push URL
$userResponse = Invoke-RestMethod -Uri "https://api.github.com/user" -Method Get -Headers $headers
$username = $userResponse.login

# Configure remote origin
$authenticatedRemoteUrl = "https://$($username):$($Token)@github.com/$username/$RepoName.git"

Write-Host "--> Configuring remote origin..." -ForegroundColor Yellow
git remote remove origin 2>$null
git remote add origin $authenticatedRemoteUrl

Write-Host "--> Pushing 'main' branch to $htmlUrl..." -ForegroundColor Yellow
git branch -M main
git push -u origin main

if ($LASTEXITCODE -eq 0) {
    # Replace authenticated url in local config with clean public url so token isn't stored in plain text in config
    git remote set-url origin "https://github.com/$username/$RepoName.git"
    Write-Host "`n==================================================================" -ForegroundColor Green
    Write-Host " [SUCCESS] CloudPulse APM successfully uploaded to GitHub!" -ForegroundColor Green
    Write-Host " Repository URL: $htmlUrl" -ForegroundColor Cyan
    Write-Host "==================================================================`n" -ForegroundColor Green
} else {
    Write-Host "[ERROR] Failed to push code to GitHub." -ForegroundColor Red
}
