Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>
    ''' Teselacja SUROWEJ chmury punktów (bez trójkątów) - zamienia punkty skanera w siatkę trójkątów.
    ''' Metoda: dopasowanie płaszczyzny (PCA) + triangulacja Delaunaya 2D (Bowyer-Watson) na rzucie punktów.
    ''' To celowo prosta metoda "2,5D": świetnie radzi sobie z pojedynczym ujęciem skanera (powierzchnia
    ''' widoczna mniej więcej z jednej strony, jak mapa wysokości). Pełna rekonstrukcja zamkniętej bryły
    ''' z chmury punktów 360° (algorytmy typu Poisson/ball-pivoting) wymaga zupełnie innego, znacznie
    ''' bardziej złożonego podejścia i nie jest tu zaimplementowana - zostaw to zadanie oprogramowaniu
    ''' skanera albo wczytaj model już zteselowany (OBJ/STL/PLY).
    ''' </summary>
    Public Module PointCloudTriangulator

        Public Function Triangulate(mesh As Mesh) As OperationResult
            If mesh.Triangles.Count > 0 Then
                Return OperationResult.Fail(
                    Loc.Translated("Siatka ma już trójkąty.", "The mesh already has triangles."),
                    Loc.Translated("Ta funkcja jest do teselacji SUROWEJ chmury punktów. Żeby zmienić rozdzielczość istniejącej siatki, użyj 'Zagęść' albo 'Uprość'.",
                          "This function is for tessellating a RAW point cloud. To change the resolution of an existing mesh, use 'Subdivide' or 'Simplify'."))
            End If
            If mesh.VertexCount < 3 Then Return OperationResult.Fail(Loc.Translated("Za mało punktów do triangulacji (potrzeba co najmniej 3).", "Not enough points to triangulate (at least 3 needed)."))
            If mesh.VertexCount > 15000 Then
                Return OperationResult.Fail(
                    Loc.Translated($"Chmura ma {mesh.VertexCount:N0} punktów - to więcej niż bezpieczny limit tej prostej metody (15 000).",
                          $"The cloud has {mesh.VertexCount:N0} points - more than this simple method's safe limit (15,000)."),
                    Loc.Translated("Ten algorytm nie używa struktury przyspieszającej (złożoność O(n²)) i jest pomyślany do pojedynczego ujęcia skanu, " &
                          "nie do pełnej chmury 360°. Zmniejsz liczbę punktów (np. przez przetworzenie w mniejszych partiach) albo wczytaj skan już zteselowany.",
                          "This algorithm has no acceleration structure (O(n²) complexity) and is meant for a single scan pass, " &
                          "not a full 360° cloud. Reduce the point count (e.g. process in smaller batches) or load an already-tessellated scan."))
            End If

            Dim n = mesh.VertexCount

            ' 1) PCA metodą potęgową - znajdź płaszczyznę najlepszego dopasowania (dwie dominujące osie + normalna)
            Dim centroid As Vector3 = Vector3.Zero
            For Each p In mesh.Vertices
                centroid += p
            Next
            centroid /= n

            Dim cov(2, 2) As Double
            For Each p In mesh.Vertices
                Dim r = p - centroid
                cov(0, 0) += CDbl(r.X) * r.X : cov(0, 1) += CDbl(r.X) * r.Y : cov(0, 2) += CDbl(r.X) * r.Z
                cov(1, 0) += CDbl(r.Y) * r.X : cov(1, 1) += CDbl(r.Y) * r.Y : cov(1, 2) += CDbl(r.Y) * r.Z
                cov(2, 0) += CDbl(r.Z) * r.X : cov(2, 1) += CDbl(r.Z) * r.Y : cov(2, 2) += CDbl(r.Z) * r.Z
            Next

            Dim e1 = DominantEigenvector(cov)
            Dim lambda1 = QuadForm(cov, e1)
            Dim deflated = Deflate(cov, e1, lambda1)
            Dim e2raw = DominantEigenvector(deflated)
            Dim e2 = e2raw - e1 * Vector3.Dot(e2raw, e1) ' Gram-Schmidt względem e1 (na wypadek niedoskonałej deflacji)
            If e2.LengthSquared() < 0.0000001F Then e2 = Vector3.Cross(e1, Vector3.UnitY) ' skrajny przypadek zdegenerowany
            e2 = Vector3.Normalize(e2)
            Dim normal = Vector3.Normalize(Vector3.Cross(e1, e2))

            ' 2) rzut punktów na 2D w tej płaszczyźnie
            Dim pts2D(n - 1) As Vector2
            For i = 0 To n - 1
                Dim r = mesh.Vertices(i) - centroid
                pts2D(i) = New Vector2(Vector3.Dot(r, e1), Vector3.Dot(r, e2))
            Next

            ' 3) triangulacja Delaunaya 2D
            Dim triIndices = BowyerWatson(pts2D)

            ' 4) ujednolić orientację wynikowych trójkątów względem dopasowanej normalnej
            Dim finalTriangles As New List(Of Triangle)(triIndices.Count)
            For Each tri In triIndices
                Dim p0 = mesh.Vertices(tri.Item1)
                Dim p1 = mesh.Vertices(tri.Item2)
                Dim p2 = mesh.Vertices(tri.Item3)
                Dim faceNormal = Vector3.Cross(p1 - p0, p2 - p0)
                If Vector3.Dot(faceNormal, normal) < 0 Then
                    finalTriangles.Add(New Triangle(tri.Item1, tri.Item3, tri.Item2))
                Else
                    finalTriangles.Add(New Triangle(tri.Item1, tri.Item2, tri.Item3))
                End If
            Next

            mesh.Triangles.Clear()
            mesh.Triangles.AddRange(finalTriangles)
            mesh.ClearNormals()

            Return OperationResult.Ok(
                Loc.Translated($"Zteselowano chmurę {n:N0} punktów: utworzono {finalTriangles.Count:N0} trójkątów.",
                      $"Tessellated a cloud of {n:N0} points: created {finalTriangles.Count:N0} triangles."),
                Loc.Translated("Metoda: triangulacja Delaunaya 2,5D (rzut na dopasowaną płaszczyznę). Dobrze działa dla pojedynczego ujęcia skanu; " &
                      "dla obiektów skanowanych z każdej strony może dać błędne trójkąty tam, gdzie rzut się nakłada.",
                      "Method: 2.5D Delaunay triangulation (projection onto a fitted plane). Works well for a single scan pass; " &
                      "for objects scanned from every side it may produce incorrect triangles where the projection overlaps."))
        End Function

