#Requires -Version 5.1
param(
    [Parameter(Mandatory)][int]   $ChiakiPid,
    [Parameter(Mandatory)][string]$ParentHwnd,
    [int]$X = 0,
    [int]$Y = 0,
    [int]$W = 800,
    [int]$H = 600
)

# Load Win32 P/Invoke helpers (guard against re-adding in the same session)
if (-not ([System.Management.Automation.PSTypeName]'EmbedHelper').Type) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class EmbedHelper {
    public const int  GWL_STYLE      = -16;
    public const uint WS_CHILD       = 0x40000000u;
    public const uint WS_VISIBLE     = 0x10000000u;
    public const uint WS_CAPTION     = 0x00C00000u;
    public const uint WS_THICKFRAME  = 0x00040000u;
    public const uint SWP_NOZORDER   = 0x0004u;
    public const uint SWP_FRAMECHANGED = 0x0020u;
    public const uint SWP_SHOWWINDOW = 0x0040u;

    [DllImport("user32.dll")]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll")]
    public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                            int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@
}

# ─── Wait for Chiaki main window ──────────────────────────────────────────────
$chiakiHwnd = [IntPtr]::Zero
$deadline   = [DateTime]::UtcNow.AddSeconds(10)

while ([DateTime]::UtcNow -lt $deadline) {
    try {
        $proc = Get-Process -Id $ChiakiPid -ErrorAction Stop
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) {
            $chiakiHwnd = $proc.MainWindowHandle
            break
        }
    } catch {
        # Process already gone
        break
    }
    Start-Sleep -Milliseconds 100
}

if ($chiakiHwnd -eq [IntPtr]::Zero) {
    Write-Output "error: could not find chiaki window for PID $ChiakiPid"
    exit 1
}

# ─── Reparent & embed ─────────────────────────────────────────────────────────
$parentHandle = [IntPtr][long]$ParentHwnd

# Strip caption/border, add WS_CHILD so it behaves as an embedded child
$style    = [EmbedHelper]::GetWindowLong($chiakiHwnd, [EmbedHelper]::GWL_STYLE)
$newStyle = ($style -band (-bnot ([uint32]([EmbedHelper]::WS_CAPTION -bor [EmbedHelper]::WS_THICKFRAME)))) -bor [EmbedHelper]::WS_CHILD
[EmbedHelper]::SetWindowLong($chiakiHwnd, [EmbedHelper]::GWL_STYLE, [uint32]$newStyle) | Out-Null

[EmbedHelper]::SetParent($chiakiHwnd, $parentHandle) | Out-Null

[EmbedHelper]::SetWindowPos(
    $chiakiHwnd, [IntPtr]::Zero,
    $X, $Y, $W, $H,
    [EmbedHelper]::SWP_NOZORDER -bor [EmbedHelper]::SWP_FRAMECHANGED -bor [EmbedHelper]::SWP_SHOWWINDOW
) | Out-Null

[EmbedHelper]::ShowWindow($chiakiHwnd, 1) | Out-Null

Write-Output 'ready'
[Console]::Out.Flush()

# ─── Stdin command loop ────────────────────────────────────────────────────────
while ($true) {
    $line = [Console]::In.ReadLine()
    if ($null -eq $line -or $line -eq 'exit') { break }

    if ($line -match '^bounds\s+(\d+)\s+(\d+)\s+(\d+)\s+(\d+)$') {
        [EmbedHelper]::SetWindowPos(
            $chiakiHwnd, [IntPtr]::Zero,
            [int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4],
            [EmbedHelper]::SWP_NOZORDER -bor [EmbedHelper]::SWP_SHOWWINDOW
        ) | Out-Null
    }
}
