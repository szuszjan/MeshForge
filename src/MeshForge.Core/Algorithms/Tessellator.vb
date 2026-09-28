Imports System.Collections.Generic
Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>
    ''' Teselacja siatki w obu kierunkach:
    ''' - Subdivide: zagęszcza siatkę (więcej trójkątów, gładszy wygląd krawędzi) przez podział każdego
    '''   trójkąta na 4 w środkach krawędzi.
    ''' - DecimateByEdgeCollapse: upraszcza siatkę (mniej trójkątów) metodą błędu kwadratowego
    '''   Garland-Heckbert (QEM) - standardowy, sprawdzony algorytm dekrymacji siatek.
    ''' </summary>
    Public Module Tessellator

#Region "Zagęszczanie (subdivision)"

        Public Function Subdivide(mesh As Mesh, iterations As Integer) As OperationResult
            If iterations <= 0 Then Return OperationResult.Fail(Loc.Translated("Liczba iteracji zagęszczania musi być dodatnia.", "The number of subdivision iterations must be positive."))
            If mesh.TriangleCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka nie ma trójkątów.", "The mesh has no triangles."))

            Dim beforeTris = mesh.TriangleCount
            Dim beforeVerts = mesh.VertexCount

            For iter = 1 To iterations
                SubdivideOnce(mesh)
            Next

            Return OperationResult.Ok(
                Loc.Translated(
                    $"Zagęszczono siatkę ({iterations}x): {beforeVerts:N0}→{mesh.VertexCount:N0} wierzchołków, {beforeTris:N0}→{mesh.TriangleCount:N0} trójkątów.",
                    $"Subdivided mesh ({iterations}x): {beforeVerts:N0}→{mesh.VertexCount:N0} vertices, {beforeTris:N0}→{mesh.TriangleCount:N0} triangles."),
                Loc.Translated(
                    "Uwaga: każda iteracja mnoży liczbę trójkątów ×4 - dla dużych siatek zaczynaj od 1 iteracji.",
                    "Note: each iteration multiplies the triangle count ×4 - for large meshes, start with 1 iteration."))
        End Function

        Private Sub SubdivideOnce(mesh As Mesh)
            Dim hasN = mesh.HasNormals
            Dim hasUV = mesh.HasUVs
            Dim hasColor = mesh.HasVertexColors

            Dim newVerts As New List(Of Vector3)(mesh.Vertices)
            Dim newNormals As New List(Of Vector3)(If(hasN, mesh.Normals, New List(Of Vector3)))
            Dim newUVs As New List(Of Vector2)(If(hasUV, mesh.UVs, New List(Of Vector2)))
            Dim newColors As New List(Of RgbColor)(If(hasColor, mesh.VertexColors, New List(Of RgbColor)))

            Dim midpointCache As New Dictionary(Of (Integer, Integer), Integer)

            Dim getMidpoint =
                Function(i1 As Integer, i2 As Integer) As Integer
                    Dim key = MeshTopology.EdgeKey(i1, i2)
                    Dim existing As Integer
                    If midpointCache.TryGetValue(key, existing) Then Return existing

                    Dim newIdx = newVerts.Count
                    newVerts.Add((mesh.Vertices(i1) + mesh.Vertices(i2)) * 0.5F)

                    If hasN Then
                        Dim sum = mesh.Normals(i1) + mesh.Normals(i2)
                        newNormals.Add(If(sum.LengthSquared() > 0.0000001F, Vector3.Normalize(sum), mesh.Normals(i1)))
                    End If
                    If hasUV Then newUVs.Add((mesh.UVs(i1) + mesh.UVs(i2)) * 0.5F)
                    If hasColor Then newColors.Add(RgbColor.Lerp(mesh.VertexColors(i1), mesh.VertexColors(i2), 0.5F))

                    midpointCache(key) = newIdx
                    Return newIdx
                End Function

            Dim newTriangles As New List(Of Triangle)(mesh.Triangles.Count * 4)
            For Each t In mesh.Triangles
                Dim mAB = getMidpoint(t.A, t.B)
                Dim mBC = getMidpoint(t.B, t.C)
                Dim mCA = getMidpoint(t.C, t.A)
                newTriangles.Add(New Triangle(t.A, mAB, mCA))
                newTriangles.Add(New Triangle(t.B, mBC, mAB))
                newTriangles.Add(New Triangle(t.C, mCA, mBC))
                newTriangles.Add(New Triangle(mAB, mBC, mCA))
            Next

            mesh.Vertices.Clear() : mesh.Vertices.AddRange(newVerts)
            mesh.Triangles.Clear() : mesh.Triangles.AddRange(newTriangles)
            mesh.Normals.Clear() : If hasN Then mesh.Normals.AddRange(newNormals)
            mesh.UVs.Clear() : If hasUV Then mesh.UVs.AddRange(newUVs)
            mesh.VertexColors.Clear() : If hasColor Then mesh.VertexColors.AddRange(newColors)
        End Sub

