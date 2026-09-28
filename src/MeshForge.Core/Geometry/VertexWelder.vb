Imports System.Numerics

Namespace Geometry

    ''' <summary>
    ''' Łączy (spawa) wierzchołki leżące bliżej siebie niż epsilon w jeden.
    ''' Potrzebne np. przy wczytywaniu STL (który nie indeksuje wierzchołków - każdy trójkąt ma własne 3 punkty)
    ''' oraz przy czyszczeniu siatki (MeshCleaner) z duplikatów powstałych np. po eksporcie z innego programu.
    ''' </summary>
    Public Module VertexWelder

        ''' <summary>
        ''' Zwraca mapowanie starego indeksu na nowy (ujednolicony) indeks oraz listę unikalnych pozycji.
        ''' Używa siatki przestrzennej (spatial hash) o rozmiarze komórki = epsilon, więc działa w czasie ~O(n).
        ''' </summary>
        Public Function WeldPositions(positions As List(Of Vector3), epsilon As Single) As (mapping As Integer(), uniquePositions As List(Of Vector3))
            Dim mapping(positions.Count - 1) As Integer
            Dim uniquePositions As New List(Of Vector3)
            If epsilon <= 0 Then epsilon = 0.000001F

            ' klucz komórki siatki -> lista indeksów unikalnych punktów w tej komórce (i sąsiednich, sprawdzane przy zapytaniu)
            Dim cellSize = epsilon
            Dim grid As New Dictionary(Of (Integer, Integer, Integer), List(Of Integer))

            Dim cellOf = Function(p As Vector3) As (Integer, Integer, Integer)
                             Return (CInt(Math.Floor(p.X / cellSize)), CInt(Math.Floor(p.Y / cellSize)), CInt(Math.Floor(p.Z / cellSize)))
                         End Function

            For i = 0 To positions.Count - 1
                Dim p = positions(i)
                Dim cell = cellOf(p)
                Dim foundIndex As Integer = -1

                ' sprawdź 27 sąsiadujących komórek (3x3x3) na wypadek, gdy bliski punkt wpadł do sąsiedniej komórki
                For dx = -1 To 1
                    For dy = -1 To 1
                        For dz = -1 To 1
                            Dim key = (cell.Item1 + dx, cell.Item2 + dy, cell.Item3 + dz)
                            Dim bucket As List(Of Integer) = Nothing
                            If grid.TryGetValue(key, bucket) Then
                                For Each candidateIdx In bucket
                                    If Vector3.DistanceSquared(uniquePositions(candidateIdx), p) <= epsilon * epsilon Then
                                        foundIndex = candidateIdx
                                        Exit For
                                    End If
                                Next
                            End If
                            If foundIndex >= 0 Then Exit For
                        Next
                        If foundIndex >= 0 Then Exit For
                    Next
                    If foundIndex >= 0 Then Exit For
                Next

                If foundIndex >= 0 Then
                    mapping(i) = foundIndex
                Else
                    Dim newIdx = uniquePositions.Count
                    uniquePositions.Add(p)
                    If Not grid.ContainsKey(cell) Then grid(cell) = New List(Of Integer)
                    grid(cell).Add(newIdx)
                    mapping(i) = newIdx
                End If
            Next

            Return (mapping, uniquePositions)
        End Function

        ''' <summary>Domyślny epsilon proporcjonalny do rozmiaru modelu (przekątna bbox * współczynnik).</summary>
        Public Function SuggestEpsilon(positions As List(Of Vector3), Optional factor As Single = 0.00001F) As Single
            If positions.Count = 0 Then Return 0.000001F
            Dim mn = positions(0)
            Dim mx = positions(0)
            For Each p In positions
                mn = Vector3.Min(mn, p)
                mx = Vector3.Max(mx, p)
            Next
            Dim diag = Vector3.Distance(mn, mx)
            If diag <= 0 Then Return 0.000001F
            Return diag * factor
        End Function

    End Module

End Namespace
