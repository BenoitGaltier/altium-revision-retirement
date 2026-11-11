# Read XML bytes through XmlReader so the declaration/BOM, not the PowerShell locale, selects the encoding.
function Read-ExtensionRegistryXml([string]$Path) {
    $settings=New-Object System.Xml.XmlReaderSettings
    $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver=$null
    $settings.IgnoreWhitespace=$false
    $reader=[Xml.XmlReader]::Create($Path,$settings)
    try {
        $document=New-Object System.Xml.XmlDocument
        $document.PreserveWhitespace=$true
        $document.XmlResolver=$null
        $document.Load($reader)
        return ,$document
    } finally {$reader.Dispose()}
}
function Write-ExtensionRegistryXml([Xml.XmlDocument]$Document,[string]$Path) {
    $settings=New-Object System.Xml.XmlWriterSettings
    $settings.Encoding=New-Object System.Text.UTF8Encoding($true)
    $declaration=@($Document.ChildNodes | Where-Object {$_ -is [Xml.XmlDeclaration]})
    if($declaration.Count -gt 0 -and $declaration[0].Encoding) {
        $settings.Encoding=[Text.Encoding]::GetEncoding($declaration[0].Encoding)
    }
    $settings.Indent=$false
    # Preserve CR, LF and TAB values when XML normalizes text and attributes on the next read.
    $settings.NewLineHandling=[Xml.NewLineHandling]::Entitize
    $writer=[Xml.XmlWriter]::Create($Path,$settings)
    try {$Document.Save($writer)} finally {$writer.Dispose()}
}
function Get-OtherRegistrationSnapshot([Xml.XmlDocument]$Document,[string]$Hrid) {
    $copy=$Document.DocumentElement.CloneNode($true)
    foreach($node in @($copy.ChildNodes)) {
        if($node -is [Xml.XmlElement] -and $node.Name -ceq 'Item' -and
            [string]::Equals($node.GetAttribute('HRID'),$Hrid,[StringComparison]::OrdinalIgnoreCase)) {
            [void]$copy.RemoveChild($node)
        }
    }
    # Retain every other node, attribute, comment and whitespace character, including case.
    return $copy.OuterXml
}
function Assert-OtherRegistrationsUnchanged([string]$ExpectedSnapshot,[Xml.XmlDocument]$Candidate,[string]$Hrid) {
    $actual=Get-OtherRegistrationSnapshot $Candidate $Hrid
    if(-not [string]::Equals($ExpectedSnapshot,$actual,[StringComparison]::Ordinal)) {
        throw 'Another extension registration would change. Installation stopped.'
    }
}
