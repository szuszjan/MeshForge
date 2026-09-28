Imports System.Diagnostics
Imports System.IO
Imports System.Linq
Imports System.Numerics
Imports MeshForge.Core.Algorithms
Imports MeshForge.Core.Geometry
Imports MeshForge.Core.IO
Imports Xunit

Public Class AlgorithmTests

    ''' <summary>Jednostkowy sześcian [-1,1]^3, 8 wierzchołków, 12 trójkątów, nawinięcie CCW-na-zewnątrz (ręcznie zweryfikowane).</summary>
    Private Shared Function MakeCube() As Mesh
        Dim m As New Mesh()
        m.Vertices.AddRange(New Vector3() {
            New Vector3(-1, -1, -1), New Vector3(1, -1, -1), New Vector3(1, 1, -1), New Vector3(-1, 1, -1),
            New Vector3(-1, -1, 1), New Vector3(1, -1, 1), New Vector3(1, 1, 1), New Vector3(-1, 1, 1)
        })
        m.Triangles.AddRange(New Triangle() {
            New Triangle(0, 2, 1), New Triangle(0, 3, 2),
            New Triangle(4, 5, 6), New Triangle(4, 6, 7),
            New Triangle(0, 1, 5), New Triangle(0, 5, 4),
            New Triangle(3, 6, 2), New Triangle(3, 7, 6),
            New Triangle(0, 7, 3), New Triangle(0, 4, 7),
            New Triangle(1, 2, 6), New Triangle(1, 6, 5)
        })
        Return m
    End Function

    ''' <summary>Cienka, szeroka "płyta" (pudełko hx x hy x hz), to samo nawinięcie co MakeCube - do testów auto-orientacji (jedna para ścian wyraźnie większa niż pozostałe).</summary>
    Private Shared Function MakeSlab(hx As Single, hy As Single, hz As Single) As Mesh
        Dim m As New Mesh()
        m.Vertices.AddRange(New Vector3() {
            New Vector3(-hx, -hy, -hz), New Vector3(hx, -hy, -hz), New Vector3(hx, hy, -hz), New Vector3(-hx, hy, -hz),
            New Vector3(-hx, -hy, hz), New Vector3(hx, -hy, hz), New Vector3(hx, hy, hz), New Vector3(-hx, hy, hz)
        })
        m.Triangles.AddRange(New Triangle() {
            New Triangle(0, 2, 1), New Triangle(0, 3, 2),
            New Triangle(4, 5, 6), New Triangle(4, 6, 7),
            New Triangle(0, 1, 5), New Triangle(0, 5, 4),
            New Triangle(3, 6, 2), New Triangle(3, 7, 6),
            New Triangle(0, 7, 3), New Triangle(0, 4, 7),
            New Triangle(1, 2, 6), New Triangle(1, 6, 5)
        })
        Return m
    End Function

    <Fact>
    Public Sub AutoOrient_ProstujeSkosnaPlaskaPlyteDoPodstawy()
        Dim slab = MakeSlab(2.0F, 0.2F, 2.0F) ' szeroka i płaska: X=4, Y=0.4, Z=4 - Y wyraźnie najmniejsze

        ' obróć losowo (żeby sprawdzić, czy funkcja faktycznie NAPRAWIA orientację, a nie tylko przepuszcza już-płaski model)
        Dim rotation = Quaternion.CreateFromYawPitchRoll(0.7F, 0.5F, 0.3F)
        For i = 0 To slab.Vertices.Count - 1
            slab.Vertices(i) = Vector3.Transform(slab.Vertices(i), rotation)
        Next

        Dim result = AutoOrient.AutoOrientToFlattest(slab)
        Assert.True(result.Success, result.ToString())

        Dim bbox = slab.GetBoundingBox()
        Dim size = bbox.max - bbox.min
        Dim sorted = {size.X, size.Y, size.Z}.OrderBy(Function(x) x).ToArray()

        ' po naprawie: najmniejszy wymiar (grubość płyty) powinien być z powrotem osią Z - HelixViewport3D
        ' w tej aplikacji używa domyślnej konwencji "Z w górę" (bez własnego UpDirection w XAML), więc
        ' "podstawa" musi leżeć w płaszczyźnie Z=0, żeby model faktycznie wyglądał na stojący, nie tylko
        ' liczbowo się zgadzał (naprawiony błąd: wcześniej kod płaszczył wzdłuż Y, co matematycznie
        ' się zgadzało, ale w tym widoku Y jest osią poziomą, nie pionową - model "stawał na sztorc").
        Assert.Equal(CDbl(sorted(0)), CDbl(size.Z), 2)
        Assert.True(size.Z < size.X * 0.3F, $"Z={size.Z} X={size.X} - płyta powinna dalej być płaska wzdłuż Z")
        Assert.True(size.Z < size.Y * 0.3F, $"Z={size.Z} Y={size.Y} - płyta powinna dalej być płaska wzdłuż Z")
        Assert.Equal(0.0F, bbox.min.Z, 3) ' model "stoi" na podłodze Z=0
    End Sub

    <Fact>
    Public Sub AutoOrient_DetectFlatSurfaces_ZnajdujeNajwiekszaPowierzchnieJakoPierwsza()
        Dim slab = MakeSlab(2.0F, 0.2F, 2.0F)
        Dim candidates = AutoOrient.DetectFlatSurfaces(slab)
        Assert.True(candidates.Count >= 2)
        ' pierwszy kandydat (największy) to góra albo dół płyty (normalna ~pionowa), nie bok
        Assert.True(Math.Abs(candidates(0).Normal.Y) > 0.9F, $"Oczekiwano normalnej ~pionowej, otrzymano {candidates(0).Normal}")
        ' kandydaci posortowani malejąco wg pola
        For i = 0 To candidates.Count - 2
            Assert.True(candidates(i).Area >= candidates(i + 1).Area)
        Next
    End Sub

    <Fact>
    Public Sub ConvexHull3D_Szescian_Zwraca12TrojkatowISzescKierunkowNormalnych()
        Dim cube = MakeCube()
        Dim hull = ConvexHull3D.Compute(cube.Vertices)
        Assert.Equal(12, hull.Count)

        Dim distinctNormals = hull.Select(Function(f) (CInt(Math.Round(f.Normal.X)), CInt(Math.Round(f.Normal.Y)), CInt(Math.Round(f.Normal.Z)))).Distinct().Count()
        Assert.Equal(6, distinctNormals)
    End Sub

    <Fact>
    Public Sub ConvexHull3D_PomijaPunktWewnetrzny()
        Dim points As New List(Of Vector3)(MakeCube().Vertices)
        points.Add(Vector3.Zero) ' środek sześcianu - musi zostać pominięty, leży ściśle wewnątrz otoczki
        Dim hull = ConvexHull3D.Compute(points)

        Dim centerIndex = points.Count - 1
        For Each f In hull
            Assert.NotEqual(centerIndex, f.A)
            Assert.NotEqual(centerIndex, f.B)
            Assert.NotEqual(centerIndex, f.C)
        Next
    End Sub

    ''' <summary>
    ''' Regresja: gęsta, prawie idealnie okrągła "organiczna" chmura punktów (np. mocno zaokrąglony skan
    ''' bez większych płaskich powierzchni - jak realny model "ośmiornica.stl", który faktycznie zawiesił
    ''' program użytkownikowi) to najgorszy przypadek dla tej otoczki wypukłej: sąsiednie ściany stają się
    ''' niemal współpłaszczyznowe, błąd zaokrąglenia w teście widoczności potrafi (rzadko) zostawić
    ''' "osierocone" ściany zamiast je usunąć, a to się kaskadowo nakręca - zmierzone: setki tysięcy ścian
    ''' i dziesiątki sekund zamiast tysięcy ścian i ułamka sekundy. ConvexHull3D.Compute ma teraz
    ''' zabezpieczenie (limit liczby ścian, poddaje się i zwraca pustą otoczkę zamiast się nakręcać) -
    ''' ten test pilnuje, żeby dla dużej, gęstej chmury zawsze kończyło się to SZYBKO, niezależnie od tego,
    ''' czy otoczka akurat wyjdzie w pełni poprawna, czy zadziała zabezpieczenie.
    ''' </summary>
    <Fact>
    Public Sub ConvexHull3D_ChmuraKulista_KonczySieSzybko()
        Dim rng As New Random(42)
        Dim points As New List(Of Vector3)
        For i = 1 To 3000
            Dim v As New Vector3(CSng(NextGaussian(rng)), CSng(NextGaussian(rng)), CSng(NextGaussian(rng)))
            If v.LengthSquared() < 0.0001F Then Continue For
            points.Add(Vector3.Normalize(v) * 10.0F)
        Next

        Dim sw = Stopwatch.StartNew()
        Dim hull = ConvexHull3D.Compute(points)
        sw.Stop()

        Assert.True(sw.Elapsed.TotalSeconds < 5, $"ConvexHull3D.Compute zajął {sw.Elapsed.TotalSeconds:F1}s dla {points.Count} punktów - zabezpieczenie przed kaskadową eksplozją ścian najwyraźniej przestało działać")
        ' pusta otoczka (zabezpieczenie się uruchomiło) jest tu akceptowalna - liczy się, że NIE ZAWIESIŁO;
        ' AutoOrient ma własny, oddzielnie testowany fallback na wypadek pustej otoczki
        Assert.True(hull.Count = 0 OrElse hull.Count <= 8 * points.Count, $"Oczekiwano pustej otoczki (zadziałało zabezpieczenie) albo rozsądnej liczby ścian, otrzymano {hull.Count}")
    End Sub

    Private Shared Function NextGaussian(rng As Random) As Double
        Dim u1 = 1.0 - rng.NextDouble()
        Dim u2 = rng.NextDouble()
        Return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2)
    End Function

    <Fact>
    Public Sub AutoOrient_DetectFlatSurfaces_OtoczkaWypuklaWykrywaBrakujacaSciane()
        Dim cube = MakeCube()
        ' usuń górną ścianę (+Z) - symuluje typową dziurę po skanowaniu (np. spód leżący na blacie skanera,
        ' więc skaner nigdy go nie zobaczył - ale rogi tej ściany nadal są w siatce, bo należą do ścian bocznych)
        cube.Triangles.RemoveAll(Function(t) (t.A = 4 AndAlso t.B = 5 AndAlso t.C = 6) OrElse (t.A = 4 AndAlso t.B = 6 AndAlso t.C = 7))

        Dim candidates = AutoOrient.DetectFlatSurfaces(cube)

        ' mimo braku trójkątów +Z, jej 4 rogi nadal istnieją w siatce - otoczka wypukła (tak jak w Orca/Prusa
        ' "postaw na ściance") powinna mimo to znaleźć tę płaszczyznę jako prawidłową, stabilną podstawę
        Assert.Contains(candidates, Function(c) Vector3.Dot(c.Normal, Vector3.UnitZ) > 0.9F)
    End Sub

    <Fact>
    Public Sub Cube_JestWodoszczelnyIMaDodatniaObjetosc()
        Dim cube = MakeCube()
        Dim stats = MeshStatistics.Compute(cube)
        Assert.True(stats.IsWatertight)
        Assert.Equal(8.0, stats.Volume, 3) ' bok 2 -> objętość 2^3 = 8
    End Sub

    <Fact>
    Public Sub HoleFiller_WypelniaPojedynczaDziureIPrzywracaWodoszczelnosc()
        Dim cube = MakeCube()
        ' usuń ścianę +Z (dwa trójkąty) - symuluje typową dziurę po skanowaniu
        cube.Triangles.RemoveAll(Function(t) (t.A = 4 AndAlso t.B = 5 AndAlso t.C = 6) OrElse (t.A = 4 AndAlso t.B = 6 AndAlso t.C = 7))
        Assert.Equal(10, cube.TriangleCount)
        Assert.False(MeshTopology.IsWatertight(cube))

        Dim result = HoleFiller.FillHoles(cube)
        Assert.True(result.Success)
        Assert.True(MeshTopology.IsWatertight(cube))
        Assert.Equal(12, cube.TriangleCount) ' łata na 4-wierzchołkowym otworze = dokładnie 2 nowe trójkąty

        ' nowe trójkąty powinny mieć normalną skierowaną w +Z (tak jak oryginalna usunięta ściana), nie do środka
        For Each t In cube.Triangles
            If t.A >= 4 AndAlso t.B >= 4 AndAlso t.C >= 4 Then ' trójkąty łaty odwołują się tylko do wierzchołków górnej ściany (4-7)
                Dim p0 = cube.Vertices(t.A) : Dim p1 = cube.Vertices(t.B) : Dim p2 = cube.Vertices(t.C)
                Dim n = Vector3.Cross(p1 - p0, p2 - p0)
                Assert.True(n.Z > 0, $"Łata powinna mieć normalną +Z, a wyszło ({n.X},{n.Y},{n.Z})")
            End If
        Next
    End Sub

    <Fact>
    Public Sub HoleFiller_NaWodoszczelnejSiatceNieDodajeTrojkatow()
        Dim cube = MakeCube()
        Dim result = HoleFiller.FillHoles(cube)
        Assert.True(result.Success)
        Assert.Equal(12, cube.TriangleCount)
    End Sub

    <Fact>
    Public Sub Subdivide_MnozyTrojkatyRazyCztery()
        Dim cube = MakeCube()
        Dim beforeVerts = cube.VertexCount
        Dim result = Tessellator.Subdivide(cube, 1)
        Assert.True(result.Success)
        Assert.Equal(48, cube.TriangleCount) ' 12 * 4
        Assert.True(cube.VertexCount > beforeVerts)
        Assert.True(MeshTopology.IsWatertight(cube)) ' subdivision nie może psuć wodoszczelności
    End Sub

    <Fact>
    Public Sub Subdivide_DwieIteracjeMnozyRazySzesnascie()
        Dim cube = MakeCube()
        Tessellator.Subdivide(cube, 2)
        Assert.Equal(192, cube.TriangleCount) ' 12 * 4 * 4
    End Sub

    <Fact>
    Public Sub Decymacja_RedukujeLiczbeTrojkatowDoCeluIZachowujeSpojnosc()
        Dim cube = MakeCube()
        Tessellator.Subdivide(cube, 2) ' 192 trójkąty - żeby było co upraszczać
        Dim result = Tessellator.DecimateByEdgeCollapse(cube, 20)
        Assert.True(result.Success)
        Assert.True(cube.TriangleCount <= 24, $"Oczekiwano rozsądnie blisko celu (20), a otrzymano {cube.TriangleCount}")
        Assert.True(cube.TriangleCount > 0)

        ' każdy trójkąt musi wskazywać na istniejący wierzchołek (brak wiszących indeksów)
        For Each t In cube.Triangles
            Assert.InRange(t.A, 0, cube.VertexCount - 1)
            Assert.InRange(t.B, 0, cube.VertexCount - 1)
            Assert.InRange(t.C, 0, cube.VertexCount - 1)
            Assert.False(t.IsDegenerate)
        Next

        ' brak NaN/nieskończoności w pozycjach po scalaniu QEM
        For Each v In cube.Vertices
            Assert.False(Single.IsNaN(v.X) OrElse Single.IsNaN(v.Y) OrElse Single.IsNaN(v.Z))
        Next
    End Sub

    <Fact>
    Public Sub MeshCleaner_SpawaZdublowaneWierzcholki()
        Dim m As New Mesh()
        ' dwa "osobne" trójkąty dzielące krawędź, ale z NIEZALEŻNYMI (zdublowanymi) wierzchołkami na tej krawędzi - typowe po eksporcie STL
        m.Vertices.AddRange(New Vector3() {
            New Vector3(0, 0, 0), New Vector3(1, 0, 0), New Vector3(0, 1, 0),
            New Vector3(1, 0, 0), New Vector3(0, 0, 0), New Vector3(1, 1, 0)
        })
        m.Triangles.Add(New Triangle(0, 1, 2))
        m.Triangles.Add(New Triangle(3, 5, 4))

        Dim merged = MeshCleaner.WeldDuplicateVertices(m, 0.0001F)
        Assert.Equal(2, merged) ' 2 pary zdublowanych punktów -> 2 scalenia (6 -> 4 wierzchołki)
        Assert.Equal(4, m.VertexCount)
        Assert.Equal(2, m.TriangleCount)
    End Sub

    <Fact>
    Public Sub PointCloudTriangulator_TriangulujeTrzyPunkty()
        Dim m As New Mesh()
        m.Vertices.Add(New Vector3(0, 0, 0))
        m.Vertices.Add(New Vector3(1, 0, 0))
        m.Vertices.Add(New Vector3(0, 0, 1))
        Dim result = PointCloudTriangulator.Triangulate(m)
        Assert.True(result.Success, result.ToString())
        Assert.Equal(1, m.TriangleCount)
    End Sub

    <Fact>
    Public Sub PointCloudTriangulator_TriangulujeRegularnaSiatkePunktow()
        Dim m As New Mesh()
        Const gridSize = 5
        For x = 0 To gridSize - 1
            For z = 0 To gridSize - 1
                m.Vertices.Add(New Vector3(x, 0, z))
            Next
        Next

        Dim result = PointCloudTriangulator.Triangulate(m)
        Assert.True(result.Success, result.Message)

        Dim expectedTriangles = 2 * (gridSize - 1) * (gridSize - 1)
        Assert.Equal(expectedTriangles, m.TriangleCount)

        For Each t In m.Triangles
            Assert.False(t.IsDegenerate)
        Next
    End Sub

    <Fact>
    Public Sub NormalCalculator_ProdukujeWektoryJednostkowe()
        Dim cube = MakeCube()
        Dim result = NormalCalculator.Recalculate(cube)
        Assert.True(result.Success)
        Assert.Equal(cube.VertexCount, cube.Normals.Count)
        For Each n In cube.Normals
            Assert.Equal(1.0F, n.Length(), 2)
        Next
    End Sub

    <Fact>
    Public Sub ObjExportImport_ZachowujeLiczbeWierzcholkowITrojkatow()
        Dim cube = MakeCube()
        Dim tmp = Path.Combine(Path.GetTempPath(), $"skan3dstudio_test_{Guid.NewGuid():N}.obj")
        Try
            MeshIO.Save(cube, tmp)
            Dim loaded = MeshIO.Load(tmp)
            Assert.Equal(cube.VertexCount, loaded.VertexCount)
            Assert.Equal(cube.TriangleCount, loaded.TriangleCount)
            Assert.True(MeshTopology.IsWatertight(loaded))
        Finally
            If File.Exists(tmp) Then File.Delete(tmp)
        End Try
    End Sub

    <Fact>
    Public Sub Smoother_NieRuszaWierzcholkowBrzegowych()
        Dim cube = MakeCube()
        cube.Triangles.RemoveAll(Function(t) (t.A = 4 AndAlso t.B = 5 AndAlso t.C = 6) OrElse (t.A = 4 AndAlso t.B = 6 AndAlso t.C = 7))
        Dim boundaryVertexBefore = cube.Vertices(4)

        Dim result = Smoother.Smooth(cube, 3, 0.5F)
        Assert.True(result.Success)
        Assert.Equal(boundaryVertexBefore, cube.Vertices(4)) ' wierzchołek na brzegu otworu ma zostać nienaruszony
    End Sub

    <Fact>
    Public Sub Texturizer_GenerujeUVDlaKazdegoWierzcholka()
        Dim cube = MakeCube()
        Dim result = Texturizer.GenerateUVs(cube, ProjectionType.Spherical)
        Assert.True(result.Success)
        Assert.Equal(cube.VertexCount, cube.UVs.Count)
    End Sub

    <Fact>
    Public Sub PrzykladowyPlik_SzescianZDziura_WczytujeSieIDajeSieWypelnic()
        Dim filePath = Path.Combine(AppContext.BaseDirectory, "przyklady", "szescian_z_dziura.obj")
        Dim mesh = MeshIO.Load(filePath)
        Assert.Equal(8, mesh.VertexCount)
        Assert.Equal(10, mesh.TriangleCount)
        Assert.False(MeshTopology.IsWatertight(mesh))

        Dim result = HoleFiller.FillHoles(mesh)
        Assert.True(result.Success)
        Assert.True(MeshTopology.IsWatertight(mesh))
        Assert.Equal(12, mesh.TriangleCount)
    End Sub

    <Fact>
    Public Sub PrzykladowyPlik_FalaChmuraPunktow_WczytujeSieITeselujeSie()
        Dim filePath = Path.Combine(AppContext.BaseDirectory, "przyklady", "fala_chmura_punktow.xyz")
        Dim mesh = MeshIO.Load(filePath)
        Assert.Equal(625, mesh.VertexCount)
        Assert.Equal(0, mesh.TriangleCount)

        Dim result = PointCloudTriangulator.Triangulate(mesh)
        Assert.True(result.Success, result.ToString())
        Assert.Equal(2 * 24 * 24, mesh.TriangleCount) ' siatka 25x25 -> 2*(n-1)*(n-1) trójkątów
        For Each t In mesh.Triangles
            Assert.False(t.IsDegenerate)
        Next
    End Sub

    <Fact>
    Public Sub MeshTransformer_NormalizeUstawiaNajwiekszyWymiarNaJeden()
        Dim cube = MakeCube() ' rozmiar 2x2x2
        MeshTransformer.NormalizeToSize(cube, 1.0F)
        Dim bbox = cube.GetBoundingBox()
        Dim size = bbox.max - bbox.min
        Dim largest = Math.Max(size.X, Math.Max(size.Y, size.Z))
        Assert.Equal(1.0F, largest, 3)
    End Sub

End Class
