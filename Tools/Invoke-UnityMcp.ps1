param([string]$Tool, [string]$Resource, [string]$Arguments='{}', [string]$ArgumentsFile, [switch]$ListTools)
$ErrorActionPreference='Stop'
$endpoint='http://127.0.0.1:8080/mcp'
$headers=@{Accept='application/json, text/event-stream'}
function Invoke-Rpc([object]$payload) {
    $body=$payload | ConvertTo-Json -Depth 50 -Compress
    $response=Invoke-WebRequest -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -UseBasicParsing -TimeoutSec 45
    foreach($line in ($response.Content -split "`n")) {
        if($line.StartsWith('data: ')) { $item=$line.Substring(6)|ConvertFrom-Json; if($null -ne $item.id){return $item} }
    }
    if($response.Content.StartsWith('{')) {return ($response.Content | ConvertFrom-Json)}
}
$initBody='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"vrplayer-tools","version":"1.0"}}}'
$init=Invoke-WebRequest -Uri $endpoint -Method Post -Headers $headers -ContentType 'application/json' -Body $initBody -UseBasicParsing -TimeoutSec 15
$headers['Mcp-Session-Id']=[string]($init.Headers['Mcp-Session-Id'] | Select-Object -First 1)
$null=Invoke-Rpc @{jsonrpc='2.0';method='notifications/initialized'}
if($ListTools){$result=Invoke-Rpc @{jsonrpc='2.0';id=2;method='tools/list'}; $result.result.tools|ConvertTo-Json -Depth 40; exit}
if($Resource){$result=Invoke-Rpc @{jsonrpc='2.0';id=2;method='resources/read';params=@{uri=$Resource}}}
else {
    if($ArgumentsFile){$Arguments=Get-Content -LiteralPath $ArgumentsFile -Raw -Encoding UTF8}
    $result=Invoke-Rpc @{jsonrpc='2.0';id=2;method='tools/call';params=@{name=$Tool;arguments=($Arguments|ConvertFrom-Json)}}
}
if($result.error){$result.error|ConvertTo-Json -Depth 20; exit 1}
if($result.result.contents){$result.result.contents|ForEach-Object {$_.text}}
else {$result.result.content|ForEach-Object {$_.text}}
if($result.result.isError){exit 1}
