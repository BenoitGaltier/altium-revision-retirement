$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'scripts\registry-xml.ps1')
$root=Join-Path $project ('.artifacts\bytes-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
[IO.Directory]::CreateDirectory($root) | Out-Null
$passed=0
function Assert($value,[string]$message) {if(-not $value) {throw $message}}
function Reject($action) {$failed=$false;try {& $action} catch {$failed=$true};Assert $failed 'Unsafe byte patch was accepted'}
$hrid='AltiumRevisionRetirement'
$guid='f19cf220-6b04-4d43-a356-eebe90b392e6'
$styles=@("`r`n","`n","`r")
foreach($encodingName in @('utf-8','utf-16','utf-16BE','utf-32','iso-8859-1')) {
    foreach($newline in $styles) {
        foreach($bom in @($false,$true)) {
            if($encodingName -eq 'iso-8859-1' -and $bom) {continue}
            $encoding=[Text.Encoding]::GetEncoding($encodingName)
            $name=if($encodingName -eq 'utf-16BE') {'utf-16'} else {$encodingName}
            $accent=[char]0x00F3
            $foreign="    <Item HRID = 'VaultExplorer' Guid = 'native' custom = 'a &gt; b&#x9;c'><Description>Extensi${accent}n&#13;&#10;Second</Description><!-- <Item HRID='$hrid'/> --><Data><![CDATA[<Item HRID='$hrid' />]]></Data></Item>"
            $xml="<?xml version='1.0' encoding='$name'?>$newline<?vendor keep='exact'?>$newline<Extensions xmlns:v='urn:vendor' flag = 'UNCHANGED'>$newline$foreign$newline</Extensions>$newline<!--trailing comment-->"
            $data=$encoding.GetBytes($xml)
            if($bom) {$data=[byte[]]($encoding.GetPreamble()+$data)}
            $path=Join-Path $root ('r'+$passed+'.xml')
            [IO.File]::WriteAllBytes($path,$data)
            [xml]$entry='<Item HRID="AltiumRevisionRetirement" Guid="'+$guid+'"><Path>new-path</Path><Version>0.1.8.0</Version></Item>'
            $patch=New-RegistryEntryPatch $path $entry.DocumentElement $hrid
            Assert-RegistryBytePatch $data $patch.Bytes $patch
            Assert ($patch.Removed -eq 0) 'Adding an entry removed original registry bytes'
            $candidate=Read-RegistryXmlBytes $patch.Bytes
            Assert (($candidate.Extensions.Item | Where-Object {$_.HRID -eq $hrid}).Version -eq '0.1.8.0') 'Native entry schema was not retained'
            # Oracle independent of the patch helper: prefix and suffix must be exact original byte slices.
            Assert ([Convert]::ToBase64String($patch.Bytes,0,$patch.Start) -ceq [Convert]::ToBase64String($data,0,$patch.Start)) 'Original prefix was rewritten'
            Assert ([Convert]::ToBase64String($patch.Bytes,$patch.Start+$patch.Inserted,$data.Length-$patch.Start) -ceq [Convert]::ToBase64String($data,$patch.Start,$data.Length-$patch.Start)) 'Original suffix was rewritten'
            [IO.File]::WriteAllBytes($path,$patch.Bytes)
            $own=$candidate.Extensions.Item | Where-Object {$_.HRID -eq $hrid}
            $own.Version='0.1.8.1'
            $update=New-RegistryEntryPatch $path $own $hrid
            Assert-RegistryBytePatch $patch.Bytes $update.Bytes $update
            Assert ($update.Removed -gt 0) 'Updating did not target the existing entry'
            $restored=[byte[]]($update.Bytes[0..($update.Start-1)]+$patch.Bytes[$update.Start..($update.Start+$update.Removed-1)]+$update.Bytes[($update.Start+$update.Inserted)..($update.Bytes.Length-1)])
            Assert ((Get-RegistryByteHash $restored) -ceq (Get-RegistryByteHash $patch.Bytes)) 'Update changed bytes outside its owned entry'
            $passed++
        }
    }
}
foreach($xml in @('<Extensions/>','<?xml version="1.0"?><Extensions flag="keep" />',('<Extensions><Item HRID="AltiumRevisionRetirement" Guid="'+$guid+'"/><Item HRID="VaultExplorer" /></Extensions>'))) {
    $path=Join-Path $root ('small'+$passed+'.xml');[IO.File]::WriteAllText($path,$xml,[Text.UTF8Encoding]::new($false))
    [xml]$entry='<Item HRID="AltiumRevisionRetirement" Guid="'+$guid+'"><Path>new-path</Path></Item>'
    $data=[IO.File]::ReadAllBytes($path);$patch=New-RegistryEntryPatch $path $entry.DocumentElement $hrid
    Assert-RegistryBytePatch $data $patch.Bytes $patch;$passed++
}
$path=Join-Path $root 'tamper.xml'
[IO.File]::WriteAllText($path,'<Extensions><Item HRID="VaultExplorer" flag="keep" /></Extensions>')
[xml]$entry='<Item HRID="AltiumRevisionRetirement" Guid="'+$guid+'"><Path>new-path</Path></Item>'
$data=[IO.File]::ReadAllBytes($path);$patch=New-RegistryEntryPatch $path $entry.DocumentElement $hrid
$tampered=[byte[]]$patch.Bytes.Clone();$tampered[2]=$tampered[2] -bxor 1
Reject {Assert-RegistryBytePatch $data $tampered $patch}
# Even a recomputed overall checksum cannot hide an unrelated edit from the span proof.
$patch.ResultSHA256=Get-RegistryByteHash $tampered
Reject {Assert-RegistryBytePatch $data $tampered $patch}
[IO.File]::WriteAllText($path,'<Extensions><!-- current changed --></Extensions>')
Reject {Assert-RegistryBytePatch ([IO.File]::ReadAllBytes($path)) $patch.Bytes $patch}
[IO.File]::WriteAllText($path,('<Extensions><Item HRID="'+$hrid+'"/><Item HRID="'+$hrid+'"/></Extensions>'))
Reject {New-RegistryEntryPatch $path $entry.DocumentElement $hrid | Out-Null}
foreach($xml in @('<Extensions xml:space="preserve">  <!--keep-->  </Extensions>',('<Extensions><!--'+[char]::ConvertFromUtf32(0x1F527)+'--><Item HRID="Other" /></Extensions>'))) {
    [IO.File]::WriteAllText($path,$xml,[Text.UTF8Encoding]::new($false));$data=[IO.File]::ReadAllBytes($path)
    $patch=New-RegistryEntryPatch $path $entry.DocumentElement $hrid
    Assert-RegistryBytePatch $data $patch.Bytes $patch;Assert ($patch.Removed -eq 0) 'Unexpected byte removal';$passed++
}
Write-Output ($passed.ToString()+' byte-preservation cases passed; BOM, encoding, declaration, comments, entities, layout and unrelated entries retained.')
