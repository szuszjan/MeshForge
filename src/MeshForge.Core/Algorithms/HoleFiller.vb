Imports System.Linq
Imports System.Numerics
Imports System.Text
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>
    ''' Wypełnia otwory w siatce (typowe dla surowych skanów 3D - brakujące fragmenty tam, gdzie skaner
    ''' nie "widział" powierzchni). Każdy kontur otworu jest triangulowany metodą "ear clipping"
    ''' w płaszczyźnie najlepiej dopasowanej do konturu (metoda Newella) - działa dobrze dla otworów
    ''' o rozsądnym rozmiarze; bardzo duże kontury (prawdopodobnie zewnętrzna krawędź niedomkniętego skanu,
    ''' a nie "dziura do załatania") są domyślnie pomijane.
    ''' </summary>
    Public Module HoleFiller

        Public Function FillHoles(mesh As Mesh, Optional maxLoopSize As Integer = 2000) As OperationResult
            Dim loops = MeshTopology.FindBoundaryLoops(mesh)
            If loops.Count = 0 Then
                Return OperationResult.Ok(Loc.Translated("Siatka nie ma otworów - jest już wodoszczelna.", "The mesh has no holes - it is already watertight."))
            End If

            Dim totalNewTriangles = 0
            Dim filledCount = 0
            Dim skipped As New List(Of Integer)

            For Each loopIndices In loops
                If loopIndices.Count > maxLoopSize Then
                    skipped.Add(loopIndices.Count)
                    Continue For
                End If

                Dim newTris = TriangulateLoop(mesh, loopIndices)
                mesh.Triangles.AddRange(newTris)
                totalNewTriangles += newTris.Count
                filledCount += 1
            Next

            If totalNewTriangles > 0 Then mesh.ClearNormals() ' stare normalne są teraz nieaktualne przy łatach - przelicz ponownie

            Dim details As New StringBuilder()
            details.AppendLine(Loc.Translated(
                $"Znaleziono konturów otworów: {loops.Count}, wypełniono: {filledCount}, dodano trójkątów: {totalNewTriangles}.",
                $"Hole contours found: {loops.Count}, filled: {filledCount}, triangles added: {totalNewTriangles}."))
            If skipped.Count > 0 Then
                details.AppendLine(Loc.Translated(
                    $"Pominięto {skipped.Count} bardzo dużych konturów (> {maxLoopSize} wierzchołków) - to zwykle zewnętrzna krawędź niedomkniętego skanu, a nie dziura do załatania.",
                    $"Skipped {skipped.Count} very large contours (> {maxLoopSize} vertices) - this is usually the outer edge of an unclosed scan, not a hole to patch."))
            End If
            If totalNewTriangles > 0 Then details.AppendLine(Loc.Translated("Zalecane: przelicz normalne (funkcja 'Przelicz normalne').", "Recommended: recalculate normals ('Recalculate normals' function)."))

            Return OperationResult.Ok(
                Loc.Translated($"Wypełniono {filledCount} z {loops.Count} otworów (+{totalNewTriangles} trójkątów).", $"Filled {filledCount} of {loops.Count} holes (+{totalNewTriangles} triangles)."),
                details.ToString())
        End Function

        ''' <summary>
        ''' Trianguluje pojedynczy kontur otworu. Kontur wejściowy (z FindBoundaryLoops) ma nawinięcie
        ''' odwrotne do brakującej "łaty" (bo to nawinięcie sąsiadów, nie usuniętej ściany) - dlatego
        ''' od razu go odwracamy, żeby nowe trójkąty miały normalne zgodne z resztą siatki.
        ''' </summary>
        Private Function TriangulateLoop(mesh As Mesh, rawLoop As List(Of Integer)) As List(Of Triangle)
            Dim ordered = rawLoop.AsEnumerable().Reverse().ToList()
            Dim n = ordered.Count
            Dim result As New List(Of Triangle)
            If n < 3 Then Return result

            If n = 3 Then
                result.Add(New Triangle(ordered(0), ordered(1), ordered(2)))
                Return result
            End If

            ' dopasowanie płaszczyzny metodą Newella - odporne na lekko niepłaskie/zaszumione kontury
            Dim normal As Vector3 = Vector3.Zero
            For i = 0 To n - 1
                Dim cur = mesh.Vertices(ordered(i))
                Dim nxt = mesh.Vertices(ordered((i + 1) Mod n))
                normal += New Vector3(
                    (cur.Y - nxt.Y) * (cur.Z + nxt.Z),
                    (cur.Z - nxt.Z) * (cur.X + nxt.X),
                    (cur.X - nxt.X) * (cur.Y + nxt.Y))
            Next
            If normal.LengthSquared() < 0.0000000001F Then
                normal = Vector3.UnitZ ' kontur zdegenerowany/współliniowy - fallback wachlarzowy poniżej i tak sobie poradzi
            Else
                normal = Vector3.Normalize(normal)
            End If

            Dim tangentU = If(Math.Abs(normal.X) < 0.9F,
                               Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitX)),
                               Vector3.Normalize(Vector3.Cross(normal, Vector3.UnitY)))
            Dim tangentV = Vector3.Cross(normal, tangentU)

            Dim origin = mesh.Vertices(ordered(0))
            Dim pts2D(n - 1) As Vector2
            For i = 0 To n - 1
                Dim rel = mesh.Vertices(ordered(i)) - origin
                pts2D(i) = New Vector2(Vector3.Dot(rel, tangentU), Vector3.Dot(rel, tangentV))
            Next

            Dim signedArea As Single = 0
            For i = 0 To n - 1
                Dim a = pts2D(i)
                Dim b = pts2D((i + 1) Mod n)
                signedArea += a.X * b.Y - b.X * a.Y
            Next
            Dim positiveWinding = signedArea >= 0

            Dim remaining As New List(Of Integer)
            For i = 0 To n - 1
                remaining.Add(i)
            Next

            Dim guard = 0
            Dim maxIterations = n * n + 16 ' zabezpieczenie przed nieskończoną pętlą na samoprzecinających się konturach
            While remaining.Count > 3 AndAlso guard < maxIterations
                guard += 1
                Dim earFound = False

                For ri = 0 To remaining.Count - 1
                    Dim prevRi = remaining((ri - 1 + remaining.Count) Mod remaining.Count)
                    Dim curRi = remaining(ri)
                    Dim nextRi = remaining((ri + 1) Mod remaining.Count)

                    Dim a2 = pts2D(prevRi)
                    Dim b2 = pts2D(curRi)
                    Dim c2 = pts2D(nextRi)

                    Dim crossVal = (b2.X - a2.X) * (c2.Y - a2.Y) - (b2.Y - a2.Y) * (c2.X - a2.X)
                    Dim isConvex = If(positiveWinding, crossVal >= 0, crossVal <= 0)
                    If Not isConvex Then Continue For

                    Dim anyInside = False
                    For Each otherRi In remaining
                        If otherRi = prevRi OrElse otherRi = curRi OrElse otherRi = nextRi Then Continue For
                        If PointInTriangle(pts2D(otherRi), a2, b2, c2) Then
                            anyInside = True
                            Exit For
                        End If
                    Next
                    If anyInside Then Continue For

                    result.Add(New Triangle(ordered(prevRi), ordered(curRi), ordered(nextRi)))
                    remaining.RemoveAt(ri)
                    earFound = True
                    Exit For
                Next

                If Not earFound Then Exit While ' kontur zbyt trudny (np. samoprzecinający) - dokończ wachlarzem poniżej
            End While

            If remaining.Count >= 3 Then
                For i = 1 To remaining.Count - 2
                    result.Add(New Triangle(ordered(remaining(0)), ordered(remaining(i)), ordered(remaining(i + 1))))
                Next
            End If

            Return result
        End Function

        Private Function PointInTriangle(p As Vector2, a As Vector2, b As Vector2, c As Vector2) As Boolean
            Dim d1 = Cross2(p, a, b)
            Dim d2 = Cross2(p, b, c)
            Dim d3 = Cross2(p, c, a)
            Dim hasNeg = (d1 < 0) OrElse (d2 < 0) OrElse (d3 < 0)
            Dim hasPos = (d1 > 0) OrElse (d2 > 0) OrElse (d3 > 0)
            Return Not (hasNeg AndAlso hasPos)
        End Function

        Private Function Cross2(p As Vector2, a As Vector2, b As Vector2) As Single
            Return (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)
        End Function

    End Module

End Namespace
