Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>
    ''' Wygładzanie Laplace'a - redukuje szum skanera przesuwając każdy wierzchołek w stronę
    ''' średniej jego sąsiadów. Wierzchołki na brzegu otwartego skanu (krawędzie otworów) są
    ''' celowo pomijane, żeby wygładzanie nie "kurczyło" brzegu do środka.
    ''' </summary>
    Public Module Smoother

        Public Function Smooth(mesh As Mesh, iterations As Integer, Optional factor As Single = 0.5F) As OperationResult
            If iterations <= 0 Then Return OperationResult.Fail(Loc.Translated("Liczba iteracji wygładzania musi być dodatnia.", "The number of smoothing iterations must be positive."))
            If mesh.VertexCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "The mesh is empty."))
            factor = Math.Max(0.0F, Math.Min(1.0F, factor))

            Dim adjacency = MeshTopology.BuildVertexAdjacency(mesh)

            Dim boundaryVerts As New HashSet(Of Integer)
            For Each e In MeshTopology.FindBoundaryDirectedEdges(mesh)
                boundaryVerts.Add(e.from)
                boundaryVerts.Add(e.to)
            Next

            For iter = 1 To iterations
                Dim newPositions(mesh.Vertices.Count - 1) As Vector3
                For i = 0 To mesh.Vertices.Count - 1
                    newPositions(i) = mesh.Vertices(i)
                Next

                For Each kvp In adjacency
                    Dim v = kvp.Key
                    If boundaryVerts.Contains(v) Then Continue For ' zachowaj brzeg otworów bez zmian
                    Dim neighbors = kvp.Value
                    If neighbors.Count = 0 Then Continue For

                    Dim avg As Vector3 = Vector3.Zero
                    For Each n In neighbors
                        avg += mesh.Vertices(n)
                    Next
                    avg /= neighbors.Count

                    newPositions(v) = Vector3.Lerp(mesh.Vertices(v), avg, factor)
                Next

                For i = 0 To mesh.Vertices.Count - 1
                    mesh.Vertices(i) = newPositions(i)
                Next
            Next

            mesh.ClearNormals()
            Dim skipped = boundaryVerts.Count
            Dim details = If(skipped > 0, Loc.Translated($"Pominięto {skipped} wierzchołków brzegowych (krawędzie otworów) - zostały nienaruszone.", $"Skipped {skipped} boundary vertices (hole edges) - left unchanged."), "")
            Return OperationResult.Ok(Loc.Translated($"Wygładzono siatkę ({iterations}x, współczynnik {factor:F2}).", $"Smoothed mesh ({iterations}x, factor {factor:F2})."), details)
        End Function

    End Module

End Namespace
