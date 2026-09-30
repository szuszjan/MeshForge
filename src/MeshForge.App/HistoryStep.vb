Imports MeshForge.Core.Geometry

''' <summary>Jeden krok na pasku historii (patrz MainWindow._history) - etykieta operacji plus pełna migawka siatki PO jej wykonaniu, żeby można było skoczyć bezpośrednio do tego punktu.</summary>
Public Class HistoryStep
    Public Property Label As String
    Public Property Snapshot As Mesh
End Class
