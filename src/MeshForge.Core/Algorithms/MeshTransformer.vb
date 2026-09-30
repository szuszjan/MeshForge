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

        ''' <summary>Obraca siatkę wokół środka jej bounding boxa o podany kąt (stopnie) wokół danej osi - obraca też normalne (bez przesunięcia).</summary>
        Public Function Rotate(mesh As Mesh, axis As Vector3, degrees As Single) As OperationResult
            If mesh.VertexCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "The mesh is empty."))
            If axis.LengthSquared() <= 0.0000001F Then Return OperationResult.Fail(Loc.Translated("Oś obrotu nie może być zerowa.", "The rotation axis cannot be zero."))

            Dim bbox = mesh.GetBoundingBox()
            Dim center = (bbox.min + bbox.max) * 0.5F
            Dim q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), degrees * MathF.PI / 180.0F)

            For i = 0 To mesh.Vertices.Count - 1
                mesh.Vertices(i) = center + Vector3.Transform(mesh.Vertices(i) - center, q)
            Next
            If mesh.HasNormals Then
                For i = 0 To mesh.Normals.Count - 1
                    mesh.Normals(i) = Vector3.Transform(mesh.Normals(i), q)
                Next
            End If

            Return OperationResult.Ok(Loc.Translated($"Obrócono siatkę o {degrees:F0}° wokół osi ({axis.X:F0}, {axis.Y:F0}, {axis.Z:F0}).",
                                             $"Rotated mesh {degrees:F0}° around axis ({axis.X:F0}, {axis.Y:F0}, {axis.Z:F0})."))
        End Function

        ''' <summary>Usuwa wskazane trójkąty (po indeksie) i sprząta osierocone wierzchołki (MeshCleaner.RemoveUnusedVertices) - używane przez narzędzie zaznaczania lasso.</summary>
        Public Function DeleteTriangles(mesh As Mesh, triangleIndices As IEnumerable(Of Integer)) As OperationResult
            Dim toRemove As New HashSet(Of Integer)(triangleIndices)
            If toRemove.Count = 0 Then Return OperationResult.Fail(Loc.Translated("Nie zaznaczono żadnych trójkątów do usunięcia.", "No triangles were selected for deletion."))

            Dim kept As New List(Of Triangle)(Math.Max(mesh.Triangles.Count - toRemove.Count, 0))
            For i = 0 To mesh.Triangles.Count - 1
                If Not toRemove.Contains(i) Then kept.Add(mesh.Triangles(i))
            Next
            Dim removedTriCount = mesh.Triangles.Count - kept.Count

            mesh.Triangles.Clear()
            mesh.Triangles.AddRange(kept)
            Dim removedVertCount = MeshCleaner.RemoveUnusedVertices(mesh)

            Return OperationResult.Ok(Loc.Translated($"Usunięto {removedTriCount:N0} trójkątów (i {removedVertCount:N0} osieroconych wierzchołków).",
                                             $"Deleted {removedTriCount:N0} triangles (and {removedVertCount:N0} orphaned vertices)."))
        End Function

        ''' <summary>
        ''' Wysuwa jeden trójkąt na zewnątrz wzdłuż jego normalnej o zadaną odległość, tworząc mały graniastosłup:
        ''' najpierw duplikuje 3 wierzchołki ściany (żeby wysunięcie nie pociągnęło sąsiednich trójkątów, które
        ''' mogły współdzielić te same wierzchołki), potem podnosi duplikaty jako nowy "daszek" i dokłada
        ''' 3 ściany boczne. Czyści normalne - topologia się zmieniła, trzeba przeliczyć (RunOperation robi to
        ''' automatycznie, jeśli AutoRecalcNormals jest włączone).
        ''' </summary>
        Public Function ExtrudeTriangle(mesh As Mesh, triangleIndex As Integer, distance As Single) As OperationResult
            If triangleIndex < 0 OrElse triangleIndex >= mesh.Triangles.Count Then Return OperationResult.Fail(Loc.Translated("Nieprawidłowy trójkąt.", "Invalid triangle."))
            If Math.Abs(distance) <= 0.0000001F Then Return OperationResult.Fail(Loc.Translated("Odległość wysunięcia musi być różna od zera.", "The extrude distance must be non-zero."))

            Dim tri = mesh.Triangles(triangleIndex)
            Dim pA = mesh.Vertices(tri.A)
            Dim pB = mesh.Vertices(tri.B)
            Dim pC = mesh.Vertices(tri.C)
            Dim normal = Vector3.Cross(pB - pA, pC - pA)
            If normal.LengthSquared() <= 0.0000001F Then Return OperationResult.Fail(Loc.Translated("Trójkąt jest zdegenerowany (zerowe pole).", "The triangle is degenerate (zero area)."))
            normal = Vector3.Normalize(normal)
            Dim offset = normal * distance

            Dim hasN = mesh.HasNormals
            Dim hasU = mesh.HasUVs
            Dim hasC = mesh.HasVertexColors

            ' podstawa - duplikaty W ORYGINALNEJ pozycji, odrywają ścianę od ewentualnych sąsiadów
            Dim baseA = DuplicateVertex(mesh, tri.A, hasN, hasU, hasC)
            Dim baseB = DuplicateVertex(mesh, tri.B, hasN, hasU, hasC)
            Dim baseC = DuplicateVertex(mesh, tri.C, hasN, hasU, hasC)

            ' daszek - kolejne duplikaty, podniesione o offset
            Dim capA = DuplicateVertex(mesh, tri.A, hasN, hasU, hasC)
            Dim capB = DuplicateVertex(mesh, tri.B, hasN, hasU, hasC)
            Dim capC = DuplicateVertex(mesh, tri.C, hasN, hasU, hasC)
            mesh.Vertices(capA) += offset
            mesh.Vertices(capB) += offset
            mesh.Vertices(capC) += offset

            ' oryginalny trójkąt staje się daszkiem (to samo nawinięcie = ta sama strona widoczna)
            mesh.Triangles(triangleIndex) = New Triangle(capA, capB, capC)

            ' ściany boczne, nawinięcie na zewnątrz graniastosłupa
            mesh.Triangles.Add(New Triangle(baseA, baseB, capB))
            mesh.Triangles.Add(New Triangle(baseA, capB, capA))
            mesh.Triangles.Add(New Triangle(baseB, baseC, capC))
            mesh.Triangles.Add(New Triangle(baseB, capC, capB))
            mesh.Triangles.Add(New Triangle(baseC, baseA, capA))
            mesh.Triangles.Add(New Triangle(baseC, capA, capC))

            mesh.ClearNormals()

            Return OperationResult.Ok(Loc.Translated($"Wysunięto trójkąt o {distance:F3}.", $"Extruded triangle by {distance:F3}."))
        End Function

        Private Function DuplicateVertex(mesh As Mesh, sourceIndex As Integer, hasNormal As Boolean, hasUV As Boolean, hasColor As Boolean) As Integer
            mesh.Vertices.Add(mesh.Vertices(sourceIndex))
            If hasNormal Then mesh.Normals.Add(mesh.Normals(sourceIndex))
            If hasUV Then mesh.UVs.Add(mesh.UVs(sourceIndex))
            If hasColor Then mesh.VertexColors.Add(mesh.VertexColors(sourceIndex))
            Return mesh.Vertices.Count - 1
        End Function

    End Module

End Namespace
