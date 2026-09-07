$url = "http://127.0.0.1:8001/"

$listener = New-Object system.net.HttpListener
$listener.Prefixes.Add($url)

# Ctrl+C is read as ordinary input, so that the loop below can notice it and
# shut the listener down instead of the pipeline being torn down under it.
$readsCancelKey = $false
try {
    [Console]::TreatControlCAsInput = $true
    $readsCancelKey = $true
} catch {
    # No console to read keys from; Ctrl+C keeps its usual behaviour.
}

function Test-CancelKey {
    if (-not $readsCancelKey) { return $false }
    while ([Console]::KeyAvailable) {
        $key = [Console]::ReadKey($true)
        if ($key.Key -eq 'C' -and ($key.Modifiers -band [ConsoleModifiers]::Control)) {
            return $true
        }
    }
    return $false
}

try {
    Write-Host("Running HTTP Server: http://localhost:8001 (Ctrl+C to stop)")
    $listener.Start()
    :serve while ($true) {
        # GetContext() blocks inside .NET, where PowerShell never gets to look
        # at Ctrl+C. The context is waited for in short slices instead, so
        # control comes back often enough to notice the key.
        $task = $listener.GetContextAsync()
        while (-not $task.Wait(200)) {
            if (Test-CancelKey) { break serve }
        }
        $context = $task.Result
        $request = $context.Request
        $response = $context.Response
        $text = "N/A"
	Write-Host("{0}: {1}" -f $request.HttpMethod, $request.RawUrl)
	foreach ($headerKey in $request.Headers.AllKeys) {
	    Write-Host("{0}: {1}" -f $headerKey, $request.Headers[$headerKey])
	}
	if ($request.HttpMethod -eq "GET") {
	    $response.StatusCode = 200
        } elseif ($request.HttpMethod -eq "POST") {
            $reader = New-Object System.IO.StreamReader($request.InputStream)
            $text = $reader.ReadToEnd()
            $reader.Close()
	    $response.StatusCode = 200
	    Write-Host("BODY: {0}" -f $text)
        } else {
            # skip except GET/POST
	    $response.StatusCode = 400
        }
        $response.Close()
    }
} catch {
    Write-Error($_.Exception)
} finally {
    Write-Host("Stopping HTTP Server")
    $listener.Stop()
    $listener.Dispose()
    if ($readsCancelKey) { [Console]::TreatControlCAsInput = $false }
}
