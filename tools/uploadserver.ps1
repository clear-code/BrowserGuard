$url = "http://127.0.0.1:8000/"

$listener = New-Object system.net.HttpListener
$listener.Prefixes.Add($url)

try {
    Write-Host("Running File Uploading Server: http://localhost:8000")
    $listener.Start()
    while ($true) {
	$context = $listener.GetContext()
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
    $listener.Stop()
    $listener.Dispose()
}
