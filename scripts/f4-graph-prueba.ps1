<#
  Prueba 6 de RadarEmpleo (F4): mandarte A TI MISMO un correo con un PDF de ejemplo desde tu hotmail, con Microsoft Graph.
  Lo ejecuta Eduardo, no Claude. No guarda el token en ningún sitio: solo vive mientras dura el script.

  Uso (en una ventana de PowerShell):
     powershell -ExecutionPolicy Bypass -File "<ruta de este archivo>" -ClientId "<Id. de aplicación (cliente) que copiaste de Entra>"

  Qué hace:
   1. Te enseña un código y la dirección https://microsoft.com/devicelogin . Abres esa página en tu navegador,
      pegas el código, entras con tu hotmail y aceptas los permisos (enviar correo y leer tu perfil).
   2. Lee tu dirección de correo (/me).
   3. Crea un PDF de ejemplo de una página y te lo manda a ti mismo, con copia en Enviados.
#>
param(
  [Parameter(Mandatory = $true)][string]$ClientId
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$tenant = 'consumers'   # solo cuentas personales de Microsoft (hotmail/outlook.com)
$scope  = 'https://graph.microsoft.com/Mail.Send https://graph.microsoft.com/User.Read'

# 1) Código de dispositivo
$dc = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/devicecode" `
      -Body @{ client_id = $ClientId; scope = $scope }
Write-Host ''
Write-Host '== PASO 1 ==' -ForegroundColor Cyan
Write-Host "Abre en tu navegador:  $($dc.verification_uri)"
Write-Host "Escribe este código:   $($dc.user_code)" -ForegroundColor Yellow
Write-Host '(entra con tu hotmail y acepta los permisos; este script espera solo)'

# 2) Esperar a que inicies sesión
$token = $null
$limite = (Get-Date).AddSeconds([int]$dc.expires_in)
while (-not $token -and (Get-Date) -lt $limite) {
  Start-Sleep -Seconds ([int]$dc.interval)
  try {
    $r = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$tenant/oauth2/v2.0/token" -Body @{
      grant_type  = 'urn:ietf:params:oauth:grant-type:device_code'
      client_id   = $ClientId
      device_code = $dc.device_code
    }
    $token = $r.access_token
  } catch {
    $err = $null
    try { $err = ($_.ErrorDetails.Message | ConvertFrom-Json).error } catch {}
    if ($err -eq 'authorization_pending') { continue }
    if ($err -eq 'slow_down') { Start-Sleep -Seconds 5; continue }
    throw "No se pudo iniciar sesión: $($_.ErrorDetails.Message)"
  }
}
if (-not $token) { throw 'Se acabó el tiempo para escribir el código. Vuelve a ejecutar el script.' }
$cab = @{ Authorization = "Bearer $token" }
Write-Host 'Sesión iniciada.' -ForegroundColor Green

# 3) Tu dirección
$yo = Invoke-RestMethod -Uri 'https://graph.microsoft.com/v1.0/me?$select=displayName,mail,userPrincipalName' -Headers $cab
$correo = if ($yo.mail) { $yo.mail } else { $yo.userPrincipalName }
Write-Host "Cuenta: $($yo.displayName) <$correo>"

# 4) PDF de ejemplo (una página, generado aquí mismo; no es tu CV)
$texto = 'PDF de prueba de RadarEmpleo - ' + (Get-Date -Format 'dd/MM/yyyy HH:mm')
$contenido = "BT /F1 18 Tf 72 760 Td ($texto) Tj ET"
$objs = @(
  '<</Type/Catalog/Pages 2 0 R>>',
  '<</Type/Pages/Kids[3 0 R]/Count 1>>',
  '<</Type/Page/Parent 2 0 R/MediaBox[0 0 595 842]/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>',
  "<</Length $($contenido.Length)>>`nstream`n$contenido`nendstream",
  '<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>'
)
$sb = New-Object Text.StringBuilder; [void]$sb.Append("%PDF-1.4`n"); $offs = @()
for ($i = 0; $i -lt $objs.Count; $i++) { $offs += $sb.Length; [void]$sb.Append("$($i+1) 0 obj$($objs[$i])endobj`n") }
$xref = $sb.Length
[void]$sb.Append("xref`n0 $($objs.Count + 1)`n0000000000 65535 f `n")
foreach ($o in $offs) { [void]$sb.Append(('{0:D10} 00000 n `n' -f $o)) }
[void]$sb.Append("trailer<</Size $($objs.Count + 1)/Root 1 0 R>>`nstartxref`n$xref`n%%EOF`n")
$pdfBytes = [Text.Encoding]::ASCII.GetBytes($sb.ToString())

# 5) Enviar el correo a ti mismo (se guarda en Enviados)
$mensaje = @{
  message = @{
    subject      = 'Prueba de RadarEmpleo: correo con PDF'
    body         = @{ contentType = 'Text'; content = "Hola, Eduardo:`n`nEste correo lo ha enviado el script de prueba de RadarEmpleo con Microsoft Graph. Si lo ves en tu bandeja de entrada y en Enviados, con el PDF adjunto, la prueba 6 funciona.`n" }
    toRecipients = @(@{ emailAddress = @{ address = $correo } })
    attachments  = @(@{
      '@odata.type' = '#microsoft.graph.fileAttachment'
      name          = 'Prueba_RadarEmpleo.pdf'
      contentType   = 'application/pdf'
      contentBytes  = [Convert]::ToBase64String($pdfBytes)
    })
  }
  saveToSentItems = $true
} | ConvertTo-Json -Depth 8
Invoke-RestMethod -Method Post -Uri 'https://graph.microsoft.com/v1.0/me/sendMail' -Headers $cab `
  -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($mensaje)) | Out-Null
Write-Host ''
Write-Host "Correo enviado a $correo. Míralo en tu bandeja de entrada y en Enviados." -ForegroundColor Green
