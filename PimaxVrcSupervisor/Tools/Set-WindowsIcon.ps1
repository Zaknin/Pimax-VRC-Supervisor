param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$IconPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw "Executable not found: $ExecutablePath"
}

if (-not (Test-Path -LiteralPath $IconPath -PathType Leaf)) {
    throw "Icon not found: $IconPath"
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class IconResourceUpdater
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateResource(
        IntPtr hUpdate,
        IntPtr lpType,
        IntPtr lpName,
        ushort wLanguage,
        byte[] lpData,
        uint cbData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);
}
"@

function Throw-LastWin32Error([string]$Action) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "$Action failed. Win32 error: $errorCode"
}

$iconBytes = [IO.File]::ReadAllBytes($IconPath)
$reader = [IO.BinaryReader]::new([IO.MemoryStream]::new($iconBytes))
$iconImages = New-Object "System.Collections.Generic.List[byte[]]"
$groupStream = [IO.MemoryStream]::new()
$groupWriter = [IO.BinaryWriter]::new($groupStream)

try {
    $reserved = $reader.ReadUInt16()
    $type = $reader.ReadUInt16()
    $count = $reader.ReadUInt16()

    if ($reserved -ne 0 -or $type -ne 1 -or $count -le 0) {
        throw "Not a valid Windows .ico file: $IconPath"
    }

    $entries = @()
    for ($index = 0; $index -lt $count; $index++) {
        $entry = [ordered]@{
            Width = $reader.ReadByte()
            Height = $reader.ReadByte()
            ColorCount = $reader.ReadByte()
            Reserved = $reader.ReadByte()
            Planes = $reader.ReadUInt16()
            BitCount = $reader.ReadUInt16()
            BytesInRes = $reader.ReadUInt32()
            ImageOffset = $reader.ReadUInt32()
            ResourceId = [UInt16]($index + 1)
        }
        $entries += [pscustomobject]$entry
    }

    foreach ($entry in $entries) {
        $image = New-Object byte[] $entry.BytesInRes
        [Array]::Copy($iconBytes, [int]$entry.ImageOffset, $image, 0, [int]$entry.BytesInRes)
        $iconImages.Add($image)
    }

    $groupWriter.Write([UInt16]0)
    $groupWriter.Write([UInt16]1)
    $groupWriter.Write([UInt16]$count)
    foreach ($entry in $entries) {
        $groupWriter.Write([byte]$entry.Width)
        $groupWriter.Write([byte]$entry.Height)
        $groupWriter.Write([byte]$entry.ColorCount)
        $groupWriter.Write([byte]0)
        $groupWriter.Write([UInt16]$entry.Planes)
        $groupWriter.Write([UInt16]$entry.BitCount)
        $groupWriter.Write([UInt32]$entry.BytesInRes)
        $groupWriter.Write([UInt16]$entry.ResourceId)
    }
}
finally {
    $reader.Dispose()
    $groupWriter.Dispose()
}

$rtIcon = [IntPtr]3
$rtGroupIcon = [IntPtr]14
$mainIconGroup = [IntPtr]1
$languageNeutral = [UInt16]0
$handle = [IconResourceUpdater]::BeginUpdateResource((Resolve-Path -LiteralPath $ExecutablePath), $false)
if ($handle -eq [IntPtr]::Zero) {
    Throw-LastWin32Error "BeginUpdateResource"
}

$discard = $true
try {
    for ($index = 0; $index -lt $iconImages.Count; $index++) {
        $resourceId = [IntPtr]($index + 1)
        $image = $iconImages[$index]
        if (-not [IconResourceUpdater]::UpdateResource($handle, $rtIcon, $resourceId, $languageNeutral, $image, [UInt32]$image.Length)) {
            Throw-LastWin32Error "UpdateResource RT_ICON"
        }
    }

    $groupBytes = $groupStream.ToArray()
    if (-not [IconResourceUpdater]::UpdateResource($handle, $rtGroupIcon, $mainIconGroup, $languageNeutral, $groupBytes, [UInt32]$groupBytes.Length)) {
        Throw-LastWin32Error "UpdateResource RT_GROUP_ICON"
    }

    $discard = $false
}
finally {
    if (-not [IconResourceUpdater]::EndUpdateResource($handle, $discard)) {
        Throw-LastWin32Error "EndUpdateResource"
    }

    $groupStream.Dispose()
}
