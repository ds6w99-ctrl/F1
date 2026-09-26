param([int]$TargetPid,[int]$Seconds=90)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$target=Get-Process -Id $TargetPid
$clock=[Diagnostics.Stopwatch]::StartNew()
$count=0
while($clock.Elapsed.TotalSeconds -lt $Seconds){
 $root=[Windows.Automation.AutomationElement]::FromHandle($target.MainWindowHandle)
 $item=$root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'PageTitle'))
 if($item){$null=$item.Current.Name}
 $count++
 Start-Sleep -Milliseconds 100
}
[pscustomobject]@{Queries=$count;Seconds=$clock.Elapsed.TotalSeconds}|ConvertTo-Json
$target.Dispose()
# Process exit releases the entire UIA client; do not force GC in the target.
