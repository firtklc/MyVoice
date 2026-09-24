param([string]$Out, [int]$Seconds = 90, [string]$Title = 'MyVoice Spike Target')
$Log = "$Out.log"
function L($m) { Add-Content -Path $Log -Value ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $m) }
L "start out=$Out title=$Title"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$f = New-Object Windows.Forms.Form -Property @{ Text = $Title; Width = 700; Height = 300; TopMost = $true }
$t = New-Object Windows.Forms.TextBox -Property @{ Multiline = $true; Dock = 'Fill'; Font = (New-Object Drawing.Font('Segoe UI', 12)) }
$f.Controls.Add($t)
$t.Add_TextChanged({ L "textchanged len=$($t.Text.Length)"; [IO.File]::WriteAllText($Out, $t.Text) })
$script:ticks = 0
$mirror = New-Object Windows.Forms.Timer -Property @{ Interval = 200 }
$mirror.Add_Tick({ $script:ticks++; if ($script:ticks % 10 -eq 1) { L "tick $script:ticks" } })
$mirror.Start()
$close = New-Object Windows.Forms.Timer -Property @{ Interval = $Seconds * 1000 }
$close.Add_Tick({ L "closing"; $f.Close() })
$close.Start()
$f.Add_Shown({ L "shown"; $t.Focus() })
L "showdialog"
[void]$f.ShowDialog()
L "exit"
