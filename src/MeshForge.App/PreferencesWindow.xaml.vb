Imports MeshForge.Core

''' <summary>Okno preferencji: język i tryb ciemny, oba stosowane na żywo (bez przycisku "Zastosuj" - patrz AppSettings).</summary>
Class PreferencesWindow

    Private _initializing As Boolean = True

    Public Sub New()
        InitializeComponent()

        CmbLanguage.SelectedIndex = If(Loc.Current = AppLanguage.English, 1, 0)
        ChkDarkMode.IsChecked = AppSettings.IsDarkMode

        _initializing = False
    End Sub

    Private Sub CmbLanguage_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmbLanguage.SelectionChanged
        If _initializing Then Return
        AppSettings.ApplyLanguage(If(CmbLanguage.SelectedIndex = 1, AppLanguage.English, AppLanguage.Polish))
    End Sub

    Private Sub ChkDarkMode_Click(sender As Object, e As RoutedEventArgs) Handles ChkDarkMode.Click
        If _initializing Then Return
        Dim dark = ChkDarkMode.IsChecked = True
        AppSettings.ApplyTheme(dark)
        TryCast(Me.Owner, MainWindow)?.ApplyTitleBarTheme(dark)
    End Sub

    Private Sub BtnClose_Click(sender As Object, e As RoutedEventArgs) Handles BtnClose.Click
        Me.Close()
    End Sub

End Class
