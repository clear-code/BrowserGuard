$url = "http://127.0.0.1:8001/"

$listener = New-Object system.net.HttpListener
$listener.Prefixes.Add($url)

try {
    Write-Host("Running HTTP Server: http://localhost:8001")
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
    $listener.Stop()
    $listener.Dispose()
}
