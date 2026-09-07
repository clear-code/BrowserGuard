$url = "http://127.0.0.1:8000/"

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
    Write-Host("Running File Uploading Server: http://localhost:8000 (Ctrl+C to stop)")
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
	    # show upload form
	    $text = "<html><body><form action='/' method='POST' enctype='multipart/form-data'><label for='upload'>Select file: <input type='file' name='file'/><br/><input type='submit' value='Submit'/></form></body></html>"
	    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
	    $response.ContentLength64 = $bytes.Length
	    $output = $response.OutputStream
	    $output.Write($bytes, 0, $bytes.Length)
	    $output.Close()
	} elseif ($request.HttpMethod -eq "POST") {
	    $reader = New-Object System.IO.StreamReader($request.InputStream)
	    $binaryText = $reader.ReadToEnd()
	    $reader.Close()
	    # emulate upload OK
	    $response.StatusCode = 200
	    Write-Host("UPLOADED: {0}: {1} uploaded" -f $request.HttpMethod, $request.RawUrl)
	    $text = "<html><meta http-equiv='refresh' content='3;http://localhost:8000'><body>file was uploaded. redirect in 3 seconds.</body></html>"
	    $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
	    $response.ContentLength64 = $bytes.Length
	    $output = $response.OutputStream
	    $output.Write($bytes, 0, $bytes.Length)
	    $output.Close()
	    continue
	} else {
	    # skip except GET/POST
	    $response.StatusCode = 400
	    $response.Close()
	    continue
	}
    }
} catch {
    Write-Error($_.Exception)
} finally {
    Write-Host("Stopping File Uploading Server")
    $listener.Stop()
    $listener.Dispose()
    if ($readsCancelKey) { [Console]::TreatControlCAsInput = $false }
}
