Imports System.IO
Imports MeshForge.Core

''' <summary>
''' Przełącza motyw (jasny/ciemny) i język (polski/angielski) w locie - bez restartu - przez podmianę
''' odpowiedniego merged dictionary w Application.Resources (zobacz komentarz w Application.xaml:
''' [0]=paleta kolorów, [1]=słownik tekstów UI, [2]=style, w tej stałej kolejności).
''' Zapamiętuje oba wybory w %AppData%\MeshForge\settings.txt (prosty format klucz=wartość).
''' </summary>
Public Module AppSettings

    Private ReadOnly SettingsPath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeshForge", "settings.txt")

    Private Const PaletteDictIndex = 0
    Private Const LangDictIndex = 1

    Public Property IsDarkMode As Boolean = True

    ''' <summary>Wczytuje zapamiętane preferencje (jeśli są) i stosuje obie naraz. Wywołaj raz, na starcie aplikacji.</summary>
    Public Sub LoadAndApply()
        Dim dark = True
        Dim lang = AppLanguage.English
        Try
            If File.Exists(SettingsPath) Then
                For Each rawLine In File.ReadLines(SettingsPath)
                    Dim parts = rawLine.Split(New Char() {"="c}, 2)
                    If parts.Length <> 2 Then Continue For
                    Select Case parts(0).Trim().ToLowerInvariant()
                        Case "theme"
                            dark = Not parts(1).Trim().Equals("light", StringComparison.OrdinalIgnoreCase)
                        Case "language"
                            If parts(1).Trim().Equals("pl", StringComparison.OrdinalIgnoreCase) Then lang = AppLanguage.Polish
                    End Select
                Next
            End If
        Catch
            ' brak/nieczytelny plik ustawień - zostań przy domyślnych (ciemny, angielski)
        End Try
        ApplyTheme(dark)
        ApplyLanguage(lang)
    End Sub

    Public Sub ApplyTheme(dark As Boolean)
        IsDarkMode = dark
        Dim uri = New Uri(If(dark, "Styles/Palette.Dark.xaml", "Styles/Palette.Light.xaml"), UriKind.Relative)
        Application.Current.Resources.MergedDictionaries(PaletteDictIndex) = New ResourceDictionary With {.Source = uri}
        SavePreferences()
    End Sub

    Public Sub ApplyLanguage(lang As AppLanguage)
        Loc.Current = lang
        Dim uri = New Uri(If(lang = AppLanguage.English, "Styles/Lang.English.xaml", "Styles/Lang.Polish.xaml"), UriKind.Relative)
        Application.Current.Resources.MergedDictionaries(LangDictIndex) = New ResourceDictionary With {.Source = uri}
        SavePreferences()
    End Sub

    Private Sub SavePreferences()
        Try
            Dim dir = Path.GetDirectoryName(SettingsPath)
            If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
            File.WriteAllLines(SettingsPath, {
                $"theme={If(IsDarkMode, "dark", "light")}",
                $"language={If(Loc.Current = AppLanguage.English, "en", "pl")}"
            })
        Catch
            ' zapis preferencji to tylko wygoda - brak zapisu nie powinien wywalać programu
        End Try
    End Sub

End Module
