''' <summary>
''' Wynik operacji wykonanej na siatce (np. wypełnianie otworów, teselacja).
''' Zwracany przez wszystkie algorytmy, żeby UI mogło pokazać spójny log operacji.
''' </summary>
Public Class OperationResult
    Public Property Success As Boolean
    Public Property Message As String
    Public Property Details As String

    Public Sub New(success As Boolean, message As String, Optional details As String = "")
        Me.Success = success
        Me.Message = message
        Me.Details = details
    End Sub

    Public Shared Function Ok(message As String, Optional details As String = "") As OperationResult
        Return New OperationResult(True, message, details)
    End Function

    Public Shared Function Fail(message As String, Optional details As String = "") As OperationResult
        Return New OperationResult(False, message, details)
    End Function

    Public Overrides Function ToString() As String
        If String.IsNullOrEmpty(Details) Then Return Message
        Return Message & Environment.NewLine & Details
    End Function
End Class
