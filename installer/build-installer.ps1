# File: installer/build-installer.ps1
#
# Builds the System Optimizer installer, end to end:
#
#   1. publishes the application (single file, self-contained, win-x64)
#   2. generates the two text files the wizard shows, from the repository's own
#      README.md and LICENSE
#   3. compiles installer/SystemOptimizer.iss with Inno Setup 6
#
# Run it from anywhere:  pwsh -File installer\build-installer.ps1
#
# The wizard's README page is GENERATED rather than written, for the same reason
# the Sanity Check guide is: a second copy of the project's own description would
# eventually describe a different product. If README.md gains a section, the
# wizard shows it. Only the two developer sections are dropped, by name, and the
# script fails if a section it expected to drop has been renamed - silence there
# would mean quietly showing build instructions to someone installing the app.
#
# Encoding note: text is read and written through [System.IO.File] with an
# explicit UTF-8 encoding, never Get-Content/Set-Content. That round trip has
# corrupted this repository's source twice, and README.md is full of the
# characters it destroys.

[CmdletBinding()]
param(
    # Skip dotnet publish and use whatever is already in src\bin\publish\win-x64.
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$InstallerDir = $PSScriptRoot
$RepoRoot     = Split-Path -Parent $InstallerDir
$BuildDir     = Join-Path $InstallerDir 'build'
$OutputDir    = Join-Path $InstallerDir 'output'
$PublishedExe = Join-Path $RepoRoot 'src\bin\publish\win-x64\SystemOptimizer.exe'
$Iscc         = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'

# Sections of README.md that are for people building the source, not for people
# installing the program. Dropped by heading text.
$DeveloperSections = @('Building', 'Layout')

function Read-Utf8([string]$Path) {
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-Utf8([string]$Path, [string]$Text) {
    # WITH a byte-order mark: Inno Setup reads LicenseFile and InfoBeforeFile as
    # the system code page unless a UTF-8 BOM tells it otherwise, and without one
    # every em dash and (C) in the README reaches the wizard as mojibake.
    [System.IO.File]::WriteAllText($Path, $Text, (New-Object System.Text.UTF8Encoding($true)))
}

# Strips inline Markdown from one already-joined block of text.
#
# This runs on a whole paragraph, never on a single source line. README.md is
# hard-wrapped, so its bold spans cross line boundaries - "**System Optimizer
# finds and collates candidate files. It does not decide that\nthey should stop
# existing - you do.**" is one span over two lines, and a per-line pass leaves the
# asterisks on screen at both ends. That is exactly what the first version of this
# script shipped into the wizard.
function Convert-Inline([string]$Text) {
    # A link becomes its text followed by its address. In a browser the address is
    # hidden behind the words; in a wizard's text box there is nothing to click, so
    # dropping it would leave "Download the setup program from Releases and run
    # it" with no way to reach Releases.
    $Text = [regex]::Replace($Text, '\[([^\]]+)\]\((https?://[^)]+)\)', '$1 ($2)')
    $Text = [regex]::Replace($Text, '\[([^\]]+)\]\([^)]+\)', '$1')
    $Text = $Text -replace '\*\*([^*]+)\*\*', '$1'
    $Text = $Text -replace '(?<!\w)\*([^*]+)\*(?!\w)', '$1'
    $Text = $Text -replace '`([^`]+)`', '$1'
    return $Text
}

# Re-flows a block to $Width, with every line after the first indented by $Hang.
function Format-Block([string]$Text, [int]$Hang = 0, [int]$Width = 76) {
    $indent = ' ' * $Hang
    $out = New-Object System.Collections.Generic.List[string]
    $current = ''
    foreach ($word in ($Text -split '\s+')) {
        if ($word -eq '') { continue }
        if ($current -eq '') { $current = $word; continue }
        if (($current + ' ' + $word).Length -gt $Width) {
            $out.Add($current)
            $current = $indent + $word
        }
        else { $current += ' ' + $word }
    }
    if ($current -ne '') { $out.Add($current) }
    return $out.ToArray()
}

function Convert-MarkdownToText([string]$Markdown) {
    $lines = $Markdown -split "`r?`n"
    $out = New-Object System.Collections.Generic.List[string]

    $skipping = $false
    $seenSections = New-Object System.Collections.Generic.List[string]
    $inFence = $false

    # The block being accumulated, and how far its continuation lines indent.
    $block = ''
    $blockHang = 0

    function Flush {
        if ($script:block.Trim() -ne '') {
            foreach ($w in (Format-Block (Convert-Inline $script:block) $script:blockHang)) {
                $script:out.Add($w)
            }
        }
        $script:block = ''
        $script:blockHang = 0
    }

    # Format-Block and Convert-Inline are called through script scope above, so the
    # accumulator has to live there too.
    $script:out = $out
    $script:block = ''
    $script:blockHang = 0

    foreach ($raw in $lines) {
        $line = $raw

        # A "## " heading decides whether the section that follows is shown at
        # all, and is tested FIRST - before the fence and before the skip - so
        # that a heading always ends the previous section. Guarded on $inFence
        # because a line of Markdown inside a code block is not a heading.
        if (-not $inFence -and $line -match '^##\s+(.+?)\s*$') {
            $title = $Matches[1] -replace '\*\*', '' -replace '`', ''
            $seenSections.Add($title)
            $skipping = $DeveloperSections -contains $title
        }

        # The fence state is tracked even inside a section being dropped,
        # otherwise the toggle desynchronises and the NEXT section is treated as
        # code. This ordering is the fix for a real bug: the build commands from
        # the "Building" section appeared verbatim in the wizard, because the
        # fence was handled before the skip was consulted.
        if ($line -match '^\s*```') {
            $inFence = -not $inFence
            if (-not $skipping) { Flush }
            continue
        }
        if ($skipping) { continue }
        if ($inFence) { $out.Add('    ' + $line); continue }

        if ($line.Trim() -eq '') { Flush; if ($out.Count -gt 0 -and $out[$out.Count - 1] -ne '') { $out.Add('') }; continue }
        if ($line.Trim() -eq '---') { Flush; continue }

        # Headings.
        if ($line -match '^#\s+(.+?)\s*$') {
            Flush
            $t = Convert-Inline $Matches[1]
            $out.Add($t); $out.Add('=' * $t.Length); continue
        }
        if ($line -match '^##\s+(.+?)\s*$') {
            Flush
            $t = Convert-Inline $Matches[1]
            $out.Add($t); $out.Add('-' * $t.Length); continue
        }
        if ($line -match '^###\s+(.+?)\s*$') {
            Flush
            $out.Add((Convert-Inline $Matches[1]) + ':'); continue
        }

        # Tables: drop the |---|---| rule, and read each remaining row as its
        # cells joined by a dash. Both tables in the README are two columns of
        # "thing" and "why", which reads correctly that way.
        if ($line -match '^\s*\|[\s:|-]+\|\s*$') { continue }
        if ($line -match '^\s*\|(.+)\|\s*$') {
            Flush
            $cells = ($Matches[1] -split '\s*\|\s*') | ForEach-Object { $_.Trim() }
            $script:block = ($cells | Where-Object { $_ -ne '' }) -join ' - '
            $script:blockHang = 4
            Flush
            continue
        }

        # A bullet ends the previous block and starts one of its own, so that two
        # adjacent bullets are not run together into a paragraph.
        if ($line -match '^\s*[-*]\s+(.+)$') {
            Flush
            $script:block = '- ' + $Matches[1]
            $script:blockHang = 2
            continue
        }

        # A block quote is prose; the marker goes, the text joins the block.
        $line = $line -replace '^\s*>\s?', ''

        if ($script:block -eq '') { $script:block = $line.Trim() }
        else { $script:block += ' ' + $line.Trim() }
    }
    Flush

    # If a dropped section were renamed, this script would silently start showing
    # build instructions in the installer. Refuse instead.
    foreach ($expected in $DeveloperSections) {
        if ($seenSections -notcontains $expected) {
            throw "README.md no longer has a '## $expected' section. build-installer.ps1 drops that section from the wizard's README page by name; either restore the heading or update `$DeveloperSections. Sections found: $($seenSections -join ', ')"
        }
    }

    # Collapse runs of blank lines left behind by the removals.
    $text = ($out -join "`r`n")
    while ($text -match "`r`n`r`n`r`n") { $text = $text -replace "`r`n`r`n`r`n", "`r`n`r`n" }
    return $text.Trim() + "`r`n"
}

# ---------------------------------------------------------------- 1. publish --

if (-not $SkipPublish) {
    Write-Host '==> Publishing (Release, self-contained, single file)...'
    & dotnet publish (Join-Path $RepoRoot 'src\SystemOptimizer.csproj') `
        -p:PublishProfile=win-x64 --nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path $PublishedExe)) {
    throw "Published executable not found at $PublishedExe. Run without -SkipPublish."
}

$exe = Get-Item $PublishedExe
Write-Host ("    {0}  {1:N1} MB  {2}" -f $exe.Name, ($exe.Length / 1MB), $exe.LastWriteTime)

# The .pdb sits beside the exe and is not listed in [Files]. Said out loud so that
# nobody adds a wildcard to [Files] and ships debug symbols by accident.
$pdb = Join-Path (Split-Path $PublishedExe) 'SystemOptimizer.pdb'
if (Test-Path $pdb) { Write-Host '    (SystemOptimizer.pdb is present and is deliberately not installed)' }

# ------------------------------------------------- 2. wizard text, generated --

Write-Host '==> Generating the wizard text from README.md and LICENSE...'
New-Item -ItemType Directory -Force -Path $BuildDir  | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$readme = Convert-MarkdownToText (Read-Utf8 (Join-Path $RepoRoot 'README.md'))
Write-Utf8 (Join-Path $BuildDir 'README.txt') $readme

# The licence is already plain text and is shown exactly as it is granted. No
# transformation at all - a licence the installer paraphrased would not be the
# licence the repository grants.
$licence = Read-Utf8 (Join-Path $RepoRoot 'LICENSE')
Write-Utf8 (Join-Path $BuildDir 'LICENSE.txt') ($licence -replace "`r?`n", "`r`n")

Write-Host ("    README.txt  {0} lines" -f ($readme -split "`r`n").Count)
Write-Host ("    LICENSE.txt {0} lines" -f ($licence -split "`r?`n").Count)

# ---------------------------------------------------------------- 3. compile --

if (-not (Test-Path $Iscc)) {
    throw "Inno Setup 6 compiler not found at $Iscc. Install Inno Setup 6, or edit `$Iscc in this script."
}

Write-Host '==> Compiling the installer...'
& $Iscc (Join-Path $InstallerDir 'SystemOptimizer.iss') /Q
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE." }

$setup = Get-ChildItem $OutputDir -Filter '*.exe' | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $setup) { throw "ISCC reported success but no installer appeared in $OutputDir." }

Write-Host ''
Write-Host ('    {0}' -f $setup.FullName)
Write-Host ('    {0:N1} MB' -f ($setup.Length / 1MB))
Write-Host ''
# This deliberately does NOT print a signtool command. One used to live here, and
# it was misleading: it pointed at a self-signed certificate, which cannot do the
# job whatever the command says.
Write-Host 'The installer is NOT code-signed, and Windows SmartScreen will warn on'
Write-Host 'first download. That is expected and the release notes say so.'
Write-Host ''
Write-Host 'Signing it usefully needs a CA-ISSUED certificate. A self-signed one cannot'
Write-Host 'help: no machine but the one that created it trusts a self-signed certificate,'
Write-Host 'so a build signed with it looks exactly like this one to every person who'
Write-Host 'downloads it. Reputation with SmartScreen also builds over time and'
Write-Host 'downloads, which an unsigned release still accrues.'
