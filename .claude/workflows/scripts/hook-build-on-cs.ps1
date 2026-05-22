#requires -Version 5.1
<#
  PostToolUse hook. Читает tool-input JSON со stdin (контракт
  Claude Code), проверяет, не правили ли только что .cs файл,
  быстро запускает dotnet build и пишет короткую сводку в stdout.

  Pipeline агентов опирается на это для плотной обратной связи:
  большинство галлюцинированных идентификаторов (несуществующие
  enum-члены, namespace, дрейф сигнатуры) падают здесь через
  секунды после правки, ещё до того, как оркестратор позовёт
  агента-валидатора.
#>

$ErrorActionPreference = 'SilentlyContinue'

try {
    $payload = $input | Out-String
    if (-not $payload) { exit 0 }
    $json = $payload | ConvertFrom-Json -ErrorAction SilentlyContinue
    if (-not $json) { exit 0 }

    $path = $json.tool_input.file_path
    if (-not $path -or -not $path.EndsWith('.cs')) { exit 0 }

    $repo = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
    Push-Location $repo
    try {
        $log = dotnet build TradingBro.slnx -nologo --verbosity quiet 2>&1
        $exit = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($exit -ne 0) {
        $errors = $log | Select-String -Pattern ': error ' | Select-Object -First 5
        $msg = "POST-WRITE BUILD FAIL (exit=$exit):`n" + ($errors -join "`n")
        $payload = @{
            decision = 'block'
            reason   = $msg
        } | ConvertTo-Json -Compress
        Write-Output $payload
        exit 2  # блокирующий выход: Claude увидит и среагирует
    }
} catch {
    # Хуки никогда не должны ронять сессию
    exit 0
}
