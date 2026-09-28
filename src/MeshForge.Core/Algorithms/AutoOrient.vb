Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>Jedna wykryta "płaska powierzchnia" - łata sąsiadujących trójkątów otoczki wypukłej o (prawie) tej samej normalnej, z łącznym polem i trójkątami do podświetlenia w podglądzie 3D.</summary>
    Public Class FlatCandidate
        Public Property Normal As Vector3
        Public Property Area As Double
        Public Property AreaFraction As Double

        ''' <summary>Środek ciężkości łaty (ważony polem) - we współrzędnych siatki PRZED ewentualnym obrotem.</summary>
        Public Property Centroid As Vector3

        ''' <summary>Trójkąty łaty (współrzędne wierzchołków, nie indeksy) - używane do podświetlenia tej powierzchni w podglądzie 3D wyboru podstawy.</summary>
        Public Property Triangles As New List(Of (A As Vector3, B As Vector3, C As Vector3))

        ''' <summary>Przyjazny, czytelny dla człowieka opis kierunku normalnej (np. "głównie w dół (-Y)").</summary>
        Public ReadOnly Property Description As String
            Get
                Dim ax = Math.Abs(Normal.X) : Dim ay = Math.Abs(Normal.Y) : Dim az = Math.Abs(Normal.Z)
                If ay >= ax AndAlso ay >= az Then
                    Return If(Normal.Y >= 0, Loc.Translated("głównie w górę (+Y)", "mostly upward (+Y)"), Loc.Translated("głównie w dół (-Y)", "mostly downward (-Y)"))
                ElseIf ax >= ay AndAlso ax >= az Then
                    Return If(Normal.X >= 0, Loc.Translated("głównie w prawo (+X)", "mostly right (+X)"), Loc.Translated("głównie w lewo (-X)", "mostly left (-X)"))
                Else
                    Return If(Normal.Z >= 0, Loc.Translated("głównie do przodu (+Z)", "mostly forward (+Z)"), Loc.Translated("głównie do tyłu (-Z)", "mostly backward (-Z)"))
                End If
            End Get
        End Property

        Public Overrides Function ToString() As String
            Return Loc.Translated($"~{AreaFraction * 100:F0}% powierzchni — {Description}", $"~{AreaFraction * 100:F0}% of surface — {Description}")
        End Function
    End Class

    ''' <summary>
    ''' Wykrywa największe płaskie powierzchnie siatki i pozwala obrócić model tak, żeby wybrana
    ''' powierzchnia stała się poziomą podstawą (spoczywała na "podłodze", normalna skierowana w dół).
    ''' Typowe zastosowanie: surowy skan ma przypadkową orientację - ta funkcja prostuje go do pozycji
    ''' "jak by stał na stole".
    '''
    ''' Wykrywanie działa tak samo jak funkcja "postaw na ściance" w Orca/PrusaSlicer: liczona jest otoczka
    ''' wypukła modelu (fizyczna podstawa MUSI na niej leżeć - powierzchnia wklęsła nie może być stabilną
    ''' podstawą), a potem sąsiadujące trójkąty otoczki o zbliżonej normalnej są łączone w jedną łatę.
    ''' Dzięki temu np. spód skanu niewidoczny dla skanera (bo model stał na nim podczas skanowania,
    ''' więc brakuje tam trójkątów) wciąż zostanie poprawnie wykryty, jeśli jego rogi są obecne w siatce.
    ''' </summary>
    Public Module AutoOrient

        ''' <summary>
        ''' Ile punktów siatki najwyżej trafia do liczenia otoczki wypukłej. Nie trzeba więcej - do wykrycia
        ''' DUŻYCH płaskich powierzchni w zupełności wystarczy kilkaset dobrze rozłożonych próbek, a niższa
        ''' wartość dodatkowo obniża ryzyko niestabilności numerycznej otoczki przy gęstym, mocno
        ''' zaokrąglonym (bez płaskich powierzchni) próbkowaniu - patrz zabezpieczenie w ConvexHull3D.Compute.
        ''' </summary>
        Private Const MaxHullSamplePoints As Integer = 800
        Private Const CoplanarDotThreshold As Single = 0.90F ' ~25°, tak samo jak próg łączenia ścianek w Orca/PrusaSlicer

        ''' <summary>
        ''' Wykrywa płaskie powierzchnie otoczki wypukłej siatki, grupuje sąsiadujące trójkąty o zbliżonej
        ''' normalnej w łaty i zwraca kandydatów posortowanych malejąco wg pola, największy pierwszy.
        ''' </summary>
        Public Function DetectFlatSurfaces(mesh As Mesh, Optional maxCandidates As Integer = 8) As List(Of FlatCandidate)
            Dim result As New List(Of FlatCandidate)
            If mesh Is Nothing OrElse mesh.TriangleCount = 0 Then Return result

            Dim totalArea = TotalMeshArea(mesh)
            If totalArea <= 0 Then Return result

            Dim hullPoints = SampleVertices(mesh, MaxHullSamplePoints)
            Dim hullFaces = ConvexHull3D.Compute(hullPoints)

            If hullFaces.Count = 0 Then
                ' Pusta otoczka: albo siatka jest zdegenerowana (płaska/liniowa - otoczka nie istnieje w 3D),
                ' albo zadziałało zabezpieczenie ConvexHull3D przed kaskadową niestabilnością numeryczną przy
                ' gęstym, mocno zaokrąglonym próbkowaniu. W obu przypadkach: zamiast nic nie zwracać, wróć do
                ' prostszego (i zawsze szybkiego) grupowania po normalnej wprost na trójkątach siatki.
                Return DetectFlatSurfacesByNormalBucketing(mesh, totalArea, maxCandidates)
            End If

            Dim neighbors = BuildHullFaceAdjacency(hullFaces)
            Dim visited(hullFaces.Count - 1) As Boolean

            For seed = 0 To hullFaces.Count - 1
                If visited(seed) Then Continue For
                Dim seedNormal = hullFaces(seed).Normal

                Dim patch As New List(Of Integer)
                Dim queue As New Queue(Of Integer)
                queue.Enqueue(seed)
                visited(seed) = True
                Do While queue.Count > 0
                    Dim fi = queue.Dequeue()
                    patch.Add(fi)
                    For Each nb In neighbors(fi)
                        If Not visited(nb) AndAlso Vector3.Dot(hullFaces(nb).Normal, seedNormal) > CoplanarDotThreshold Then
                            visited(nb) = True
                            queue.Enqueue(nb)
                        End If
                    Next
                Loop

                Dim candidate = BuildCandidate(hullFaces, patch, hullPoints, totalArea)
                If candidate IsNot Nothing Then result.Add(candidate)
            Next

            result.Sort(Function(a, b) b.Area.CompareTo(a.Area))
            If result.Count > maxCandidates Then result = result.GetRange(0, maxCandidates)
            Return result
        End Function

        ''' <summary>Automatycznie wybiera największą płaską powierzchnię i od razu prostuje model do niej.</summary>
        Public Function AutoOrientToFlattest(mesh As Mesh) As OperationResult
            Dim candidates = DetectFlatSurfaces(mesh, 1)
            If candidates.Count = 0 Then
                Return OperationResult.Fail(Loc.Translated("Nie znaleziono żadnej płaskiej powierzchni (siatka pusta albo zdegenerowana).", "No flat surface found (mesh is empty or degenerate)."))
            End If
            Return OrientToSurface(mesh, candidates(0))
        End Function

        ''' <summary>Obraca model tak, żeby podana powierzchnia (jej normalna) stała się poziomą podstawą skierowaną w dół, i "stawia" go na podłodze (Y=0).</summary>
        Public Function OrientToSurface(mesh As Mesh, candidate As FlatCandidate) As OperationResult
            Return OrientToSurface(mesh, candidate.Normal, candidate.AreaFraction)
        End Function

        Public Function OrientToSurface(mesh As Mesh, targetNormal As Vector3, Optional areaFraction As Double = 0) As OperationResult
            If mesh.VertexCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "Mesh is empty."))
            If targetNormal.LengthSquared() < 0.0000001F Then Return OperationResult.Fail(Loc.Translated("Nieprawidłowy kierunek powierzchni.", "Invalid surface direction."))

            Dim a = Vector3.Normalize(targetNormal)
            ' Widok 3D (HelixViewport3D) używa domyślnej konwencji "Z w górę" (nie ustawiamy własnego
            ' UpDirection w XAML) - to samo widać na gizmo osi w podglądzie. Wybrana powierzchnia ma więc
            ' wskazywać w dół wzdłuż -Z, żeby model faktycznie "stanął na podłodze" NA EKRANIE, a nie tylko
            ' w liczbach - wcześniej użycie -Y dawało matematycznie poprawny, ale wizualnie "postawiony
            ' na sztorc" wynik, bo Y w tym widoku jest osią poziomą, nie pionową.
            Dim b = -Vector3.UnitZ ' wybrana powierzchnia ma wskazywać w dół - staje się podstawą
            Dim rotation = RotationAligning(a, b)

            Dim centroid As Vector3 = Vector3.Zero
            For Each v In mesh.Vertices
                centroid += v
            Next
            centroid /= mesh.VertexCount

            For i = 0 To mesh.Vertices.Count - 1
                mesh.Vertices(i) = centroid + Vector3.Transform(mesh.Vertices(i) - centroid, rotation)
            Next

            ' postaw na "podłodze" (Z=0)
            Dim bbox = mesh.GetBoundingBox()
            Dim dropOffset = -bbox.min.Z
            For i = 0 To mesh.Vertices.Count - 1
                mesh.Vertices(i) = New Vector3(mesh.Vertices(i).X, mesh.Vertices(i).Y, mesh.Vertices(i).Z + dropOffset)
            Next

            mesh.ClearNormals() ' orientacja się zmieniła - stare normalne nieaktualne

            Dim pctText = If(areaFraction > 0, Loc.Translated($" (~{areaFraction * 100:F0}% powierzchni)", $" (~{areaFraction * 100:F0}% of surface)"), "")
            Return OperationResult.Ok(
                Loc.Translated($"Wyprostowano model wg wybranej powierzchni{pctText} - stała się podstawą.", $"Model straightened using the selected surface{pctText} - it is now the base."),
                Loc.Translated("Zalecane: przelicz normalne.", "Recommended: recalculate normals."))
        End Function

        ''' <summary>Macierz rotacji (jako kwaternion) obracająca znormalizowany wektor a na znormalizowany wektor b.</summary>
        Private Function RotationAligning(a As Vector3, b As Vector3) As Quaternion
            Dim dot = Vector3.Dot(a, b)

            If dot > 0.999999F Then Return Quaternion.Identity ' już wyrównane

            If dot < -0.999999F Then ' dokładnie przeciwne - obrót o 180° wokół dowolnej osi prostopadłej do a
                Dim axis = Vector3.Cross(a, Vector3.UnitX)
                If axis.LengthSquared() < 0.000001F Then axis = Vector3.Cross(a, Vector3.UnitZ)
                axis = Vector3.Normalize(axis)
                Return Quaternion.CreateFromAxisAngle(axis, MathF.PI)
            End If

            Dim rotAxis = Vector3.Normalize(Vector3.Cross(a, b))
            Dim angle = MathF.Acos(Math.Max(-1.0F, Math.Min(1.0F, dot)))
            Return Quaternion.CreateFromAxisAngle(rotAxis, angle)
        End Function

        Private Function TotalMeshArea(mesh As Mesh) As Double
            Dim total As Double = 0
            For Each t In mesh.Triangles
                Dim p0 = mesh.Vertices(t.A) : Dim p1 = mesh.Vertices(t.B) : Dim p2 = mesh.Vertices(t.C)
                total += Vector3.Cross(p1 - p0, p2 - p0).Length() * 0.5
            Next
            Return total
        End Function

        ''' <summary>
        ''' Próbkuje wierzchołki siatki do policzenia otoczki wypukłej (dla bardzo gęstych skanów liczenie
        ''' otoczki z KAŻDEGO punktu byłoby niepotrzebnie wolne - do wykrycia dużych płaskich powierzchni
        ''' w zupełności wystarczy reprezentatywna próbka). Stały seed = powtarzalny wynik dla tego samego pliku.
        ''' </summary>
        Private Function SampleVertices(mesh As Mesh, maxPoints As Integer) As Vector3()
            If mesh.VertexCount <= maxPoints Then Return mesh.Vertices.ToArray()

            Dim indices(mesh.VertexCount - 1) As Integer
            For i = 0 To indices.Length - 1
                indices(i) = i
            Next
            Dim rng As New Random(12345)
            For i = 0 To maxPoints - 1
                Dim j = rng.Next(i, indices.Length)
                Dim tmp = indices(i) : indices(i) = indices(j) : indices(j) = tmp
            Next

            Dim result(maxPoints - 1) As Vector3
            For i = 0 To maxPoints - 1
                result(i) = mesh.Vertices(indices(i))
            Next
            Return result
        End Function

        ''' <summary>Sąsiedzi każdej ściany otoczki (inne ściany dzielące z nią krawędź) - do wypełniania łat.</summary>
        Private Function BuildHullFaceAdjacency(faces As List(Of HullFace)) As List(Of List(Of Integer))
            Dim edgeOwners As New Dictionary(Of (Integer, Integer), List(Of Integer))
            Dim addEdge = Sub(a As Integer, b As Integer, fi As Integer)
                              Dim key = MeshTopology.EdgeKey(a, b)
                              Dim owners As List(Of Integer) = Nothing
                              If Not edgeOwners.TryGetValue(key, owners) Then
                                  owners = New List(Of Integer)
                                  edgeOwners(key) = owners
                              End If
                              owners.Add(fi)
                          End Sub

            For fi = 0 To faces.Count - 1
                addEdge(faces(fi).A, faces(fi).B, fi)
                addEdge(faces(fi).B, faces(fi).C, fi)
                addEdge(faces(fi).C, faces(fi).A, fi)
            Next

            Dim neighbors As New List(Of List(Of Integer))
            For i = 0 To faces.Count - 1
                neighbors.Add(New List(Of Integer))
            Next
            For Each owners In edgeOwners.Values
                If owners.Count = 2 Then
                    neighbors(owners(0)).Add(owners(1))
                    neighbors(owners(1)).Add(owners(0))
                End If
                ' >2 właścicieli (niemanifoldowe) praktycznie się nie zdarza dla prawdziwej otoczki wypukłej - pomijamy bez błędu
            Next
            Return neighbors
        End Function

        ''' <summary>Buduje kandydata z łaty ściankowej: pole, normalna ważona polem, środek ciężkości i trójkąty do podświetlenia. Zwraca Nothing dla zdegenerowanej (zerowej) łaty.</summary>
        Private Function BuildCandidate(hullFaces As List(Of HullFace), patch As List(Of Integer), points As Vector3(), totalMeshArea As Double) As FlatCandidate
            Dim weightedNormal As Vector3 = Vector3.Zero
            Dim weightedCentroid As Vector3 = Vector3.Zero
            Dim area As Double = 0
            Dim triangles As New List(Of (A As Vector3, B As Vector3, C As Vector3))

            For Each fi In patch
                Dim f = hullFaces(fi)
                Dim pa = points(f.A) : Dim pb = points(f.B) : Dim pc = points(f.C)
                Dim triArea = Vector3.Cross(pb - pa, pc - pa).Length() * 0.5
                If triArea <= 0 Then Continue For
                weightedNormal += f.Normal * CSng(triArea)
                weightedCentroid += ((pa + pb + pc) / 3.0F) * CSng(triArea)
                area += triArea
                triangles.Add((pa, pb, pc))
            Next
            If area <= 0 Then Return Nothing

            Return New FlatCandidate With {
                .Normal = If(weightedNormal.LengthSquared() > 0.0000001F, Vector3.Normalize(weightedNormal), hullFaces(patch(0)).Normal),
                .Area = area,
                .AreaFraction = area / totalMeshArea,
                .Centroid = weightedCentroid / CSng(area),
                .Triangles = triangles
            }
        End Function

        ''' <summary>Zapasowy sposób wykrywania (gdy otoczka wypukła się nie liczy - zdegenerowana siatka): grupuje trójkąty siatki wprost wg kierunku normalnej, bez otoczki.</summary>
        Private Function DetectFlatSurfacesByNormalBucketing(mesh As Mesh, totalArea As Double, maxCandidates As Integer) As List(Of FlatCandidate)
            Dim buckets As New Dictionary(Of (Integer, Integer, Integer), FlatCandidate)
            Const bucketScale As Single = 50.0F ' ~0,02 na składową (kilka stopni tolerancji)

            For Each t In mesh.Triangles
                Dim p0 = mesh.Vertices(t.A) : Dim p1 = mesh.Vertices(t.B) : Dim p2 = mesh.Vertices(t.C)
                Dim cross = Vector3.Cross(p1 - p0, p2 - p0)
                Dim lenSq = cross.LengthSquared()
                If lenSq <= 0.0000000001F Then Continue For ' zdegenerowany trójkąt

                Dim len = MathF.Sqrt(lenSq)
                Dim area = len * 0.5
                Dim n = cross / len

                Dim key = (CInt(Math.Round(n.X * bucketScale)), CInt(Math.Round(n.Y * bucketScale)), CInt(Math.Round(n.Z * bucketScale)))
                Dim candidate As FlatCandidate = Nothing
                If Not buckets.TryGetValue(key, candidate) Then
                    candidate = New FlatCandidate()
                    buckets(key) = candidate
                End If
                candidate.Normal += n * CSng(area)
                candidate.Area += area
                candidate.Triangles.Add((p0, p1, p2))
            Next

            Dim result As New List(Of FlatCandidate)
            For Each c In buckets.Values
                If c.Normal.LengthSquared() > 0.0000001F Then c.Normal = Vector3.Normalize(c.Normal)
                c.AreaFraction = If(totalArea > 0, c.Area / totalArea, 0)
                Dim centroidSum As Vector3 = Vector3.Zero
                For Each tri In c.Triangles
                    centroidSum += (tri.A + tri.B + tri.C) / 3.0F
                Next
                c.Centroid = If(c.Triangles.Count > 0, centroidSum / c.Triangles.Count, Vector3.Zero)
                result.Add(c)
            Next

            result.Sort(Function(a, b) b.Area.CompareTo(a.Area))
            If result.Count > maxCandidates Then result = result.GetRange(0, maxCandidates)
            Return result
        End Function

    End Module

End Namespace
