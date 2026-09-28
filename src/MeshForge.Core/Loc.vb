''' <summary>Język komunikatów zwracanych przez Core (OperationResult, statystyki itd.).</summary>
Public Enum AppLanguage
    Polish
    English
End Enum

''' <summary>
''' Bardzo prosta lokalizacja komunikatów Core: każde miejsce, które buduje tekst dla użytkownika,
''' woła Loc.T("polski tekst", "english text") zamiast wpisywać gołą literę. Nie ma tu żadnej
''' zależności od WPF/UI - to nadal czysta biblioteka Core, tylko z jednym globalnym przełącznikiem
''' języka, który aplikacja (App) ustawia raz przy starcie / przy zmianie w Preferencjach.
''' </summary>
Public Module Loc

    Public Property Current As AppLanguage = AppLanguage.Polish

    ''' <summary>Zwraca pl albo en, zależnie od Loc.Current.</summary>
    Public Function Translated(pl As String, en As String) As String
        Return If(Current = AppLanguage.English, en, pl)
    End Function

End Module
