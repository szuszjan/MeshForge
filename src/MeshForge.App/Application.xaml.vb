Imports System.Diagnostics
Imports System.Threading.Tasks

Class Application

    ''' <summary>Minimalny czas, przez jaki ekran startowy zostaje widoczny (żeby nie "mignął" przy szybkim starcie).</summary>
    Private Const MinSplashMilliseconds = 1100

    Private Async Sub Application_Startup(sender As Object, e As StartupEventArgs) Handles Me.Startup
        AppSettings.LoadAndApply()

        Dim splash As New SplashWindow()
        splash.Show()

        Dim sw = Stopwatch.StartNew()
        Dim main As New MainWindow()

        Dim remaining = MinSplashMilliseconds - CInt(sw.ElapsedMilliseconds)
        If remaining > 0 Then Await Task.Delay(remaining)

        Me.MainWindow = main
        main.Show()
        splash.Close()
    End Sub

End Class
