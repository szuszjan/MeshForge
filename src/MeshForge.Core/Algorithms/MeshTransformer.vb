Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>Podstawowe przekształcenia geometryczne całej siatki: przesunięcie, skalowanie, wyśrodkowanie, odwrócenie normalnych.</summary>
    Public Module MeshTransformer

        Public Function Translate(mesh As Mesh, offset As Vector3) As OperationResult
            For i = 0 To mesh.Vertices.Count - 1
                mesh.Vertices(i) += offset
            Next
            Return OperationResult.Ok(Loc.Translated($"Przesunięto siatkę o ({offset.X:F3}, {offset.Y:F3}, {offset.Z:F3}).", $"Translated mesh by ({offset.X:F3}, {offset.Y:F3}, {offset.Z:F3})."))
        End Function

        ''' <summary>Przesuwa siatkę tak, żeby środek jej bounding boxa znalazł się w punkcie (0,0,0).</summary>
        Public Function Center(mesh As Mesh) As OperationResult
            If mesh.VertexCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "The mesh is empty."))
            Dim bbox = mesh.GetBoundingBox()
            Dim bboxCenter = (bbox.min + bbox.max) * 0.5F
            Translate(mesh, -bboxCenter)
            Return OperationResult.Ok(Loc.Translated($"Wyśrodkowano siatkę (przesunięto o {(-bboxCenter).X:F3}, {(-bboxCenter).Y:F3}, {(-bboxCenter).Z:F3}).",
                                             $"Centered mesh (translated by {(-bboxCenter).X:F3}, {(-bboxCenter).Y:F3}, {(-bboxCenter).Z:F3})."))
        End Function

        ''' <summary>Skaluje jednorodnie wokół środka bounding boxa siatki (nie wokół (0,0,0), żeby model "nie uciekł" w przestrzeni).</summary>
        Public Function Scale(mesh As Mesh, factor As Single) As OperationResult
            If mesh.VertexCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "The mesh is empty."))
            If factor <= 0 Then Return OperationResult.Fail(Loc.Translated("Współczynnik skali musi być dodatni.", "The scale factor must be positive."))

            Dim bbox = mesh.GetBoundingBox()
            Dim center = (bbox.min + bbox.max) * 0.5F
            For i = 0 To mesh.Vertices.Count - 1
                mesh.Vertices(i) = center + (mesh.Vertices(i) - center) * factor
            Next
            Return OperationResult.Ok(Loc.Translated($"Przeskalowano siatkę ×{factor:F3}.", $"Scaled mesh ×{factor:F3}."))
        End Function

        ''' <summary>Wyśrodkowuje i skaluje tak, żeby największy wymiar bounding boxa był równy targetSize.</summary>
        Public Function NormalizeToSize(mesh As Mesh, Optional targetSize As Single = 1.0F) As OperationResult
            If mesh.VertexCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "The mesh is empty."))
            Dim bbox = mesh.GetBoundingBox()
            Dim size = bbox.max - bbox.min
            Dim largest = Math.Max(size.X, Math.Max(size.Y, size.Z))
            If largest <= 0.0000001F Then Return OperationResult.Fail(Loc.Translated("Siatka ma zerowy rozmiar - nie można znormalizować.", "The mesh has zero size - cannot normalize."))

            Center(mesh)
            Dim factor = targetSize / largest
            Scale(mesh, factor)
            Return OperationResult.Ok(Loc.Translated($"Znormalizowano siatkę do rozmiaru {targetSize:F3} (skala ×{factor:F4}).", $"Normalized mesh to size {targetSize:F3} (scale ×{factor:F4})."))
        End Function

        ''' <summary>Odwraca nawinięcie wszystkich trójkątów i (jeśli są) znak normalnych - naprawia siatkę "wywróconą na lewą stronę".</summary>
        Public Function FlipNormals(mesh As Mesh) As OperationResult
            For i = 0 To mesh.Triangles.Count - 1
                mesh.Triangles(i) = mesh.Triangles(i).Flipped()
            Next
            If mesh.HasNormals Then
                For i = 0 To mesh.Normals.Count - 1
                    mesh.Normals(i) = -mesh.Normals(i)
                Next
            End If
            Return OperationResult.Ok(Loc.Translated("Odwrócono nawinięcie trójkątów i normalne.", "Flipped triangle winding and normals."))
        End Function

        ''' <summary>Dolącza drugą siatkę do pierwszej (np. dwa oddzielne skany) - proste złączenie bez wyrównywania (ICP nie jest jeszcze zaimplementowane).</summary>
        Public Function Append(target As Mesh, other As Mesh) As OperationResult
            Dim offset = target.Vertices.Count
            target.Vertices.AddRange(other.Vertices)

            For Each t In other.Triangles
                target.Triangles.Add(New Triangle(t.A + offset, t.B + offset, t.C + offset))
            Next

            ' atrybuty dołączamy tylko, jeśli OBIE siatki je mają - inaczej wyczyść, żeby uniknąć niespójnej długości list
            If target.HasNormals AndAlso other.HasNormals Then
                target.Normals.AddRange(other.Normals)
            Else
                target.Normals.Clear()
            End If

            If target.HasUVs AndAlso other.HasUVs Then
                target.UVs.AddRange(other.UVs)
            Else
                target.UVs.Clear()
            End If

            If target.HasVertexColors AndAlso other.HasVertexColors Then
                target.VertexColors.AddRange(other.VertexColors)
            Else
                target.VertexColors.Clear()
            End If

            Return OperationResult.Ok(
                Loc.Translated($"Dołączono siatkę '{other.Name}' ({other.VertexCount:N0} wierzchołków, {other.TriangleCount:N0} trójkątów).",
                      $"Appended mesh '{other.Name}' ({other.VertexCount:N0} vertices, {other.TriangleCount:N0} triangles)."),
                Loc.Translated("Uwaga: to proste złączenie bez automatycznego wyrównania (rejestracji/ICP) - jeśli skany nie są już w tym samym układzie współrzędnych, ustaw je ręcznie przed połączeniem.",
                      "Note: this is a simple merge with no automatic alignment (registration/ICP) - if the scans are not already in the same coordinate system, align them manually before merging."))
        End Function

    End Module

End Namespace
