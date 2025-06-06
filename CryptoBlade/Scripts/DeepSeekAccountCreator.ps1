param (
    [int]$AccountCount = 1,
    [string]$OutputFile = "appsettings.Accounts.json"
)

Import-Module Selenium

$ChromeOptions = New-Object OpenQA.Selenium.Chrome.ChromeOptions
$ChromeOptions.AddArgument("--start-maximized")
$ChromeOptions.AddArgument("--disable-blink-features=AutomationControlled")
$ChromeOptions.AddArgument("--no-sandbox")
$ChromeOptions.AddArgument("--disable-dev-shm-usage")
$ChromeOptions.AddArgument("--disable-gpu")


# Dodaj przed utworzeniem drivera:
$ChromeOptions.AddArgument("--disable-dev-shm-usage")
$ChromeOptions.AddArgument("--no-sandbox")
$ChromeOptions.AddArgument("--disable-setuid-sandbox")

# Ustawienie ścieżki do ChromeDriver
$DriverService = [OpenQA.Selenium.Chrome.ChromeDriverService]::CreateDefaultService()
$DriverService.ChromeDriverPath

# Wyłącz headless dla debugowania
# $ChromeOptions.AddArgument("--headless=new")

function Get-TempEmail {
    # Generuj losowy email bez API
    $Domains = @("gmail.com", "yahoo.com", "outlook.com", "proton.me", "hotmail.com")
    $RandomName = (-join ((65..90) + (97..122) | Get-Random -Count 10 | % {[char]$_}))
    $RandomDomain = $Domains | Get-Random
    
    return "${RandomName}@${RandomDomain}"
}

function Get-VerificationCode {
    param($TempEmail)
    
    Write-Host "--------------------------------------------------------" -ForegroundColor Yellow
    Write-Host " AUTOMATION PAUSED" -ForegroundColor Yellow
    Write-Host " Please check verification code for: $TempEmail" -ForegroundColor Yellow
    Write-Host " You'll need to access this email manually" -ForegroundColor Yellow
    Write-Host "--------------------------------------------------------" -ForegroundColor Yellow
    
    return Read-Host "Enter verification code"
}

function Generate-StrongPassword {
    $Chars = [char[]]'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()_+-='
    return -join (1..16 | ForEach-Object { $Chars | Get-Random })
}

function Get-DeepSeekApiKey {
    param($Driver)
    
    # Przejdź do API keys
    $Driver.Navigate().GoToUrl("https://platform.deepseek.com/api_keys")
    
    # Poczekaj na załadowanie strony
    Start-Sleep -Seconds 5
    
    # Kliknij przycisk
    try {
        $CreateButton = $Driver.FindElement([OpenQA.Selenium.By]::CssSelector("button.btn-primary"))
        $CreateButton.Click()
    }
    catch {
        # Alternatywny sposób
        $CreateButton = $Driver.FindElement([OpenQA.Selenium.By]::XPath("//button[contains(., 'Create')]"))
        $CreateButton.Click()
    }
    
    # Poczekaj na wygenerowanie klucza
    Start-Sleep -Seconds 5
    
    # Pobierz klucz
    $KeyElement = $Driver.FindElement([OpenQA.Selenium.By]::CssSelector(".api-key-value, .api-key-display"))
    return $KeyElement.Text
}

function Protect-Data {
    param($Data)
    
    $SecureString = ConvertTo-SecureString -String $Data -AsPlainText -Force
    return ConvertFrom-SecureString -SecureString $SecureString
}

function Unprotect-Data {
    param($EncryptedData)
    
    $SecureString = ConvertTo-SecureString -String $EncryptedData
    $Ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($Ptr)
}

