Namespace Geometry

    ''' <summary>
    ''' Wspólne narzędzia do analizy topologii siatki (krawędzie, sąsiedztwo, pętle brzegowe/otwory).
    ''' Używane przez HoleFiller, MeshCleaner, Tessellator, Smoother i statystyki siatki.
    ''' </summary>
    Public Module MeshTopology

        ''' <summary>Klucz nieskierowanej krawędzi (zawsze mniejszy indeks pierwszy) - do zliczania użyć.</summary>
        Public Function EdgeKey(a As Integer, b As Integer) As (lo As Integer, hi As Integer)
            If a <= b Then Return (a, b)
            Return (b, a)
        End Function

        ''' <summary>Liczba trójkątów korzystających z każdej nieskierowanej krawędzi. 1 = krawędź brzegowa (otwór), 2 = normalna, >2 = niemanifoldowa.</summary>
        Public Function BuildEdgeUsage(mesh As Mesh) As Dictionary(Of (Integer, Integer), Integer)
            Dim usage As New Dictionary(Of (Integer, Integer), Integer)
            For Each t In mesh.Triangles
                For Each e In t.DirectedEdges()
                    Dim k = EdgeKey(e.from, e.to)
                    If usage.ContainsKey(k) Then
                        usage(k) += 1
                    Else
                        usage(k) = 1
                    End If
                Next
            Next
            Return usage
        End Function

        ''' <summary>Sąsiedzi każdego wierzchołka (do wygładzania Laplace'a, dekrymacji itp.).</summary>
        Public Function BuildVertexAdjacency(mesh As Mesh) As Dictionary(Of Integer, HashSet(Of Integer))
            Dim adj As New Dictionary(Of Integer, HashSet(Of Integer))

            Dim ensure = Sub(v As Integer)
                             If Not adj.ContainsKey(v) Then adj(v) = New HashSet(Of Integer)
                         End Sub

            For Each t In mesh.Triangles
                ensure(t.A) : ensure(t.B) : ensure(t.C)
                adj(t.A).Add(t.B) : adj(t.A).Add(t.C)
                adj(t.B).Add(t.A) : adj(t.B).Add(t.C)
                adj(t.C).Add(t.A) : adj(t.C).Add(t.B)
            Next
            Return adj
        End Function

        ''' <summary>
        ''' Skierowane krawędzie brzegowe (należące do dokładnie jednego trójkąta), w kierunku
        ''' zgodnym z nawinięciem trójkąta, do którego należą - zachowuje spójną orientację przy wypełnianiu otworów.
        ''' </summary>
        Public Function FindBoundaryDirectedEdges(mesh As Mesh) As List(Of (from As Integer, [to] As Integer))
            Dim usage = BuildEdgeUsage(mesh)
            Dim result As New List(Of (from As Integer, [to] As Integer))
            For Each t In mesh.Triangles
                For Each e In t.DirectedEdges()
                    Dim k = EdgeKey(e.from, e.to)
                    If usage(k) = 1 Then
                        result.Add(e)
                    End If
                Next
            Next
            Return result
        End Function

        ''' <summary>
        ''' Grupuje skierowane krawędzie brzegowe w zamknięte pętle (kontury otworów).
        ''' Każda pętla to lista indeksów wierzchołków w kolejności obiegu.
        ''' Pętle niemanifoldowe (rozgałęzione) lub niezamknięte są pomijane.
        ''' </summary>
        Public Function FindBoundaryLoops(mesh As Mesh) As List(Of List(Of Integer))
            Dim boundaryEdges = FindBoundaryDirectedEdges(mesh)
            Dim loops As New List(Of List(Of Integer))
            If boundaryEdges.Count = 0 Then Return loops

            ' from -> lista możliwych "to" (zwykle dokładnie jeden dla prostej pętli)
            Dim nextMap As New Dictionary(Of Integer, List(Of Integer))
            For Each e In boundaryEdges
                If Not nextMap.ContainsKey(e.from) Then nextMap(e.from) = New List(Of Integer)
                nextMap(e.from).Add(e.to)
            Next

            Dim usedStart As New HashSet(Of Integer)
            Dim usedEdge As New HashSet(Of (Integer, Integer))

            For Each startEdge In boundaryEdges
                If usedEdge.Contains((startEdge.from, startEdge.to)) Then Continue For

                Dim loop1 As New List(Of Integer)
                Dim current = startEdge.from
                Dim guard = 0
                Dim maxSteps = boundaryEdges.Count + 1
                Dim closed = False

                Do
                    loop1.Add(current)
                    Dim candidates = nextMap.GetValueOrDefault(current)
                    If candidates Is Nothing OrElse candidates.Count = 0 Then
                        Exit Do ' ślepy zaułek - pętla niemanifoldowa/przerwana, porzucamy
                    End If

                    ' wybierz pierwszą jeszcze nieużytą krawędź wychodzącą z current
                    Dim chosen As Integer = -1
                    For Each cand In candidates
                        If Not usedEdge.Contains((current, cand)) Then
                            chosen = cand
                            Exit For
                        End If
                    Next
                    If chosen = -1 Then Exit Do

                    usedEdge.Add((current, chosen))
                    current = chosen
                    guard += 1

                    If current = startEdge.from Then
                        closed = True
                        Exit Do
                    End If
                Loop While guard < maxSteps

                If closed AndAlso loop1.Count >= 3 Then
                    loops.Add(loop1)
                End If
            Next

            Return loops
        End Function

        ''' <summary>Siatka jest "wodoszczelna" (bez otworów), gdy nie ma żadnych krawędzi brzegowych.</summary>
        Public Function IsWatertight(mesh As Mesh) As Boolean
            Return FindBoundaryDirectedEdges(mesh).Count = 0
        End Function

    End Module

End Namespace
