Imports MeshForge.Core

''' <summary>Okno preferencji: język/motyw stosowane na żywo (bez przycisku "Zastosuj"); reszta (auto-przeliczanie normalnych, jednostki, głębokość historii) zapisuje się na bieżąco, MainWindow odświeża się po zamknięciu tego okna.</summary>
Class PreferencesWindow

    Private _initializing As Boolean = True

    Public Sub New()
        InitializeComponent()

        CmbLanguage.SelectedIndex = If(Loc.Current = AppLanguage.English, 1, 0)
        ChkDarkMode.IsChecked = AppSettings.IsDarkMode
        ChkAutoRecalcNormals.IsChecked = AppSettings.AutoRecalcNormals

        RefreshUnitsComboTexts()
        CmbUnits.SelectedIndex = CInt(AppSettings.Units)

        TxtHistoryDepth.Text = AppSettings.MaxHistorySteps.ToString()

        _initializing = False
    End Sub

    Private Function UiStr(key As String) As String
        Return CStr(Me.FindResource(key))
    End Function

    ''' <summary>"mm"/"cm" nie są tłumaczone (uniwersalne skróty), ale "Brak"/"cal" tak - odświeżane też po zmianie języka.</summary>
    Private Sub RefreshUnitsComboTexts()
        CmbUnitsNone.Content = UiStr("Str_PrefsUnitsNone")
        CmbUnitsMm.Content = "mm"
        CmbUnitsCm.Content = "cm"
        CmbUnitsIn.Content = UiStr("Str_PrefsUnitsInch")
    End Sub

    Private Sub CmbLanguage_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmbLanguage.SelectionChanged
        If _initializing Then Return
        AppSettings.ApplyLanguage(If(CmbLanguage.SelectedIndex = 1, AppLanguage.English, AppLanguage.Polish))
        RefreshUnitsComboTexts()
    End Sub

    Private Sub ChkDarkMode_Click(sender As Object, e As RoutedEventArgs) Handles ChkDarkMode.Click
        If _initializing Then Return
        Dim dark = ChkDarkMode.IsChecked = True
        AppSettings.ApplyTheme(dark)
        TryCast(Me.Owner, MainWindow)?.ApplyTitleBarTheme(dark)
    End Sub

    Private Sub ChkAutoRecalcNormals_Click(sender As Object, e As RoutedEventArgs) Handles ChkAutoRecalcNormals.Click
        If _initializing Then Return
        AppSettings.AutoRecalcNormals = ChkAutoRecalcNormals.IsChecked = True
        AppSettings.Save()
    End Sub

    Private Sub CmbUnits_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles CmbUnits.SelectionChanged
        If _initializing Then Return
        AppSettings.Units = CType(CmbUnits.SelectedIndex, UnitLabel)
        AppSettings.Save()
    End Sub

    Private Sub TxtHistoryDepth_LostFocus(sender As Object, e As RoutedEventArgs) Handles TxtHistoryDepth.LostFocus
        If _initializing Then Return
        Dim v As Integer
        If Integer.TryParse(TxtHistoryDepth.Text, v) AndAlso v >= 1 Then
            AppSettings.MaxHistorySteps = Math.Min(v, 200) ' rozsądny górny limit - każdy krok historii to pełna kopia siatki w pamięci
            AppSettings.Save()
        End If
        TxtHistoryDepth.Text = AppSettings.MaxHistorySteps.ToString()
    End Sub

    Private Sub BtnClose_Click(sender As Object, e As RoutedEventArgs) Handles BtnClose.Click
        Me.Close()
    End Sub

End Class