try {
    $Accounts = @()
    if (Test-Path $OutputFile) {
        try {
            $EncryptedData = Get-Content $OutputFile -Raw
            $Json = Unprotect-Data -EncryptedData $EncryptedData
            $Accounts = $Json | ConvertFrom-Json
        }
        catch {
            Write-Host "Error reading config: $_" -ForegroundColor Yellow
        }
    }

    Write-Host "Starting ChromeDriver..."
    $DriverService = [OpenQA.Selenium.Chrome.ChromeDriverService]::CreateDefaultService()
    $DriverService.HideCommandPromptWindow = $true
    $Driver = New-Object OpenQA.Selenium.Chrome.ChromeDriver($DriverService, $ChromeOptions)

    for ($i = 1; $i -le $AccountCount; $i++) {
        try {
            Write-Host "Creating account $i/$AccountCount"
            
            $TempEmail = Get-TempEmail
            Write-Host "Temporary email: $TempEmail"
            
            # Przejdź do strony rejestracji
            $Driver.Navigate().GoToUrl("https://platform.deepseek.com/sign_up")
            
            # Poczekaj na załadowanie strony
            Start-Sleep -Seconds 5
            
            # Wprowadź email
            $EmailField = $Driver.FindElement([OpenQA.Selenium.By]::CssSelector("input[type='email']"))
            $EmailField.SendKeys($TempEmail)
            
            # Kliknij przycisk
            $GetCodeButton = $Driver.FindElement([OpenQA.Selenium.By]::CssSelector("button.btn-primary"))
            $GetCodeButton.Click()
            
            Write-Host "Waiting for verification code..."
            $VerificationCode = Get-VerificationCode -TempEmail $TempEmail
            Write-Host "Verification code: $VerificationCode"
            
            # Wprowadź kod
            $CodeInputs = $Driver.FindElements([OpenQA.Selenium.By]::CssSelector("input[type='text']"))
            for ($j = 0; $j -lt 6; $j++) {
                $CodeInputs[$j].SendKeys($VerificationCode[$j])
            }
            
            # Utwórz hasło
            $Password = Generate-StrongPassword
            $PasswordField = $Driver.FindElement([OpenQA.Selenium.By]::CssSelector("input[type='password']"))
            $PasswordField.SendKeys($Password)
            
            # Kliknij rejestrację
            $SignUpButton = $Driver.FindElement([OpenQA.Selenium.By]::CssSelector("button[type='submit']"))
            $SignUpButton.Click()
            
            # Poczekaj na załadowanie
            Start-Sleep -Seconds 10
            
            # Sprawdź czy rejestracja się powiodła
            if ($Driver.Url -match "error") {
                throw "Registration failed. Page: $($Driver.Url)"
            }
            
            # Pobierz klucz API
            $ApiKey = Get-DeepSeekApiKey -Driver $Driver
            Write-Host "API Key: $($ApiKey.Substring(0, 10))..."
            
            # Dodaj do listy
            $Accounts += [PSCustomObject]@{
                Email      = $TempEmail
                ApiKey     = $ApiKey
                CreatedAt  = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            }
            
            # Zapisz tymczasowo
            $Json = $Accounts | ConvertTo-Json
            $Encrypted = Protect-Data -Data $Json
            $Encrypted | Set-Content $OutputFile
            
            Write-Host "Account created successfully!" -ForegroundColor Green
        }
        catch {
            Write-Host "Account creation failed: $_" -ForegroundColor Red
            
            # Spróbuj zrobić screenshot alternatywną metodą
            try {
                $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
                $screenshot = $Driver.GetScreenshot()
                $screenshot.SaveAsFile("${PWD}/error_${timestamp}.png")
            }
            catch {
                Write-Host "Screenshot failed: $_" -ForegroundColor Yellow
            }
        }
        finally {
            Start-Sleep -Seconds 3
        }
    }
}
catch {
    Write-Host "Critical error: $_" -ForegroundColor Red
}
finally {
    if ($Accounts) {
        $Json = $Accounts | ConvertTo-Json
        $Encrypted = Protect-Data -Data $Json
        $Encrypted | Set-Content $OutputFile
    }
    
    if ($Driver) {
        try {
            $Driver.Quit()
        }
        catch {
            Write-Host "Error closing driver: $_" -ForegroundColor Yellow
        }
    }
    
    Write-Host "Created $($Accounts.Count) accounts. Keys saved to $OutputFile"
}