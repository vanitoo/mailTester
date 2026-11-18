[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string] $CliPath
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path -LiteralPath $CliPath).Path
if ((Split-Path $exe -Leaf) -ine 'MailTester.Cli.exe') {
    throw 'Point CliPath at published MailTester.Cli.exe.'
}
$name = 'MailTester Monitor'
if (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {
    throw "Task exists: $name. Review or remove it before registering a replacement."
}
$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute $exe -Argument 'monitor run-due --max 20 --format json' -WorkingDirectory (Split-Path $exe -Parent)
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 90) -StartWhenAvailable
if ($PSCmdlet.ShouldProcess($name, "Register interactive task as $identity")) {
    Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'MailTester SQLite monitor queue; requires current user session.' | Out-Null
    Write-Output "Registered $name under $identity. Only enabled profiles are scheduled."
}