#Region "Bowyer-Watson (Delaunay 2D)"

        Private Function BowyerWatson(points As Vector2()) As List(Of (Integer, Integer, Integer))
            Dim n = points.Length
            Dim minX = points(0).X, maxX = points(0).X
            Dim minY = points(0).Y, maxY = points(0).Y
            For Each p In points
                minX = Math.Min(minX, p.X) : maxX = Math.Max(maxX, p.X)
                minY = Math.Min(minY, p.Y) : maxY = Math.Max(maxY, p.Y)
            Next
            Dim dx = maxX - minX
            Dim dy = maxY - minY
            Dim dmax = Math.Max(Math.Max(dx, dy), 1.0F)
            Dim midX = (minX + maxX) * 0.5F
            Dim midY = (minY + maxY) * 0.5F

            ' Mnożnik "2" (nie więcej!) to sprawdzona, klasyczna formuła (Bourke) na super-trójkąt otaczający
            ' wszystkie punkty. Zbyt duży super-trójkąt psuje algorytm: jego okręgi opisane stają się patologicznie
            ' wielkie i przez wiele iteracji "połykają" już wstawione punkty zamiast się ustabilizować, co prowadzi
            ' do utraty większości punktów z finalnej triangulacji.
            Dim allPts As New List(Of Vector2)(points)
            Dim superA = allPts.Count : allPts.Add(New Vector2(midX - 2F * dmax, midY - dmax))
            Dim superB = allPts.Count : allPts.Add(New Vector2(midX, midY + 2F * dmax))
            Dim superC = allPts.Count : allPts.Add(New Vector2(midX + 2F * dmax, midY - dmax))

            Dim tris As New List(Of (Integer, Integer, Integer))
            tris.Add(MakeCcw(allPts, superA, superB, superC))

            For i = 0 To n - 1
                Dim p = allPts(i)
                Dim bad As New List(Of Integer)
                For ti = 0 To tris.Count - 1
                    Dim t = tris(ti)
                    If InCircumcircle(allPts(t.Item1), allPts(t.Item2), allPts(t.Item3), p) Then
                        bad.Add(ti)
                    End If
                Next
                If bad.Count = 0 Then Continue For ' punkt praktycznie zduplikowany z istniejącym - pomiń

                Dim boundary As New List(Of (Integer, Integer))
                For Each ti In bad
                    Dim t = tris(ti)
                    Dim edges = {(t.Item1, t.Item2), (t.Item2, t.Item3), (t.Item3, t.Item1)}
                    For Each e In edges
                        Dim isShared = False
                        For Each tj In bad
                            If tj = ti Then Continue For
                            If HasDirectedEdge(tris(tj), e.Item2, e.Item1) Then
                                isShared = True
                                Exit For
                            End If
                        Next
                        If Not isShared Then boundary.Add(e)
                    Next
                Next

                bad.Sort()
                For k = bad.Count - 1 To 0 Step -1
                    tris.RemoveAt(bad(k))
                Next

                For Each e In boundary
                    tris.Add((e.Item1, e.Item2, i))
                Next
            Next

            Dim result As New List(Of (Integer, Integer, Integer))
            For Each t In tris
                If t.Item1 < n AndAlso t.Item2 < n AndAlso t.Item3 < n Then result.Add(t)
            Next
            Return result
        End Function

        Private Function MakeCcw(pts As List(Of Vector2), a As Integer, b As Integer, c As Integer) As (Integer, Integer, Integer)
            If SignedArea(pts(a), pts(b), pts(c)) < 0 Then Return (a, c, b)
            Return (a, b, c)
        End Function

        Private Function SignedArea(a As Vector2, b As Vector2, c As Vector2) As Single
            Return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)
        End Function

        Private Function HasDirectedEdge(t As (Integer, Integer, Integer), u As Integer, v As Integer) As Boolean
            Return (t.Item1 = u AndAlso t.Item2 = v) OrElse (t.Item2 = u AndAlso t.Item3 = v) OrElse (t.Item3 = u AndAlso t.Item1 = v)
        End Function

        ''' <summary>Test Delaunaya: czy p leży wewnątrz okręgu opisanego na trójkącie (a,b,c). Zakłada, że trójkąt zostanie ujednolicony do CCW wewnątrz funkcji.</summary>
        Private Function InCircumcircle(a As Vector2, b As Vector2, c As Vector2, p As Vector2) As Boolean
            Dim bb = b : Dim cc = c
            If SignedArea(a, b, c) < 0 Then
                bb = c : cc = b
            End If
            Dim adx = CDbl(a.X - p.X) : Dim ady = CDbl(a.Y - p.Y)
            Dim bdx = CDbl(bb.X - p.X) : Dim bdy = CDbl(bb.Y - p.Y)
            Dim cdx = CDbl(cc.X - p.X) : Dim cdy = CDbl(cc.Y - p.Y)
            Dim abdet = adx * bdy - bdx * ady
            Dim bcdet = bdx * cdy - cdx * bdy
            Dim cadet = cdx * ady - adx * cdy
            Dim alift = adx * adx + ady * ady
            Dim blift = bdx * bdx + bdy * bdy
            Dim clift = cdx * cdx + cdy * cdy
            Dim det = alift * bcdet + blift * cadet + clift * abdet
            Return det > 0
        End Function

#End Region

#Region "PCA - pomocnicze (metoda potęgowa)"

        ''' <summary>
        ''' Metoda potęgowa z kilkoma wektorami startowymi. Jeden ustalony start (np. (1,1,1)) potrafi
        ''' trafić DOKŁADNIE prostopadle do prawdziwego dominującego kierunku dla symetrycznych układów
        ''' punktów (np. punktów leżących na osiach) - wtedy iteracja zbiega do złego (słabszego) wektora
        ''' własnego. Próbujemy kilku niewspółliniowych startów i zostawiamy ten o największym ilorazie
        ''' Rayleigha (macierz kowariancji jest półokreślona dodatnio, więc większa wartość = lepszy wynik).
        ''' </summary>
        Private Function DominantEigenvector(m As Double(,)) As Vector3
            Dim seeds() As Vector3 = {
                New Vector3(1, 1, 1),
                New Vector3(1, -1, 0.5F),
                New Vector3(-0.5F, 1, -1)
            }

            Dim best = Vector3.UnitX
            Dim bestVal As Double = Double.NegativeInfinity

            For Each seed In seeds
                Dim v = Vector3.Normalize(seed)
                For iter = 1 To 50
                    Dim nv = MatVec(m, v)
                    If nv.LengthSquared() < 0.0000000001F Then Exit For ' macierz ~zerowa (np. wszystkie punkty identyczne)
                    v = Vector3.Normalize(nv)
                Next
                Dim val = QuadForm(m, v)
                If val > bestVal Then
                    bestVal = val
                    best = v
                End If
            Next

            Return best
        End Function

        Private Function MatVec(m As Double(,), v As Vector3) As Vector3
            Return New Vector3(
                CSng(m(0, 0) * v.X + m(0, 1) * v.Y + m(0, 2) * v.Z),
                CSng(m(1, 0) * v.X + m(1, 1) * v.Y + m(1, 2) * v.Z),
                CSng(m(2, 0) * v.X + m(2, 1) * v.Y + m(2, 2) * v.Z))
        End Function

        Private Function QuadForm(m As Double(,), v As Vector3) As Double
            Return Vector3.Dot(MatVec(m, v), v)
        End Function

        Private Function Deflate(m As Double(,), e As Vector3, lambda As Double) As Double(,)
            Dim r(2, 2) As Double
            Dim ev() As Single = {e.X, e.Y, e.Z}
            For i = 0 To 2
                For j = 0 To 2
                    r(i, j) = m(i, j) - lambda * ev(i) * ev(j)
                Next
            Next
            Return r
        End Function

#End Region

    End Module

End Namespace
