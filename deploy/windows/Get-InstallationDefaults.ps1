#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputFile,
    [string]$DataRoot = (Join-Path $env:ProgramData 'PalworldPanel')
)
$ErrorActionPreference = 'Stop'
function Is-PrivateIPv4([string]$Value) {
    $address = $null
    if (-not [Net.IPAddress]::TryParse($Value,[ref]$address) -or $address.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { return $false }
    $bytes = $address.GetAddressBytes()
    return ($bytes[0] -eq 10 -or ($bytes[0] -eq 172 -and $bytes[1] -ge 16 -and $bytes[1] -le 31) -or ($bytes[0] -eq 192 -and $bytes[1] -eq 168))
}
$addresses = @(Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.AddressState -eq 'Preferred' -and (Is-PrivateIPv4 $_.IPAddress) })
$interfaces = @(Get-NetIPConfiguration | Where-Object IPv4DefaultGateway | Sort-Object @{Expression={$_.NetIPv4Interface.InterfaceMetric}})
$bindIp = ''
foreach ($interface in $interfaces) {
    $match = $addresses | Where-Object InterfaceIndex -eq $interface.InterfaceIndex | Select-Object -First 1
    if ($match) { $bindIp = $match.IPAddress; break }
}
if (-not $bindIp) {
    $physical = @(Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Select-Object -ExpandProperty ifIndex)
    $match = $addresses | Sort-Object @{Expression={if ($_.InterfaceIndex -in $physical) { 0 } else { 1 }}},InterfaceIndex | Select-Object -First 1
    if ($match) { $bindIp = $match.IPAddress }
}
$defaults = @{bindIp=$bindIp;port='18080';allowedIps=$bindIp}
$config = Join-Path $DataRoot 'private/panel.json'
if (Test-Path -LiteralPath $config -PathType Leaf) {
    # Only export non-secret network fields. Never copy the configuration itself.
    $existing = Get-Content -LiteralPath $config -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($existing.bindIp -in $addresses.IPAddress) { $defaults.bindIp = [string]$existing.bindIp }
    $port = 0
    if ([int]::TryParse([string]$existing.port,[ref]$port) -and $port -ge 1024 -and $port -le 65535) { $defaults.port = [string]$port }
    $allowed = @($existing.allowedIps | Where-Object { Is-PrivateIPv4 ([string]$_) })
    if ($allowed.Count) { $defaults.allowedIps = $allowed -join ',' }
    else { $defaults.allowedIps = $defaults.bindIp }
}
# A fixed three-line UTF-8 format lets Inno load values without a JSON dependency.
[IO.File]::WriteAllText($OutputFile,($defaults.bindIp + "`n" + $defaults.port + "`n" + $defaults.allowedIps),[Text.UTF8Encoding]::new($false))
