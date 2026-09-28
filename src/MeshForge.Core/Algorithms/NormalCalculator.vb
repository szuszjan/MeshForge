Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>Liczy normalne trójkątów i uśrednione (ważone polem powierzchni) normalne wierzchołków.</summary>
    Public Module NormalCalculator

        Public Function Recalculate(mesh As Mesh) As OperationResult
            If mesh.Vertices.Count = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "The mesh is empty."))

            Dim accum As New Vector3(0, 0, 0)
            Dim normals(mesh.Vertices.Count - 1) As Vector3
            For i = 0 To normals.Length - 1
                normals(i) = Vector3.Zero
            Next

            For Each t In mesh.Triangles
                Dim p0 = mesh.Vertices(t.A)
                Dim p1 = mesh.Vertices(t.B)
                Dim p2 = mesh.Vertices(t.C)
                ' długość iloczynu wektorowego = 2x pole trójkąta, więc sumowanie tego wektora
                ' automatycznie waży wkład każdego trójkąta jego polem powierzchni
                Dim faceNormalWeighted = Vector3.Cross(p1 - p0, p2 - p0)

                normals(t.A) += faceNormalWeighted
                normals(t.B) += faceNormalWeighted
                normals(t.C) += faceNormalWeighted
            Next

            mesh.Normals.Clear()
            Dim degenerateCount = 0
            For i = 0 To normals.Length - 1
                If normals(i).LengthSquared() > 0.0000000001F Then
                    mesh.Normals.Add(Vector3.Normalize(normals(i)))
                Else
                    mesh.Normals.Add(Vector3.UnitY) ' izolowany wierzchołek bez trójkątów - normalna umowna
                    degenerateCount += 1
                End If
            Next

            Dim details = If(degenerateCount > 0, Loc.Translated($"Uwaga: {degenerateCount} wierzchołków bez sąsiadujących trójkątów (przypisano normalną domyślną).", $"Note: {degenerateCount} vertices have no adjacent triangles (assigned a default normal)."), "")
            Return OperationResult.Ok(Loc.Translated($"Przeliczono normalne dla {mesh.Vertices.Count:N0} wierzchołków.", $"Recalculated normals for {mesh.Vertices.Count:N0} vertices."), details)
        End Function

    End Module

End Namespace
