# Copies the HTML/CSS UI proof of concept into a Simitone release folder.
# Run with PowerShell 7 (pwsh): Windows PowerShell 5.1 mangles non-ASCII characters in paths without a BOM.
# Build first:  dotnet build Tools/Conversations -c Release -o Tools/Conversations/out
param(
  [string]$App = "$env:USERPROFILE\.t3\projects\the-sims-run\app",
  [string]$Backup = "$env:USERPROFILE\.t3\projects\the-sims-patch\backup\pre-htmlui"
)
$out = Join-Path $PSScriptRoot '..\Conversations\out'
New-Item -ItemType Directory -Force $Backup | Out-Null

# The game ships older copies of these helper assemblies; AngleSharp needs the newer ones, so they replace the old ones and the exe gets binding redirects.
$libs = 'System.Memory','System.Buffers','System.Runtime.CompilerServices.Unsafe','System.Numerics.Vectors','System.ValueTuple','System.Text.Encoding.CodePages'
foreach ($n in $libs) {
  if (-not (Test-Path "$Backup\$n.dll")) { Copy-Item "$App\$n.dll" $Backup }
  Copy-Item "$out\$n.dll" $App -Force
}
if (-not (Test-Path "$Backup\Simitone.Windows.exe.config")) { Copy-Item "$App\Simitone.Windows.exe.config" $Backup }
Copy-Item "$out\Conversations.dll", "$out\HtmlUi.dll", "$out\AngleSharp.dll", "$out\AngleSharp.Css.dll" $App -Force

$redirects = ''
foreach ($n in $libs) {
  $v = [Reflection.AssemblyName]::GetAssemblyName("$out\$n.dll")
  $t = ($v.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join ''
  $redirects += "      <dependentAssembly><assemblyIdentity name=`"$n`" publicKeyToken=`"$t`" culture=`"neutral`" /><bindingRedirect oldVersion=`"0.0.0.0-$($v.Version)`" newVersion=`"$($v.Version)`" /></dependentAssembly>`r`n"
}
$cfg = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n<configuration>`r`n<startup><supportedRuntime version=`"v4.0`" sku=`".NETFramework,Version=v4.6.1`"/></startup>`r`n  <runtime>`r`n    <assemblyBinding xmlns=`"urn:schemas-microsoft-com:asm.v1`">`r`n$redirects    </assemblyBinding>`r`n  </runtime>`r`n</configuration>`r`n"
[IO.File]::WriteAllText("$App\Simitone.Windows.exe.config", $cfg, (New-Object Text.UTF8Encoding($false)))

Copy-Item (Join-Path $PSScriptRoot 'samples\*.html') (Join-Path $App 'Content\ui') -Force
"deployed. In game: simtool convo htmlui memory.html   (edit Content\ui\memory.html while it is open to see it reload)"
