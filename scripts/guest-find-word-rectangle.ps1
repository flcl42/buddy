[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Word
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = Get-Process -Name Buddy -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Sort-Object StartTime -Descending |
    Select-Object -First 1
if ($null -eq $process) {
    throw 'Buddy has no interactive window.'
}

$root = [System.Windows.Automation.AutomationElement]::FromHandle(
    $process.MainWindowHandle)
$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ClassNameProperty,
    'RichTextBlock')
$elements = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    $condition)

$matches = @()
foreach ($element in $elements) {
    $pattern = $null
    if (-not $element.TryGetCurrentPattern(
            [System.Windows.Automation.TextPattern]::Pattern,
            [ref] $pattern)) {
        continue
    }

    $textPattern = [System.Windows.Automation.TextPattern] $pattern
    $text = $textPattern.DocumentRange.GetText(-1)
    if ($text.IndexOf(
            $Word,
            [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        continue
    }

    $range = $textPattern.DocumentRange.FindText($Word, $false, $true)
    if ($null -eq $range) {
        continue
    }

    $rectangles = @($range.GetBoundingRectangles())
    for ($index = 0; $index -lt $rectangles.Count; $index += 4) {
        if ($index + 3 -ge $rectangles.Count) {
            break
        }
        $left = [double] $rectangles[$index]
        $top = [double] $rectangles[$index + 1]
        $width = [double] $rectangles[$index + 2]
        $height = [double] $rectangles[$index + 3]
        if ($width -le 0 -or $height -le 0) {
            continue
        }
        $matches += [pscustomobject]@{
            Text = $text.Trim()
            Left = $left
            Top = $top
            Width = $width
            Height = $height
            CenterX = $left + ($width / 2)
            CenterY = $top + ($height / 2)
        }
    }
}

if ($matches.Count -eq 0) {
    throw "The word '$Word' was not found in a visible native text range."
}

$matches | ConvertTo-Json -Depth 4