#End Region

#Region "Decymacja (upraszczanie metodą QEM Garland-Heckbert)"

        Public Function DecimateToFraction(mesh As Mesh, fraction As Single) As OperationResult
            fraction = Math.Max(0.01F, Math.Min(1.0F, fraction))
            Dim target = CInt(mesh.TriangleCount * fraction)
            Return DecimateByEdgeCollapse(mesh, target)
        End Function

        Public Function DecimateByEdgeCollapse(mesh As Mesh, targetTriangleCount As Integer) As OperationResult
            If mesh.TriangleCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka nie ma trójkątów.", "The mesh has no triangles."))
            targetTriangleCount = Math.Max(4, targetTriangleCount)
            Dim beforeTris = mesh.TriangleCount
            Dim beforeVerts = mesh.VertexCount

            If targetTriangleCount >= mesh.TriangleCount Then
                Return OperationResult.Ok(Loc.Translated("Docelowa liczba trójkątów nie mniejsza niż obecna - nic do zrobienia.", "Target triangle count is not lower than the current one - nothing to do."))
            End If

            Dim vertexCount = mesh.Vertices.Count
            Dim vertexAlive(vertexCount - 1) As Boolean
            Dim vertexGeneration(vertexCount - 1) As Integer
            Dim quadrics As New List(Of Double(,))(vertexCount)
            For i = 0 To vertexCount - 1
                vertexAlive(i) = True
                quadrics.Add(New Double(3, 3) {})
            Next

            Dim triangles = mesh.Triangles ' pracujemy bezpośrednio na liście siatki (mutowalna)
            Dim triangleAlive(triangles.Count - 1) As Boolean
            For i = 0 To triangleAlive.Length - 1
                triangleAlive(i) = True
            Next

            Dim trianglesByVertex As New Dictionary(Of Integer, HashSet(Of Integer))
            Dim ensureSet = Sub(v As Integer)
                                If Not trianglesByVertex.ContainsKey(v) Then trianglesByVertex(v) = New HashSet(Of Integer)
                            End Sub

            ' 1) zbuduj kwadryki błędu każdego wierzchołka z płaszczyzn sąsiadujących trójkątów
            For ti = 0 To triangles.Count - 1
                Dim t = triangles(ti)
                ensureSet(t.A) : ensureSet(t.B) : ensureSet(t.C)
                trianglesByVertex(t.A).Add(ti) : trianglesByVertex(t.B).Add(ti) : trianglesByVertex(t.C).Add(ti)

                Dim p0 = mesh.Vertices(t.A) : Dim p1 = mesh.Vertices(t.B) : Dim p2 = mesh.Vertices(t.C)
                Dim n = Vector3.Cross(p1 - p0, p2 - p0)
                Dim lenSq = n.LengthSquared()
                If lenSq <= 0.0000000001F Then Continue For ' zdegenerowany trójkąt - brak wkładu do kwadryki
                n = n / MathF.Sqrt(lenSq)
                Dim d = -Vector3.Dot(n, p0)
                Dim k = MakeQuadric(n.X, n.Y, n.Z, d)
                AddQuadric(quadrics(t.A), k)
                AddQuadric(quadrics(t.B), k)
                AddQuadric(quadrics(t.C), k)
            Next

            ' 2) kolejka priorytetowa kandydatów do scalenia (najtańszy błąd na wierzchu)
            Dim queue As New PriorityQueue(Of EdgeCandidate, Double)

            Dim tryPushEdge =
                Sub(u As Integer, v As Integer)
                    If Not (vertexAlive(u) AndAlso vertexAlive(v)) Then Return
                    Dim qSum = SumQuadrics(quadrics(u), quadrics(v))
                    Dim optPos = SolveOptimalPosition(qSum, mesh.Vertices(u), mesh.Vertices(v))
                    Dim cost = EvaluateQuadric(qSum, optPos)
                    queue.Enqueue(New EdgeCandidate With {
                        .U = u, .V = v, .OptimalPos = optPos,
                        .GenU = vertexGeneration(u), .GenV = vertexGeneration(v)
                    }, cost)
                End Sub

            For Each edge In MeshTopology.BuildEdgeUsage(mesh).Keys
                tryPushEdge(edge.Item1, edge.Item2)
            Next

            ' 3) pętla główna: ściągaj najtańszą krawędź, scalaj, aktualizuj lokalnie
            Dim currentTriCount = triangles.Count
            While currentTriCount > targetTriangleCount AndAlso queue.Count > 0
                Dim candidate As EdgeCandidate = Nothing
                Dim cost As Double
                Dim gotOne = False

                While queue.Count > 0
                    queue.TryDequeue(candidate, cost)
                    If Not vertexAlive(candidate.U) OrElse Not vertexAlive(candidate.V) Then Continue While
                    If vertexGeneration(candidate.U) <> candidate.GenU OrElse vertexGeneration(candidate.V) <> candidate.GenV Then Continue While
                    gotOne = True
                    Exit While
                End While
                If Not gotOne Then Exit While ' kolejka pusta / same nieaktualne wpisy

                Dim u = candidate.U
                Dim v = candidate.V

                ' scal v w u
                mesh.Vertices(u) = candidate.OptimalPos
                quadrics(u) = SumQuadrics(quadrics(u), quadrics(v))
                vertexAlive(v) = False
                vertexGeneration(u) += 1

                ensureSet(u)
                Dim vTris = If(trianglesByVertex.ContainsKey(v), New List(Of Integer)(trianglesByVertex(v)), New List(Of Integer))
                For Each ti In vTris
                    If Not triangleAlive(ti) Then Continue For
                    Dim newT = triangles(ti).WithReplacedIndex(v, u)
                    If newT.IsDegenerate Then
                        triangleAlive(ti) = False
                        currentTriCount -= 1
                    Else
                        triangles(ti) = newT
                        trianglesByVertex(u).Add(ti)
                    End If
                Next
                trianglesByVertex.Remove(v)

                If currentTriCount <= targetTriangleCount Then Exit While

                ' znajdź aktualnych sąsiadów u i wrzuć świeże kandydatury
                Dim neighbors As New HashSet(Of Integer)
                For Each ti In trianglesByVertex(u)
                    If Not triangleAlive(ti) Then Continue For
                    For Each idx In triangles(ti).Indices()
                        If idx <> u AndAlso vertexAlive(idx) Then neighbors.Add(idx)
                    Next
                Next
                For Each w In neighbors
                    tryPushEdge(u, w)
                Next
            End While

            ' 4) usuń martwe trójkąty, wyczyść nieużywane wierzchołki, zaktualizuj siatkę
            Dim survivors As New List(Of Triangle)(currentTriCount)
            For i = 0 To triangles.Count - 1
                If triangleAlive(i) Then survivors.Add(triangles(i))
            Next
            mesh.Triangles.Clear()
            mesh.Triangles.AddRange(survivors)
            mesh.ClearNormals() ' pozycje się zmieniły (optymalne punkty QEM) - stare normalne są nieaktualne

            Dim removedVerts = MeshCleaner.RemoveUnusedVertices(mesh)

            Return OperationResult.Ok(
                Loc.Translated(
                    $"Uproszczono siatkę: {beforeTris:N0}→{mesh.TriangleCount:N0} trójkątów ({beforeVerts:N0}→{mesh.VertexCount:N0} wierzchołków).",
                    $"Simplified mesh: {beforeTris:N0}→{mesh.TriangleCount:N0} triangles ({beforeVerts:N0}→{mesh.VertexCount:N0} vertices)."),
                Loc.Translated("Metoda: Garland-Heckbert QEM. Zalecane: przelicz normalne po tej operacji.", "Method: Garland-Heckbert QEM. Recommended: recalculate normals after this operation."))
        End Function

        Private Structure EdgeCandidate
            Public U As Integer
            Public V As Integer
            Public OptimalPos As Vector3
            Public GenU As Integer
            Public GenV As Integer
        End Structure

        ''' <summary>Kwadryka błędu płaszczyzny (nx,ny,nz,d) jako iloczyn zewnętrzny wektora [n,d] z samym sobą (macierz 4x4 symetryczna).</summary>
        Private Function MakeQuadric(nx As Single, ny As Single, nz As Single, d As Single) As Double(,)
            Dim v() As Double = {nx, ny, nz, d}
            Dim q(3, 3) As Double
            For i = 0 To 3
                For j = 0 To 3
                    q(i, j) = v(i) * v(j)
                Next
            Next
            Return q
        End Function

        Private Function SumQuadrics(a As Double(,), b As Double(,)) As Double(,)
            Dim r(3, 3) As Double
            For i = 0 To 3
                For j = 0 To 3
                    r(i, j) = a(i, j) + b(i, j)
                Next
            Next
            Return r
        End Function

        Private Sub AddQuadric(target As Double(,), addend As Double(,))
            For i = 0 To 3
                For j = 0 To 3
                    target(i, j) += addend(i, j)
                Next
            Next
        End Sub

        ''' <summary>Wartość formy kwadratowej Q w jednorodnym punkcie [x,y,z,1] - błąd geometryczny w tym punkcie.</summary>
        Private Function EvaluateQuadric(q As Double(,), p As Vector3) As Double
            Dim x = CDbl(p.X) : Dim y = CDbl(p.Y) : Dim z = CDbl(p.Z)
            Return q(0, 0) * x * x + q(1, 1) * y * y + q(2, 2) * z * z + q(3, 3) +
                   2 * q(0, 1) * x * y + 2 * q(0, 2) * x * z + 2 * q(0, 3) * x +
                   2 * q(1, 2) * y * z + 2 * q(1, 3) * y +
                   2 * q(2, 3) * z
        End Function

        ''' <summary>
        ''' Rozwiązuje układ 3x3 wynikający z kwadryki (metoda Cramera), żeby znaleźć punkt minimalizujący błąd.
        ''' Gdy układ jest osobliwy (płaska/współliniowa okolica), wybiera najlepszy z {u, v, środek} po prostu porównując błąd.
        ''' </summary>
        Private Function SolveOptimalPosition(q As Double(,), posU As Vector3, posV As Vector3) As Vector3
            Dim a00 = q(0, 0) : Dim a01 = q(0, 1) : Dim a02 = q(0, 2)
            Dim a11 = q(1, 1) : Dim a12 = q(1, 2)
            Dim a22 = q(2, 2)
            Dim b0 = -q(0, 3) : Dim b1 = -q(1, 3) : Dim b2 = -q(2, 3)

            Dim det = a00 * (a11 * a22 - a12 * a12) - a01 * (a01 * a22 - a12 * a02) + a02 * (a01 * a12 - a11 * a02)

            If Math.Abs(det) > 0.0000001 Then
                Dim detX = b0 * (a11 * a22 - a12 * a12) - a01 * (b1 * a22 - a12 * b2) + a02 * (b1 * a12 - a11 * b2)
                Dim detY = a00 * (b1 * a22 - a12 * b2) - b0 * (a01 * a22 - a12 * a02) + a02 * (a01 * b2 - b1 * a02)
                Dim detZ = a00 * (a11 * b2 - b1 * a12) - a01 * (a01 * b2 - b1 * a02) + b0 * (a01 * a12 - a11 * a02)
                Dim solved = New Vector3(CSng(detX / det), CSng(detY / det), CSng(detZ / det))
                Return solved
            End If

            ' fallback: układ osobliwy - wybierz najlepszą z trzech kandydatur po prostu ocenie błędu
            Dim mid = (posU + posV) * 0.5F
            Dim costU = EvaluateQuadric(q, posU)
            Dim costV = EvaluateQuadric(q, posV)
            Dim costMid = EvaluateQuadric(q, mid)
            If costU <= costV AndAlso costU <= costMid Then Return posU
            If costV <= costU AndAlso costV <= costMid Then Return posV
            Return mid
        End Function

#End Region

    End Module

End Namespace
