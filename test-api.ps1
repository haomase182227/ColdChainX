# Test ColdChainX API on Render
$baseUrl = "https://coldchainx.onrender.com"

Write-Host "Testing ColdChainX API..." -ForegroundColor Cyan
Write-Host ""

# Test 1: Health Check
Write-Host "1. Testing Health Check..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/" -Method Get
    Write-Host "✅ Health Check: SUCCESS" -ForegroundColor Green
    $response | ConvertTo-Json
} catch {
    Write-Host "❌ Health Check: FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message
}

Write-Host ""

# Test 2: Register User
Write-Host "2. Testing User Registration..." -ForegroundColor Yellow
$registerData = @{
    email = "test@coldchainx.com"
    password = "Test@123456"
    fullName = "Test User"
    phoneNumber = "0123456789"
    role = "User"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/auth/register" -Method Post -Body $registerData -ContentType "application/json"
    Write-Host "✅ Register: SUCCESS" -ForegroundColor Green
    $response | ConvertTo-Json
} catch {
    Write-Host "⚠️ Register: " -ForegroundColor Yellow -NoNewline
    Write-Host $_.Exception.Message
}

Write-Host ""

# Test 3: Login
Write-Host "3. Testing User Login..." -ForegroundColor Yellow
$loginData = @{
    email = "test@coldchainx.com"
    password = "Test@123456"
} | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $loginData -ContentType "application/json"
    Write-Host "✅ Login: SUCCESS" -ForegroundColor Green
    $response | ConvertTo-Json
    
    # Save token for future tests
    $global:accessToken = $response.data.accessToken
    $global:refreshToken = $response.data.refreshToken
} catch {
    Write-Host "❌ Login: FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message
}

Write-Host ""
Write-Host "Testing completed!" -ForegroundColor Cyan
Write-Host "Visit Swagger UI: $baseUrl/swagger" -ForegroundColor Cyan
