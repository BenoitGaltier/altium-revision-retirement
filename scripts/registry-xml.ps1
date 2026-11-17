# Read XML bytes through XmlReader so the declaration/BOM, not the PowerShell locale, selects the encoding.
function Read-RegistryXmlBytes([byte[]]$Bytes) {
    $settings=New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver=$null
    $settings.IgnoreWhitespace=$false
    $stream=[IO.MemoryStream]::new($Bytes,$false)
    $reader=[Xml.XmlReader]::Create($stream,$settings)
    try {
        $document=New-Object System.Xml.XmlDocument
        $document.PreserveWhitespace=$true
        $document.XmlResolver=$null
        $document.Load($reader)
        return ,$document
    } finally {$reader.Dispose();$stream.Dispose()}
}
function Read-ExtensionRegistryXml([string]$Path) {
    return ,(Read-RegistryXmlBytes ([IO.File]::ReadAllBytes($Path)))
}
function Get-RegistryByteHash([byte[]]$Bytes,[int]$Offset=0,[int]$Count=-1) {
    if($Count -lt 0) {$Count=$Bytes.Length-$Offset}
    $hash=[Security.Cryptography.SHA256]::Create()
    try {return [BitConverter]::ToString($hash.ComputeHash($Bytes,$Offset,$Count)).Replace('-','')} finally {$hash.Dispose()}
}
function Get-XmlTagEnd([string]$Text,[int]$Start) {
    $quote=[char]0
    for($i=$Start;$i -lt $Text.Length;$i++) {
        $c=$Text[$i]
        if($quote -ne [char]0) {if($c -eq $quote) {$quote=[char]0}}
        elseif($c -eq [char]34 -or $c -eq [char]39) {$quote=$c}
        elseif($c -eq '>') {return $i+1}
    }
    throw 'Incomplete XML tag. The registry was not changed.'
}
function Get-RegistryTokenStart($Reader,[string]$Text,$Lines) {
    $position=$Lines[$Reader.LineNumber-1]+$Reader.LinePosition-1
    $start=$Text.LastIndexOf('<',[Math]::Min($position,$Text.Length-1))
    $token=if($Reader.NodeType -eq [Xml.XmlNodeType]::EndElement) {'</'+$Reader.Name} else {'<'+$Reader.Name}
    if($start -lt 0 -or -not $Text.Substring($start).StartsWith($token,[StringComparison]::Ordinal)) {throw 'Cannot locate the exact XML entry. The registry was not changed.'}
    return $start
}
function New-RegistryEntryPatch([string]$Path,[Xml.XmlElement]$Entry,[string]$Hrid) {
    if($Entry.Name -cne 'Item' -or $Entry.NamespaceURI -ne '' -or -not [string]::Equals($Entry.GetAttribute('HRID'),$Hrid,[StringComparison]::OrdinalIgnoreCase)) {throw 'Invalid extension entry for byte patch.'}
    $bytes=[IO.File]::ReadAllBytes($Path)
    $original=Read-RegistryXmlBytes $bytes
    if($original.DocumentElement.Name -cne 'Extensions' -or $original.DocumentElement.NamespaceURI -ne '') {throw 'Unsupported Altium registry structure. The registry was not changed.'}
    $stream=[IO.MemoryStream]::new($bytes,$false)
    $encodingReader=[Xml.XmlTextReader]::new($stream)
    $encodingReader.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$encodingReader.XmlResolver=$null
    try {[void]$encodingReader.Read();$encoding=$encodingReader.Encoding.Clone()} finally {$encodingReader.Dispose();$stream.Dispose()}
    $encoding.EncoderFallback=[Text.EncoderFallback]::ExceptionFallback
    $encoding.DecoderFallback=[Text.DecoderFallback]::ExceptionFallback
    $bom=0
    foreach($prefix in @('0000FEFF','FFFE0000','EFBBBF','FEFF','FFFE')) {
        $count=$prefix.Length/2
        if($bytes.Length -ge $count -and [BitConverter]::ToString($bytes,0,$count).Replace('-','') -eq $prefix) {$bom=$count;break}
    }
    $text=$encoding.GetString($bytes,$bom,$bytes.Length-$bom)
    $roundtrip=$encoding.GetBytes($text)
    if((Get-RegistryByteHash $roundtrip) -cne (Get-RegistryByteHash $bytes $bom ($bytes.Length-$bom))) {throw 'The registry encoding cannot be mapped losslessly. The registry was not changed.'}
    $lines=[Collections.Generic.List[int]]::new();$lines.Add(0)
    for($i=0;$i -lt $text.Length;$i++) {
        if($text[$i] -eq "`r") {if($i+1 -lt $text.Length -and $text[$i+1] -eq "`n") {$i++};$lines.Add($i+1)}
        elseif($text[$i] -eq "`n") {$lines.Add($i+1)}
    }
    $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
    $inputText=[IO.StringReader]::new($text)
    $reader=[Xml.XmlReader]::Create($inputText,$settings)
    $start=-1;$end=-1;$rootClose=-1;$rootEmpty=-1;$firstItem=-1;$entryCount=0
    try {
        while($reader.Read()) {
            if($reader.NodeType -eq [Xml.XmlNodeType]::Element -and $reader.Depth -eq 0 -and $reader.IsEmptyElement) {$rootEmpty=(Get-XmlTagEnd $text (Get-RegistryTokenStart $reader $text $lines))-2}
            if($reader.NodeType -eq [Xml.XmlNodeType]::EndElement -and $reader.Depth -eq 0) {$rootClose=Get-RegistryTokenStart $reader $text $lines}
            if($reader.NodeType -eq [Xml.XmlNodeType]::Element -and $reader.Depth -eq 1 -and $reader.Name -ceq 'Item' -and $reader.NamespaceURI -eq '') {
                $token=Get-RegistryTokenStart $reader $text $lines
                if($firstItem -lt 0) {$firstItem=$token}
                if([string]::Equals($reader.GetAttribute('HRID'),$Hrid,[StringComparison]::OrdinalIgnoreCase)) {
                    $entryCount++;$start=$token
                    if($reader.IsEmptyElement) {$end=Get-XmlTagEnd $text $start}
                }
            }
            if($start -ge 0 -and $end -lt 0 -and $reader.NodeType -eq [Xml.XmlNodeType]::EndElement -and $reader.Depth -eq 1 -and $reader.Name -ceq 'Item') {$end=Get-XmlTagEnd $text (Get-RegistryTokenStart $reader $text $lines)}
        }
    } finally {$reader.Dispose();$inputText.Dispose()}
    if($entryCount -gt 1) {throw 'Duplicate extension entries. The registry was not changed.'}
    $newline=[regex]::Match($text,"`r`n|`n|`r").Value
    $multiline=$newline.Length -gt 0
    if(-not $multiline) {$newline="`r`n"}
    $indent='  '
    if($firstItem -ge 0) {
        $lineStart=0;foreach($offset in $lines) {if($offset -gt $firstItem) {break};$lineStart=$offset}
        $leading=$text.Substring($lineStart,$firstItem-$lineStart)
        if($leading -match '^[\t ]+$') {$indent=$leading}
    }
    $fragmentStream=[IO.MemoryStream]::new()
    $writerSettings=[Xml.XmlWriterSettings]::new();$writerSettings.Encoding=$encoding;$writerSettings.OmitXmlDeclaration=$true
    $writerSettings.ConformanceLevel=[Xml.ConformanceLevel]::Fragment;$writerSettings.Indent=$multiline
    $writerSettings.IndentChars=$indent;$writerSettings.NewLineChars=$newline;$writerSettings.NewLineHandling=[Xml.NewLineHandling]::Entitize
    $writer=[Xml.XmlWriter]::Create($fragmentStream,$writerSettings)
    try {$Entry.WriteTo($writer);$writer.Flush();$fragmentBytes=$fragmentStream.ToArray()} finally {$writer.Dispose();$fragmentStream.Dispose()}
    $preamble=$encoding.GetPreamble();$fragmentOffset=0
    if($preamble.Length -gt 0 -and $fragmentBytes.Length -ge $preamble.Length -and [BitConverter]::ToString($fragmentBytes,0,$preamble.Length) -ceq [BitConverter]::ToString($preamble)) {$fragmentOffset=$preamble.Length}
    $fragment=$encoding.GetString($fragmentBytes,$fragmentOffset,$fragmentBytes.Length-$fragmentOffset)
    if($entryCount -eq 1) {
        if($start -lt 0 -or $end -le $start) {throw 'Incomplete extension span. The registry was not changed.'}
    } elseif($rootEmpty -ge 0) {
        $start=$rootEmpty;$end=$start+2;$fragment='>'+$fragment+'</Extensions>'
    } else {
        if($rootClose -lt 0) {throw 'Closing Extensions element not found. The registry was not changed.'}
        $start=$rootClose;$end=$rootClose
        $lineStart=0;foreach($offset in $lines) {if($offset -gt $rootClose) {break};$lineStart=$offset}
        if($multiline -and $text.Substring($lineStart,$rootClose-$lineStart) -match '^[\t ]*$' -and $original.DocumentElement.GetAttribute('space','http://www.w3.org/XML/1998/namespace') -ne 'preserve') {
            $start=$lineStart;$end=$lineStart
            $fragment=$indent+$fragment.Replace($newline,$newline+$indent)+$newline
        }
    }
    $byteStart=$bom+$encoding.GetByteCount($text.Substring(0,$start))
    $removed=$encoding.GetByteCount($text.Substring($start,$end-$start))
    $insert=$encoding.GetBytes($fragment)
    $result=[IO.MemoryStream]::new()
    try {$result.Write($bytes,0,$byteStart);$result.Write($insert,0,$insert.Length);$result.Write($bytes,$byteStart+$removed,$bytes.Length-$byteStart-$removed);$patched=$result.ToArray()} finally {$result.Dispose()}
    $candidate=Read-RegistryXmlBytes $patched
    Assert-OtherRegistrationsUnchanged (Get-OtherRegistrationSnapshot $original $Hrid) $candidate $Hrid
    $own=@($candidate.DocumentElement.ChildNodes | Where-Object {$_ -is [Xml.XmlElement] -and $_.Name -ceq 'Item' -and $_.NamespaceURI -eq '' -and [string]::Equals($_.GetAttribute('HRID'),$Hrid,[StringComparison]::OrdinalIgnoreCase)})
    if($own.Count -ne 1 -or $own[0].GetAttribute('Guid') -cne $Entry.GetAttribute('Guid')) {throw 'Unexpected extension identity after byte patch.'}
    return [pscustomobject]@{Bytes=$patched;Start=$byteStart;Removed=$removed;Inserted=$insert.Length;SourceLength=$bytes.Length;OriginalSHA256=(Get-RegistryByteHash $bytes);ResultSHA256=(Get-RegistryByteHash $patched);FragmentSHA256=(Get-RegistryByteHash $insert);Hrid=$Hrid}
}
function Assert-RegistryBytePatch([byte[]]$Original,[byte[]]$Candidate,$Patch) {
    if($Original.Length -ne $Patch.SourceLength -or (Get-RegistryByteHash $Original) -cne $Patch.OriginalSHA256 -or $Candidate.Length -ne ($Original.Length-$Patch.Removed+$Patch.Inserted) -or (Get-RegistryByteHash $Candidate) -cne $Patch.ResultSHA256) {throw 'Registry byte patch integrity check failed.'}
    $suffix=$Original.Length-$Patch.Start-$Patch.Removed
    if((Get-RegistryByteHash $Original 0 $Patch.Start) -cne (Get-RegistryByteHash $Candidate 0 $Patch.Start) -or
        (Get-RegistryByteHash $Original ($Patch.Start+$Patch.Removed) $suffix) -cne (Get-RegistryByteHash $Candidate ($Patch.Start+$Patch.Inserted) $suffix) -or
        (Get-RegistryByteHash $Candidate $Patch.Start $Patch.Inserted) -cne $Patch.FragmentSHA256) {throw 'Bytes outside the extension entry would change. Installation stopped.'}
}
function Get-OtherRegistrationSnapshot([Xml.XmlDocument]$Document,[string]$Hrid) {
    $copy=$Document.DocumentElement.CloneNode($true)
    foreach($node in @($copy.ChildNodes)) {
        if($node -is [Xml.XmlElement] -and $node.Name -ceq 'Item' -and $node.NamespaceURI -eq '' -and
            [string]::Equals($node.GetAttribute('HRID'),$Hrid,[StringComparison]::OrdinalIgnoreCase)) {
            [void]$copy.RemoveChild($node)
        }
        elseif(($node.NodeType -eq [Xml.XmlNodeType]::Whitespace -or $node.NodeType -eq [Xml.XmlNodeType]::SignificantWhitespace) -and [string]::IsNullOrWhiteSpace($node.Value)) {[void]$copy.RemoveChild($node)}
    }
    if($copy.ChildNodes.Count -eq 0) {$copy.IsEmpty=$true}
    # Secondary structural check. Exact formatting/BOM preservation is enforced by Assert-RegistryBytePatch.
    return $copy.OuterXml
}
function Assert-OtherRegistrationsUnchanged([string]$ExpectedSnapshot,[Xml.XmlDocument]$Candidate,[string]$Hrid) {
    $actual=Get-OtherRegistrationSnapshot $Candidate $Hrid
    if(-not [string]::Equals($ExpectedSnapshot,$actual,[StringComparison]::Ordinal)) {
        throw 'Another extension registration would change. Installation stopped.'
    }
}
