Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>Podstawowe operacje porządkujące siatkę: spawanie zdublowanych wierzchołków, usuwanie zdegenerowanych/powtórzonych trójkątów i martwych wierzchołków.</summary>
    Public Module MeshCleaner

        ''' <summary>
        ''' Wykonuje pełne czyszczenie w bezpiecznej kolejności: spawanie blisko leżących wierzchołków,
        ''' usunięcie zdegenerowanych i zdublowanych trójkątów, usunięcie nieużywanych wierzchołków.
        ''' To jest funkcja, którą warto uruchomić jako pierwszą po imporcie surowego skanu.
        ''' </summary>
        Public Function CleanAll(mesh As Mesh, Optional weldEpsilon As Single = 0) As OperationResult
            Dim before = (mesh.VertexCount, mesh.TriangleCount)

            If weldEpsilon <= 0 Then weldEpsilon = VertexWelder.SuggestEpsilon(mesh.Vertices)
            Dim welded = WeldDuplicateVertices(mesh, weldEpsilon)
            Dim degenerate = RemoveDegenerateTriangles(mesh)
            Dim duplicates = RemoveDuplicateTriangles(mesh)
            Dim unused = RemoveUnusedVertices(mesh)

            Dim details = Loc.Translated(
                $"Spawanie wierzchołków (eps={weldEpsilon:G3}): {welded}" & Environment.NewLine &
                $"Usunięte zdegenerowane trójkąty: {degenerate}" & Environment.NewLine &
                $"Usunięte zdublowane trójkąty: {duplicates}" & Environment.NewLine &
                $"Usunięte nieużywane wierzchołki: {unused}",
                $"Welded vertices (eps={weldEpsilon:G3}): {welded}" & Environment.NewLine &
                $"Removed degenerate triangles: {degenerate}" & Environment.NewLine &
                $"Removed duplicate triangles: {duplicates}" & Environment.NewLine &
                $"Removed unused vertices: {unused}")

            Return OperationResult.Ok(
                Loc.Translated(
                    $"Wyczyszczono siatkę: {before.Item1:N0}→{mesh.VertexCount:N0} wierzchołków, {before.Item2:N0}→{mesh.TriangleCount:N0} trójkątów.",
                    $"Mesh cleaned: {before.Item1:N0}→{mesh.VertexCount:N0} vertices, {before.Item2:N0}→{mesh.TriangleCount:N0} triangles."),
                details)
        End Function

        ''' <summary>Łączy wierzchołki bliższe niż epsilon. Zwraca liczbę scalonych (usuniętych) wierzchołków.</summary>
        Public Function WeldDuplicateVertices(mesh As Mesh, epsilon As Single) As Integer
            Dim welded = VertexWelder.WeldPositions(mesh.Vertices, epsilon)
            Dim mapping = welded.mapping
            Dim mergedCount = mesh.Vertices.Count - welded.uniquePositions.Count
            If mergedCount <= 0 Then Return 0

            Dim hasN = mesh.HasNormals
            Dim hasUV = mesh.HasUVs
            Dim hasColor = mesh.HasVertexColors

            Dim newNormals As New List(Of Vector3)
            Dim newUVs As New List(Of Vector2)
            Dim newColors As New List(Of RgbColor)
            Dim seen As New HashSet(Of Integer)

            ' dla każdego nowego (ujednoliconego) indeksu zachowaj atrybuty PIERWSZEGO starego wierzchołka, który się na niego zmapował
            If hasN Then newNormals.AddRange(New Vector3(welded.uniquePositions.Count - 1) {})
            If hasUV Then newUVs.AddRange(New Vector2(welded.uniquePositions.Count - 1) {})
            If hasColor Then newColors.AddRange(New RgbColor(welded.uniquePositions.Count - 1) {})

            For oldIdx = 0 To mapping.Length - 1
                Dim newIdx = mapping(oldIdx)
                If seen.Add(newIdx) Then
                    If hasN Then newNormals(newIdx) = mesh.Normals(oldIdx)
                    If hasUV Then newUVs(newIdx) = mesh.UVs(oldIdx)
                    If hasColor Then newColors(newIdx) = mesh.VertexColors(oldIdx)
                End If
            Next

            Dim newTriangles As New List(Of Triangle)(mesh.Triangles.Count)
            For Each t In mesh.Triangles
                Dim nt As New Triangle(mapping(t.A), mapping(t.B), mapping(t.C))
                If Not nt.IsDegenerate Then newTriangles.Add(nt)
            Next

            mesh.Vertices.Clear() : mesh.Vertices.AddRange(welded.uniquePositions)
            mesh.Triangles.Clear() : mesh.Triangles.AddRange(newTriangles)
            mesh.Normals.Clear() : If hasN Then mesh.Normals.AddRange(newNormals)
            mesh.UVs.Clear() : If hasUV Then mesh.UVs.AddRange(newUVs)
            mesh.VertexColors.Clear() : If hasColor Then mesh.VertexColors.AddRange(newColors)

            Return mergedCount
        End Function

        ''' <summary>Usuwa trójkąty zdegenerowane (powtórzony indeks) i trójkąty o polu ~0. Zwraca liczbę usuniętych.</summary>
        Public Function RemoveDegenerateTriangles(mesh As Mesh, Optional minArea As Single = 0.0000000001F) As Integer
            Dim kept As New List(Of Triangle)(mesh.Triangles.Count)
            Dim removed = 0
            For Each t In mesh.Triangles
                If t.IsDegenerate Then
                    removed += 1
                    Continue For
                End If
                Dim p0 = mesh.Vertices(t.A)
                Dim p1 = mesh.Vertices(t.B)
                Dim p2 = mesh.Vertices(t.C)
                Dim area2 = Vector3.Cross(p1 - p0, p2 - p0).LengthSquared()
                If area2 <= minArea Then
                    removed += 1
                    Continue For
                End If
                kept.Add(t)
            Next
            If removed > 0 Then
                mesh.Triangles.Clear()
                mesh.Triangles.AddRange(kept)
            End If
            Return removed
        End Function

        ''' <summary>Usuwa trójkąty odwołujące się do dokładnie tego samego zestawu 3 wierzchołków (ta sama krotność, niezależnie od obrotu). Zwraca liczbę usuniętych.</summary>
        Public Function RemoveDuplicateTriangles(mesh As Mesh) As Integer
            Dim seen As New HashSet(Of (Integer, Integer, Integer))
            Dim kept As New List(Of Triangle)(mesh.Triangles.Count)
            Dim removed = 0

            For Each t In mesh.Triangles
                Dim key = CanonicalKey(t)
                If seen.Add(key) Then
                    kept.Add(t)
                Else
                    removed += 1
                End If
            Next

            If removed > 0 Then
                mesh.Triangles.Clear()
                mesh.Triangles.AddRange(kept)
            End If
            Return removed
        End Function

        Private Function CanonicalKey(t As Triangle) As (Integer, Integer, Integer)
            ' obróć trójkę tak, żeby najmniejszy indeks był pierwszy - wykrywa duplikaty niezależnie od punktu startowego obiegu
            If t.A <= t.B AndAlso t.A <= t.C Then Return (t.A, t.B, t.C)
            If t.B <= t.A AndAlso t.B <= t.C Then Return (t.B, t.C, t.A)
            Return (t.C, t.A, t.B)
        End Function

        ''' <summary>Usuwa wierzchołki, do których nie odwołuje się żaden trójkąt (typowe po wycinaniu fragmentów siatki). Zwraca liczbę usuniętych.</summary>
        Public Function RemoveUnusedVertices(mesh As Mesh) As Integer
            Dim used As New HashSet(Of Integer)
            For Each t In mesh.Triangles
                used.Add(t.A) : used.Add(t.B) : used.Add(t.C)
            Next

            If used.Count = mesh.Vertices.Count Then Return 0 ' nic do usunięcia

            Dim hasN = mesh.HasNormals
            Dim hasUV = mesh.HasUVs
            Dim hasColor = mesh.HasVertexColors

            Dim remap(mesh.Vertices.Count - 1) As Integer
            Dim newVerts As New List(Of Vector3)
            Dim newNormals As New List(Of Vector3)
            Dim newUVs As New List(Of Vector2)
            Dim newColors As New List(Of RgbColor)

            For i = 0 To mesh.Vertices.Count - 1
                If used.Contains(i) Then
                    remap(i) = newVerts.Count
                    newVerts.Add(mesh.Vertices(i))
                    If hasN Then newNormals.Add(mesh.Normals(i))
                    If hasUV Then newUVs.Add(mesh.UVs(i))
                    If hasColor Then newColors.Add(mesh.VertexColors(i))
                Else
                    remap(i) = -1
                End If
            Next

            Dim removedCount = mesh.Vertices.Count - newVerts.Count

            Dim newTriangles As New List(Of Triangle)(mesh.Triangles.Count)
            For Each t In mesh.Triangles
                newTriangles.Add(New Triangle(remap(t.A), remap(t.B), remap(t.C)))
            Next

            mesh.Vertices.Clear() : mesh.Vertices.AddRange(newVerts)
            mesh.Triangles.Clear() : mesh.Triangles.AddRange(newTriangles)
            mesh.Normals.Clear() : If hasN Then mesh.Normals.AddRange(newNormals)
            mesh.UVs.Clear() : If hasUV Then mesh.UVs.AddRange(newUVs)
            mesh.VertexColors.Clear() : If hasColor Then mesh.VertexColors.AddRange(newColors)

            Return removedCount
        End Function

    End Module

End Namespace
