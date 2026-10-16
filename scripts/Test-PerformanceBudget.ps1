[CmdletBinding()]
param([Parameter(Mandatory)][string]$ResultsDir)
$ErrorActionPreference='Stop'
$failed=$false
function Read-Metrics([string]$file) {
    $result=@{}
    foreach($row in Import-Csv -LiteralPath $file -Delimiter "`t") {$result[$row.metric]=$row.value}
    return $result
}
function Number([string]$text) {return [double]::Parse($text,[Globalization.CultureInfo]::InvariantCulture)}
foreach($role in @('host','client')) {
    $offline=Read-Metrics (Join-Path $ResultsDir "$role-dual-offline-summary.tsv")
    $online=Read-Metrics (Join-Path $ResultsDir "$role-connected-summary.tsv")
    foreach($key in @('build','level','resolution','fps-cap','npc-ai')) {
        if($offline[$key] -ne $online[$key]) {throw "Mismatched $key in $role comparison."}
    }
    if((Number $online['frames']) -lt 50) {throw 'Incomplete connected sample.'}
    $retained=(Number $online['mean-fps'])/(Number $offline['mean-fps'])
    $p99=Number $online['frame-p99-ms']
    $stalls=Number $online['over-50-ms']
    $verdict=$retained -ge .90 -and $p99 -le 33.333 -and $stalls -le 1
    Write-Output ("{0}: {1}; FPS retained={2:P1}, p99={3:F3} ms, stalls >50ms={4}" -f $role,$(if($verdict){'PASS'}else{'FAIL'}),$retained,$p99,$stalls)
    if(!$verdict) {$failed=$true}
}
$world=Import-Csv -LiteralPath (Join-Path $ResultsDir 'host-connected-sections.tsv') -Delimiter "`t" | Where-Object section -eq world
$p95=Number $world.'p95-ms';$max=Number $world.'max-ms'
$worldOk=$p95 -le 4 -and $max -le 16
Write-Output ("host world: {0}; p95={1:F3} ms (budget 4), max={2:F3} ms (budget 16)" -f $(if($worldOk){'PASS'}else{'FAIL'}),$p95,$max)
if(!$worldOk) {$failed=$true}
if($failed) {exit 1}
