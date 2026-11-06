param([Parameter(Mandatory)][string]$GameDirectory, [string]$SteamPath, [ValidatePattern("^\d+$")][string]$SteamUserId, [switch]$PrepareOnly, [switch]$EnableNativeGamepad)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$forever=(Split-Path $repo -Leaf) -eq 'MaxDps-Companion-Forever'
$app=if($forever){'MaxDpsCompanionForever'}else{'MaxDpsCompanion'}
$name=if($forever){'World of Warcraft Forever (MaxDPS)'}else{'World of Warcraft Midnight (MaxDPS)'}
$iniName=if($forever){'forever.settings.ini'}else{'settings.ini'}
$exe=Join-Path $repo "dist\$app.exe"
$gameExe=Join-Path $GameDirectory 'Wow.exe'
if(-not (Test-Path -LiteralPath $gameExe)) {throw 'Game Wow.exe not found'}
$version=[version](Get-Item -LiteralPath $gameExe).VersionInfo.FileVersion
if(($forever -and ($version.Major -ne 1 -or $version.Minor -ne 60)) -or (-not $forever -and ($version.Major -ne 12 -or $version.Minor -ne 1))) {throw 'Wrong game client for this companion'}
if(-not $SteamPath){$SteamPath=(Get-ItemProperty 'HKCU:\Software\Valve\Steam').SteamPath}
if(-not $SteamUserId){$SteamUserId=[string](Get-ItemProperty 'HKCU:\Software\Valve\Steam\ActiveProcess').ActiveUser}
$config=Join-Path $SteamPath "userdata\$SteamUserId\config"
$shortcutsFile=Join-Path $config 'shortcuts.vdf'
$localFile=Join-Path $config 'localconfig.vdf'
if(-not (Test-Path -LiteralPath $localFile)){throw 'Active Steam user config not found; specify SteamUserId'}
function Read-ZString([IO.BinaryReader]$reader){
 $bytes=[Collections.Generic.List[byte]]::new()
 while(($value=$reader.ReadByte()) -ne 0){$bytes.Add($value)}
 [Text.Encoding]::UTF8.GetString($bytes.ToArray())
}
function Read-Vdf([IO.BinaryReader]$reader){
 $result=[Collections.Specialized.OrderedDictionary]::new()
 while($reader.BaseStream.Position -lt $reader.BaseStream.Length){
  $type=$reader.ReadByte(); if($type -eq 8){return $result}
  $key=Read-ZString $reader
  $value=switch($type){0 {Read-Vdf $reader} 1 {Read-ZString $reader} 2 {$reader.ReadUInt32()} default {throw "Unsupported VDF field type $type; no changes made"}}
  $result.Add($key,$value)
 }
 return $result
}
function Write-ZString([IO.BinaryWriter]$writer,[string]$value){$writer.Write([Text.Encoding]::UTF8.GetBytes($value));$writer.Write([byte]0)}
function Write-Vdf([IO.BinaryWriter]$writer,[Collections.IDictionary]$value){
 foreach($key in $value.Keys){
  $item=$value[$key]
  if($item -is [Collections.IDictionary]){$writer.Write([byte]0);Write-ZString $writer $key;Write-Vdf $writer $item}
  elseif($item -is [string]){$writer.Write([byte]1);Write-ZString $writer $key;Write-ZString $writer $item}
  elseif($item -is [uint32]){$writer.Write([byte]2);Write-ZString $writer $key;$writer.Write([uint32]$item)}
  else{throw 'Unsupported VDF value; no changes made'}
 }
 $writer.Write([byte]8)
}
function Encode-Vdf($value){$stream=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($stream);Write-Vdf $writer $value;$writer.Flush();$bytes=$stream.ToArray();$writer.Dispose();return ,$bytes}
$original=if(Test-Path -LiteralPath $shortcutsFile){[IO.File]::ReadAllBytes($shortcutsFile)}else{$null}
if($original){
 $reader=[IO.BinaryReader]::new([IO.MemoryStream]::new($original));$root=Read-Vdf $reader;$reader.Dispose()
 if([Convert]::ToBase64String([byte[]]$original) -cne [Convert]::ToBase64String([byte[]](Encode-Vdf $root))){throw 'VDF round trip changed unrelated bytes; no changes made'}
}else{$root=[ordered]@{shortcuts=[ordered]@{}}}
$target='"'+$exe+'"'
if(-not ('MaxDpsShortcutCrc' -as [type])){
 Add-Type -TypeDefinition 'using System.Text; public static class MaxDpsShortcutCrc { public static uint AppId(string value) { uint crc=0xffffffff; foreach(byte b in Encoding.UTF8.GetBytes(value)) { crc^=b; for(int i=0;i<8;i++) crc=(crc&1)!=0 ? (crc>>1)^0xedb88320 : crc>>1; } return (~crc)|0x80000000; } }'
}
$shortcut=$null
foreach($entry in $root['shortcuts'].Values){if($entry['AppName'] -eq $name){$shortcut=$entry;break}}
if($shortcut){
 if($shortcut['Exe'] -ne $target -or $shortcut['LaunchOptions'] -ne '--steam-game-session'){throw 'Existing named Steam entry targets another application; refusing to replace it'}
 $appid=[uint32]$shortcut['appid']
 $shortcut['AllowOverlay']=[uint32]1;$shortcut['AllowDesktopConfig']=[uint32]1
}else{
 $appid=[MaxDpsShortcutCrc]::AppId($target+$name)
 foreach($entry in $root['shortcuts'].Values){if($entry['appid'] -eq $appid){throw 'Steam shortcut ID collision'}}
 $shortcut=[ordered]@{appid=[uint32]$appid;AppName=$name;Exe=$target;StartDir=('"'+(Split-Path $exe -Parent)+'"');icon=$exe;ShortcutPath='';LaunchOptions='--steam-game-session';IsHidden=[uint32]0;AllowDesktopConfig=[uint32]1;AllowOverlay=[uint32]1;OpenVR=[uint32]0;Devkit=[uint32]0;DevkitGameID='';LastPlayTime=[uint32]0;tags=[ordered]@{}}
 $index=0;while($root['shortcuts'].Contains([string]$index)){$index++};$root['shortcuts'].Add([string]$index,$shortcut)
}
$gameid=(([uint64]$appid -shl 32) -bor [uint64]0x02000000).ToString()
$signedId=[BitConverter]::ToInt32([BitConverter]::GetBytes($appid),0).ToString()
$local=[IO.File]::ReadAllText($localFile)
$block="`t`t`"$signedId`"`r`n`t`t{`r`n`t`t`t`"UseSteamControllerConfig`"`t`t`"1`"`r`n`t`t`t`"OverlayAppEnable`"`t`t`"1`"`r`n`t`t}`r`n"
$pattern='(?ms)^\t\t"'+[regex]::Escape($signedId)+'"\s*\r?\n\t\t\{.*?^\t\t\}\r?\n'
if([regex]::IsMatch($local,$pattern)){$newLocal=[regex]::Replace($local,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($match)$block},1)}
else{
 $apps=[regex]::new('(?m)^\t"apps"\s*\r?\n\t\{\r?\n');if($apps.Matches($local).Count -ne 1){throw 'Steam per-app controller section is ambiguous; no changes made'}
 $newLocal=$apps.Replace($local,[Text.RegularExpressions.MatchEvaluator]{param($match)$match.Value+$block},1)
}
$settingsFile=Join-Path $repo "dist\$iniName"
$settings=[IO.File]::ReadAllText($settingsFile)
function Set-LaunchValue([string]$text,[string]$key,[string]$value){
 $keyPattern='(?m)^'+[regex]::Escape($key)+'=.*$'
 if([regex]::IsMatch($text,$keyPattern)){return [regex]::Replace($text,$keyPattern,[Text.RegularExpressions.MatchEvaluator]{param($match)"$key=$value"})}
 if($text -notmatch '(?m)^\[Launch\]\s*$'){$text+="`r`n[Launch]`r`n"}
 return $text.Replace('[Launch]',"[Launch]`r`n$key=$value")
}
$newSettings=Set-LaunchValue $settings 'SteamLaunchEnabled' '1'
$newSettings=Set-LaunchValue $newSettings 'SteamGameId' $gameid
$newSettings=Set-LaunchValue $newSettings 'GameExecutablePath' $gameExe
if($forever){$newSettings=Set-LaunchValue $newSettings 'ForeverGamePath' $gameExe}
$wtf=Join-Path $GameDirectory 'WTF\Config.wtf'
$native=$null
if($EnableNativeGamepad){
 $native=[IO.File]::ReadAllText($wtf)
 if($native -match '(?m)^SET GamePadEnable '){$native=[regex]::Replace($native,'(?m)^SET GamePadEnable .*$', 'SET GamePadEnable "1"')}
 else{$native+="`r`nSET GamePadEnable `"1`"`r`n"}
}
$template='controller_neptune_gamepad_joystick.vdf'
if(-not (Test-Path -LiteralPath (Join-Path $SteamPath "controller_base\templates\$template"))){throw 'Steam compatible Gamepad template is missing; no changes made'}
$configSets=@()
foreach($type in @('controller_triton','controller_neptune')){
 $path=Join-Path $SteamPath "steamapps\common\Steam Controller Configs\$SteamUserId\config\configset_$type.vdf"
 $text=if(Test-Path -LiteralPath $path){[IO.File]::ReadAllText($path)}else{"`"controller_config`"`r`n{`r`n}`r`n"}
 foreach($key in @($name.ToLowerInvariant(),[string]$appid)){
  if($text -notmatch ('(?m)^\t"'+[regex]::Escape($key)+'"\s*$')){
   $entry="`t`"$key`"`r`n`t{`r`n`t`t`"template`"`t`t`"$template`"`r`n`t}`r`n"
   $header=[regex]::new('(?m)^"controller_config"\s*\r?\n\{\r?\n')
   if($header.Matches($text).Count -ne 1){throw 'Controller config-set structure is ambiguous; no changes made'}
   $text=$header.Replace($text,[Text.RegularExpressions.MatchEvaluator]{param($match)$match.Value+$entry},1)
  }
 }
 $configSets+=@{Path=$path;Text=$text}
}
$plan=[ordered]@{Name=$name;AppId=$appid;GameId=$gameid;Target=$exe;Options='--steam-game-session';OverlayEnabled=$true;SteamInputForced=$true;NativeGamepad=[bool]$EnableNativeGamepad;ControllerLayout="steam://controllerconfig/$appid";GameExecutable=$gameExe;PreparedOnly=[bool]$PrepareOnly;ControllerTemplate=$template;ControllerConfigSets=@($configSets.Path)}
if($PrepareOnly){$plan|ConvertTo-Json;return}
if(Get-Process -Name steam -ErrorAction SilentlyContinue){throw 'Exit Steam completely, then rerun setup. No files changed.'}
if(Get-Process -Name $app -ErrorAction SilentlyContinue){throw 'Close this companion before setup. No files changed.'}
if($EnableNativeGamepad -and (Get-Process -Name Wow -ErrorAction SilentlyContinue | Where-Object Path -eq $gameExe)){throw 'Close this game before changing native controller settings. No files changed.'}
$backup=Join-Path $repo ('artifacts\steam-setup-backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force|Out-Null
foreach($file in (@($shortcutsFile,$localFile,$settingsFile,$wtf)+@($configSets.Path))){if(Test-Path -LiteralPath $file){Copy-Item -LiteralPath $file -Destination $backup}}
$bytes=Encode-Vdf $root
$temp=$shortcutsFile+'.maxdps.tmp';[IO.File]::WriteAllBytes($temp,$bytes);Move-Item -LiteralPath $temp -Destination $shortcutsFile -Force
[IO.File]::WriteAllText($localFile,$newLocal,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($settingsFile,$newSettings,[Text.UTF8Encoding]::new($false))
foreach($set in $configSets){New-Item -ItemType Directory -Path (Split-Path $set.Path -Parent) -Force|Out-Null;[IO.File]::WriteAllText($set.Path,$set.Text,[Text.UTF8Encoding]::new($false))}
if($EnableNativeGamepad){[IO.File]::WriteAllText($wtf,$native,[Text.UTF8Encoding]::new($false))}
if([Convert]::ToBase64String([byte[]]$bytes) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes($shortcutsFile))){throw 'Shortcut read-back mismatch'}
$plan['Backup']=$backup;$plan|ConvertTo-Json
