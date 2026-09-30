Imports System.IO
Imports System.Globalization
Imports MeshForge.Core

''' <summary>Jednostka wyświetlana obok wymiarów/pola/objętości w statystykach - czysto kosmetyczna etykieta, nie wpływa na żadne obliczenia (Core nic o niej nie wie).</summary>
Public Enum UnitLabel
    None
    Millimeters
    Centimeters
    Inches
End Enum

''' <summary>
''' Przełącza motyw (jasny/ciemny) i język (polski/angielski) w locie - bez restartu - przez podmianę
''' odpowiedniego merged dictionary w Application.Resources (zobacz komentarz w Application.xaml:
''' [0]=paleta kolorów, [1]=słownik tekstów UI, [2]=style, w tej stałej kolejności).
''' Trzyma też resztę zapamiętywanych preferencji (patrz niżej) - wszystko w jednym prostym pliku
''' key=value w %AppData%\MeshForge\settings.txt.
''' </summary>
Public Module AppSettings

    Private ReadOnly SettingsPath As String =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MeshForge", "settings.txt")

    Private Const PaletteDictIndex = 0
    Private Const LangDictIndex = 1

    Public Property IsDarkMode As Boolean = True
    Public Property ShowWireframe As Boolean = False
    Public Property AutoRecalcNormals As Boolean = False
    Public Property Units As UnitLabel = UnitLabel.None
    Public Property MaxHistorySteps As Integer = 15

    ''' <summary>Ostatnio użyte parametry operacji - wczytywane do pól tekstowych zamiast sztywnych domyślnych przy starcie.</summary>
    Public Property LastSmoothIterations As Integer = 3
    Public Property LastSmoothStrength As Single = 0.5F
    Public Property LastSubdivideIterations As Integer = 1
    Public Property LastDecimatePercent As Single = 50.0F

    ''' <summary>Sufiks jednostki do wyświetlenia (pusty, jeśli wyłączone) - "mm"/"cm"/"in".</summary>
    Public Function UnitSuffix() As String
        Select Case Units
            Case UnitLabel.Millimeters : Return "mm"
            Case UnitLabel.Centimeters : Return "cm"
            Case UnitLabel.Inches : Return "in"
            Case Else : Return ""
        End Select
    End Function

    ''' <summary>Wczytuje zapamiętane preferencje (jeśli są) i stosuje motyw+język. Wywołaj raz, na starcie aplikacji.</summary>
    Public Sub LoadAndApply()
        Dim dark = True
        Dim lang = AppLanguage.English
        Try
            If File.Exists(SettingsPath) Then
                For Each rawLine In File.ReadLines(SettingsPath)
                    Dim parts = rawLine.Split(New Char() {"="c}, 2)
                    If parts.Length <> 2 Then Continue For
                    Dim key = parts(0).Trim().ToLowerInvariant()
                    Dim value = parts(1).Trim()
                    Select Case key
                        Case "theme"
                            dark = Not value.Equals("light", StringComparison.OrdinalIgnoreCase)
                        Case "language"
                            If value.Equals("pl", StringComparison.OrdinalIgnoreCase) Then lang = AppLanguage.Polish
                        Case "wireframe"
                            ShowWireframe = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                        Case "autorecalcnormals"
                            AutoRecalcNormals = value.Equals("true", StringComparison.OrdinalIgnoreCase)
                        Case "units"
                            Select Case value.ToLowerInvariant()
                                Case "mm" : Units = UnitLabel.Millimeters
                                Case "cm" : Units = UnitLabel.Centimeters
                                Case "in" : Units = UnitLabel.Inches
                                Case Else : Units = UnitLabel.None
                            End Select
                        Case "historydepth"
                            MaxHistorySteps = ParseIntOrDefault(value, MaxHistorySteps)
                        Case "smoothiterations"
                            LastSmoothIterations = ParseIntOrDefault(value, LastSmoothIterations)
                        Case "smoothstrength"
                            LastSmoothStrength = ParseSingleOrDefault(value, LastSmoothStrength)
                        Case "subdivideiterations"
                            LastSubdivideIterations = ParseIntOrDefault(value, LastSubdivideIterations)
                        Case "decimatepercent"
                            LastDecimatePercent = ParseSingleOrDefault(value, LastDecimatePercent)
                    End Select
                Next
            End If
        Catch
            ' brak/nieczytelny plik ustawień - zostań przy domyślnych (ciemny, angielski, reszta jak wyżej)
        End Try
        ApplyTheme(dark)
        ApplyLanguage(lang)
    End Sub

    Public Sub ApplyTheme(dark As Boolean)
        IsDarkMode = dark
        Dim uri = New Uri(If(dark, "Styles/Palette.Dark.xaml", "Styles/Palette.Light.xaml"), UriKind.Relative)
        Application.Current.Resources.MergedDictionaries(PaletteDictIndex) = New ResourceDictionary With {.Source = uri}
        Save()
    End Sub

    Public Sub ApplyLanguage(lang As AppLanguage)
        Loc.Current = lang
        Dim uri = New Uri(If(lang = AppLanguage.English, "Styles/Lang.English.xaml", "Styles/Lang.Polish.xaml"), UriKind.Relative)
        Application.Current.Resources.MergedDictionaries(LangDictIndex) = New ResourceDictionary With {.Source = uri}
        Save()
    End Sub

    ''' <summary>Zapisuje WSZYSTKIE bieżące preferencje na dysk - wołaj po zmianie którejkolwiek (motyw/język robią to już same).</summary>
    Public Sub Save()
        Try
            Dim dir = Path.GetDirectoryName(SettingsPath)
            If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
            File.WriteAllLines(SettingsPath, {
                $"theme={If(IsDarkMode, "dark", "light")}",
                $"language={If(Loc.Current = AppLanguage.English, "en", "pl")}",
                $"wireframe={ShowWireframe.ToString().ToLowerInvariant()}",
                $"autoRecalcNormals={AutoRecalcNormals.ToString().ToLowerInvariant()}",
                $"units={If(Units = UnitLabel.Millimeters, "mm", If(Units = UnitLabel.Centimeters, "cm", If(Units = UnitLabel.Inches, "in", "none")))}",
                $"historyDepth={MaxHistorySteps}",
                $"smoothIterations={LastSmoothIterations}",
                $"smoothStrength={LastSmoothStrength.ToString(CultureInfo.InvariantCulture)}",
                $"subdivideIterations={LastSubdivideIterations}",
                $"decimatePercent={LastDecimatePercent.ToString(CultureInfo.InvariantCulture)}"
            })
        Catch
            ' zapis preferencji to tylko wygoda - brak zapisu nie powinien wywalać programu
        End Try
    End Sub

    Private Function ParseIntOrDefault(text As String, defaultValue As Integer) As Integer
        Dim v As Integer
        If Integer.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then Return v
        Return defaultValue
    End Function

    Private Function ParseSingleOrDefault(text As String, defaultValue As Single) As Single
        Dim v As Single
        If Single.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return v
        Return defaultValue
    End Function

End Module
