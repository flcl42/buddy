#Requires -Assembly UIAutomationClient
#Requires -Assembly UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$all = $root.FindAll(
    [System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
$items = foreach ($element in $all) {
    try {
        $current = $element.Current
        $rect = $current.BoundingRectangle
        if ($rect.Bottom -ge 900 -or
            $current.Name -match 'hidden|Chitchat|Buddy|notification') {
            [pscustomobject]@{
                Name = $current.Name
                AutomationId = $current.AutomationId
                ClassName = $current.ClassName
                ControlType = $current.ControlType.ProgrammaticName
                Rect = '{0},{1},{2},{3}' -f
                    [Math]::Round($rect.Left),
                    [Math]::Round($rect.Top),
                    [Math]::Round($rect.Width),
                    [Math]::Round($rect.Height)
                IsOffscreen = $current.IsOffscreen
            }
        }
    }
    catch [System.Windows.Automation.ElementNotAvailableException] {
    }
}

$items | ConvertTo-Json -Depth 4
