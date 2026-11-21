<#
.SYNOPSIS
    Schrijft het OPNsense vga-image (.img) direct naar een USB-stick (zoals dd of Rufus DD-modus).

.DESCRIPTION
    - Start zichzelf als administrator.
    - Zoekt het .img in Downloads\OPNsense. Is het er niet, dan draait het
      Download-OPNsense.ps1 -Type vga (moet in dezelfde map staan als dit script).
    - Laat alleen USB-schijven zien, nooit je systeemschijf.
    - Vraagt om bevestiging voordat de stick gewist wordt.

.EXAMPLE
    .\Write-OPNsenseUSB.ps1
    .\Write-OPNsenseUSB.ps1 -ImagePath D:\ISO\OPNsense-26.7-vga-amd64.img
#>
[CmdletBinding()]
param(
    [string]$ImagePath = "",
    [string]$ImageDir  = (Join-Path $env:USERPROFILE "Downloads\OPNsense")
)

$ErrorActionPreference = "Stop"

# 1. Als administrator draaien
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Opnieuw starten als administrator..." -ForegroundColor Yellow
    $a = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -ImageDir `"$ImageDir`""
    if ($ImagePath) { $a += " -ImagePath `"$ImagePath`"" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $a
    exit
}

try {
    # 2. Image zoeken (of downloaden)
    function Find-Image {
        Get-ChildItem -Path $ImageDir -Filter "OPNsense-*-vga-amd64.img" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
    }

    if ($ImagePath) {
        if (-not (Test-Path $ImagePath)) { throw "Image niet gevonden: $ImagePath" }
        $img = Get-Item $ImagePath
    } else {
        $img = Find-Image
        if (-not $img) {
            $dl = Join-Path $PSScriptRoot "Download-OPNsense.ps1"
            if (-not (Test-Path $dl)) { throw "Geen .img gevonden in $ImageDir en Download-OPNsense.ps1 staat niet naast dit script." }
            Write-Host "Geen image gevonden, ik download eerst het vga-image..." -ForegroundColor Cyan
            & $dl -Type vga -Destination $ImageDir
            $img = Find-Image
            if (-not $img) { throw "Image downloaden/uitpakken is mislukt." }
        }
    }
    $imgGB = [math]::Round($img.Length / 1GB, 2)
    Write-Host "`nImage: $($img.FullName) ($imgGB GB)" -ForegroundColor Green

    # 3. USB-schijf kiezen (nooit systeem- of bootschijf)
    $disks = @(Get-Disk | Where-Object { $_.BusType -eq "USB" -and -not $_.IsSystem -and -not $_.IsBoot })
    if ($disks.Count -eq 0) { throw "Geen USB-stick gevonden. Steek de stick erin en probeer opnieuw." }

    Write-Host "`nGevonden USB-schijven:" -ForegroundColor Cyan
    foreach ($d in $disks) {
        $labels = @(Get-Partition -DiskNumber $d.Number -ErrorAction SilentlyContinue |
            Get-Volume -ErrorAction SilentlyContinue | ForEach-Object { $_.FileSystemLabel } |
            Where-Object { $_ }) -join ", "
        if (-not $labels) { $labels = "-" }
        "  Schijf {0}: {1}, {2} GB, labels: {3}" -f $d.Number, $d.FriendlyName, [math]::Round($d.Size / 1GB, 1), $labels
    }
    Write-Host "`nTip: een stick met label 'Ventoy' of 'VTOYEFI' is je Ventoy-stick. Kies die niet als je hem wilt houden." -ForegroundColor Yellow

    $num = Read-Host "`nWelk schijfnummer wil je overschrijven?"
    $target = $disks | Where-Object { "$($_.Number)" -eq $num.Trim() }
    if (-not $target) { throw "Ongeldige keuze: '$num'." }
    if ($target.Size -lt $img.Length) { throw "De stick is kleiner dan het image." }

    Write-Host "`nLET OP: ALLES op schijf $($target.Number) ($($target.FriendlyName), $([math]::Round($target.Size / 1GB, 1)) GB) wordt gewist." -ForegroundColor Red
    $ok = Read-Host "Typ JA om door te gaan"
    if ($ok -cne "JA") { Write-Host "Afgebroken."; return }

    # 4. Stick leegmaken zodat Windows de volumes loslaat
    Write-Host "`nStick leegmaken..." -ForegroundColor Cyan
    Clear-Disk -Number $target.Number -RemoveData -RemoveOEM -Confirm:$false

    # 5. Raw schrijven naar \\.\PhysicalDriveN
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class RawDisk {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);
}
"@ -ErrorAction SilentlyContinue

    $handle = [RawDisk]::CreateFile("\\.\PhysicalDrive$($target.Number)", 0x40000000, 3, [IntPtr]::Zero, 3, 0, [IntPtr]::Zero)
    if ($handle.IsInvalid) {
        $err = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
        throw "Kan schijf niet openen om te schrijven (Win32-fout $err). Sluit Verkenner-vensters van de stick en probeer opnieuw."
    }

    $dst = New-Object IO.FileStream($handle, [IO.FileAccess]::Write, 1)
    $src = [IO.File]::OpenRead($img.FullName)
    try {
        $buf   = New-Object byte[] (4MB)
        $total = $img.Length
        $done  = 0L
        Write-Host "Schrijven... even geduld." -ForegroundColor Cyan
        while ($true) {
            $filled = 0
            while ($filled -lt $buf.Length) {
                $r = $src.Read($buf, $filled, $buf.Length - $filled)
                if ($r -le 0) { break }
                $filled += $r
            }
            if ($filled -eq 0) { break }
            # Raw schrijven moet in veelvouden van 512 bytes
            $w = [int]([math]::Ceiling($filled / 512.0) * 512)
            if ($w -gt $filled) { [Array]::Clear($buf, $filled, $w - $filled) }
            $dst.Write($buf, 0, $w)
            $done += $filled
            Write-Progress -Activity "OPNsense naar USB schrijven" -Status ("{0:N0} / {1:N0} MB" -f ($done / 1MB), ($total / 1MB)) -PercentComplete ([int](100 * $done / $total))
        }
        $dst.Flush()
    } finally {
        $src.Dispose()
        $dst.Dispose()
        Write-Progress -Activity "OPNsense naar USB schrijven" -Completed
    }

    Update-HostStorageCache
    Write-Host "`nKlaar. De stick is beschreven." -ForegroundColor Green
    Write-Host "Windows kan zeggen dat de stick niet leesbaar is of geformatteerd moet worden: kies Annuleren, dat is normaal (FreeBSD-bestandssysteem)."
    Write-Host "Verwijder de stick veilig, steek hem in de HP mini en boot ervan (F9)."
    Write-Host "Inloggen in de installer: installer / opnsense"
}
catch {
    Write-Host "`nFout: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Read-Host "`nDruk op Enter om te sluiten"
}