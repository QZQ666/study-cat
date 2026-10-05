param([switch]$SelfTest, [string]$PreviewPath, [switch]$ExportIcon)
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
    $references = @([System.Uri].Assembly.Location, [System.Linq.Enumerable].Assembly.Location, [System.Xml.XmlDocument].Assembly.Location, [System.Xaml.XamlReader].Assembly.Location, [System.Windows.Threading.Dispatcher].Assembly.Location, [System.Windows.Media.Brush].Assembly.Location, [System.Windows.Window].Assembly.Location)
    Add-Type -TypeDefinition ([System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'StudyCat.cs'))) -ReferencedAssemblies $references
    [StudyCat.CalicoAssets]::AssetDirectory = $PSScriptRoot
    if ($ExportIcon) { [StudyCat.CatBrand]::ExportIcon((Join-Path $PSScriptRoot 'cat.ico')); exit 0 }
    if ($SelfTest) { [StudyCat.Checks]::Run(); exit 0 }
    $mutexName = 'Local\StudyCatPersonalDesktop'
    if ($PreviewPath) { $mutexName += 'Preview' }
    $mutex = New-Object System.Threading.Mutex($false, $mutexName)
    if (-not $mutex.WaitOne(0)) { [System.Windows.MessageBox]::Show('小猫陪学已经打开了，请从任务栏找到它。','小猫陪学') | Out-Null; exit 0 }
    $dataFolder = Join-Path $PSScriptRoot 'data'
    if ($PreviewPath) { $dataFolder = Join-Path $PSScriptRoot '.preview-data' }
    $window = New-Object StudyCat.AppWindow($dataFolder)
    if ($PreviewPath) {
        $window.Left = -10000; $window.Top = -10000; $window.WindowStartupLocation = 'Manual'; $window.ShowInTaskbar = $false
        $window.Show()
        $window.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
        if (-not $window.NativeReady) { throw 'Native power/session notifications did not register.' }
        $grid = $window.Content
        if (-not $window.Icon -or $window.WindowStyle -ne 'None') { throw 'Branded icon or title bar was not applied.' }
        $grid.FindName('Minimize').RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if ($window.WindowState -ne 'Minimized') { throw 'Custom minimize button failed.' }
        $window.WindowState = 'Normal'
        $grid.FindName('Maximize').RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if ($window.WindowState -ne 'Maximized') { throw 'Custom maximize button failed.' }
        $grid.FindName('Maximize').RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if ($window.WindowState -ne 'Normal') { throw 'Custom restore button failed.' }
        $startButton = $grid.FindName('Start')
        $startButton.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if (-not $window.Counter.Running) { throw 'Start button failed.' }
        $cat = $window.Companion
        if (-not $cat.IsVisible -or -not $cat.Topmost -or $cat.Owner) { throw 'Independent floating cat did not appear.' }
        $sessionBeforePetChange = $window.Counter.SessionSeconds
        $sleepBeforePetChange = $cat.IsSleeping
        $grid.FindName('PetRagdoll').IsChecked = $true
        if ($window.Counter.Data.PetId -ne 'ragdoll' -or $cat.PetId -ne 'ragdoll') { throw 'Pet selection did not update both windows.' }
        if ($window.Counter.SessionSeconds -ne $sessionBeforePetChange -or -not $window.Counter.Running) { throw 'Changing pets altered session timing.' }
        if ($cat.IsSleeping -ne $sleepBeforePetChange) { throw 'Changing pets altered focus animation state.' }
        $savedPet = ([xml](Get-Content -LiteralPath (Join-Path $dataFolder 'study-data.xml') -Raw)).Journal.PetId
        if ($savedPet -ne 'ragdoll') { throw 'Pet choice was not persisted.' }
        $grid.FindName('PetCalico').IsChecked = $true
        if ($cat.PetId -ne 'calico' -or $window.Counter.SessionSeconds -ne $sessionBeforePetChange) { throw 'Switching back changed timing or failed.' }
        $window.WindowState = 'Minimized'
        $window.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::ApplicationIdle)
        if (-not $cat.IsVisible) { throw 'Minimizing dashboard hides cat.' }
        $window.WindowState = 'Normal'
        $cat.MoveTo([System.Windows.SystemParameters]::WorkArea.Left + 24, [System.Windows.SystemParameters]::WorkArea.Top + 24)
        if ($window.Counter.Data.CatLeft -ne $cat.Left -or $window.Counter.Data.CatTop -ne $cat.Top) { throw 'Cat position was not saved.' }
        $cat.Left = -10000; $cat.Top = -10000
        $grid.FindName('VideoMode').IsChecked = $true
        if (-not $window.Counter.Video) { throw 'Video selector failed.' }
        $startButton.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if ($window.Counter.Running) { throw 'Pause button failed.' }
        if (-not $cat.IsVisible) { throw 'Pausing should preserve cat.' }
        if ($cat.IsSleeping) { throw 'Paused cat should wake up.' }
        $previousRolls = $cat.RollCount
        $cat.RollOver()
        if ($cat.RollCount -ne $previousRolls) { throw 'Awake cat should not roll in sleep.' }
        $window.Counter.SessionSeconds = 125
        $window.Tick()
        if ($cat.DisplayedTime -ne '00:02:05') { throw 'Cat session time did not synchronize.' }
        $cat.UpdateDisplay(125, $false, $false, $true)
        if (-not $cat.IsSleeping) { throw 'Focused cat should sleep.' }
        $cat.RollOver()
        if ($cat.RollCount -ne ($previousRolls + 1)) { throw 'Sleeping rollover did not start.' }
        $cat.AdvanceAnimationAt([DateTime]::UtcNow.AddSeconds(1.2))
        if ($cat.PoseIndex -ne 6) { throw 'Rollover must pass through belly-up pose.' }
        $cat.AdvanceAnimationAt([DateTime]::UtcNow.AddSeconds(4))
        if ($cat.PoseIndex -ne 7) { throw 'Rollover must settle on other side.' }
        for ($pose = 0; $pose -lt 12; $pose++) {
            if ([StudyCat.CalicoAssets]::Frame($pose).PixelWidth -lt 30) { throw "Empty cat pose $pose" }
            $cat.PreviewPose($pose)
            $cat.Snapshot((Join-Path (Split-Path $PreviewPath) ('calico-pose-' + $pose + '-preview.png')))
        }
        $cat.PreviewPose(4)
        $cat.Snapshot((Join-Path (Split-Path $PreviewPath) 'floating-cat-preview.png'))
        $pausedSeconds = $window.Counter.SessionSeconds
        $grid.FindName('PetRagdoll').IsChecked = $true
        if ($window.Counter.Running -or $window.Counter.SessionSeconds -ne $pausedSeconds) { throw 'Pet switching resumed paused session.' }
        $cat.UpdateDisplay(125, $false, $false, $true)
        $cat.RollOver()
        $cat.AdvanceAnimationAt([DateTime]::UtcNow.AddSeconds(1.2))
        if ($cat.PoseIndex -ne 6) { throw 'Ragdoll belly-up pose did not appear.' }
        $cat.AdvanceAnimationAt([DateTime]::UtcNow.AddSeconds(4.2))
        if ($cat.PoseIndex -ne 7) { throw 'Ragdoll rollover did not settle.' }
        for ($pose=0; $pose -lt 12; $pose++) {
            if ([StudyCat.CalicoAssets]::Frame('ragdoll',$pose).PixelWidth -lt 30) { throw "Empty ragdoll pose $pose" }
            $cat.PreviewPose($pose)
            $cat.Snapshot((Join-Path (Split-Path $PreviewPath) ('ragdoll-pose-' + $pose + '-preview.png')))
        }
        $cat.PreviewPose(4)
        $cat.Snapshot((Join-Path (Split-Path $PreviewPath) 'ragdoll-cat-preview.png'))
        $grid.FindName('ReadMode').IsChecked = $true
        $grid.FindName('Idle60').IsChecked = $true
        if ($window.Counter.Data.IdleSeconds -ne 60) { throw 'Idle setting failed.' }
        $grid.FindName('Idle90').IsChecked = $true
        $grid.FindName('Finish').RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if ($window.Counter.SessionSeconds -ne 0) { throw 'Finish button failed.' }
        if ($cat.IsVisible) { throw 'Finishing should hide cat.' }
        $startButton.RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if (-not $cat.IsVisible -or $window.Companion -ne $cat) { throw 'Next session should reuse cat.' }
        $grid.FindName('Finish').RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        $window.Snapshot($PreviewPath)
        $grid.FindName('CloseWindow').RaiseEvent((New-Object System.Windows.RoutedEventArgs([System.Windows.Controls.Button]::ClickEvent)))
        if ($cat.IsVisible) { throw 'Closing dashboard left cat open.' }
        $reopened = New-Object StudyCat.AppWindow($dataFolder)
        if ($reopened.Counter.Data.PetId -ne 'ragdoll') { throw 'Pet selection did not survive restart.' }
        $reopened.Close()
        'PASS: WPF controls, native hooks, pet switching and persistence, both pet animations, floating cat lifecycle, time synchronization, minimized dashboard, position persistence and snapshots'
    } else { $window.ShowDialog() | Out-Null }
    $mutex.ReleaseMutex(); $mutex.Dispose()
} catch {
    $_ | Out-String | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'startup-error.log') -Encoding UTF8
    if ($SelfTest -or $PreviewPath) { throw }
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('启动失败。请查看软件目录里的 startup-error.log。' + [Environment]::NewLine + $_.Exception.Message,'小猫陪学') | Out-Null
    exit 1
}
