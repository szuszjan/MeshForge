Imports System.Windows.Media.Media3D
Imports System.Windows.Media.Imaging
Imports MeshForge.Core.Geometry
Imports HelixToolkit.Wpf

''' <summary>
''' Konwertuje MeshForge.Core.Geometry.Mesh (model niezależny od WPF) na obiekty WPF 3D do wyświetlenia
''' w podglądzie. Trzymane osobno od Core, żeby biblioteka Core mogła pozostać niezależna od WPF/UI.
''' </summary>
Public Module MeshVisualBuilder

    ''' <summary>Buduje główny model siatki z materiałem: tekstura ze zdjęcia > kolory wierzchołków (jako wygenerowany atlas) > jednolity szary.</summary>
    Public Function BuildModel(mesh As Mesh) As Model3D
        Dim geometry As New MeshGeometry3D()

        For Each v In mesh.Vertices
            geometry.Positions.Add(New Point3D(v.X, v.Y, v.Z))
        Next
        For Each t In mesh.Triangles
            geometry.TriangleIndices.Add(t.A)
            geometry.TriangleIndices.Add(t.B)
            geometry.TriangleIndices.Add(t.C)
        Next
        If mesh.HasNormals Then
            For Each n In mesh.Normals
                geometry.Normals.Add(New Vector3D(n.X, n.Y, n.Z))
            Next
        End If
        ' jeśli normalnych brak, WPF policzy je automatycznie przy renderowaniu - nic nie trzeba robić

        Dim material As Material
        If mesh.HasTexture Then
            For Each uv In mesh.UVs
                geometry.TextureCoordinates.Add(New Point(uv.X, 1.0 - uv.Y)) ' V w WPF jest odwrócone względem konwencji OBJ
            Next
            Dim bmp = LoadBitmap(mesh.TexturePath)
            material = New DiffuseMaterial(New ImageBrush(bmp))
        ElseIf mesh.HasVertexColors Then
            Dim atlas = BuildVertexColorAtlas(mesh)
            geometry.TextureCoordinates = New PointCollection(atlas.uvs)
            material = New DiffuseMaterial(New ImageBrush(atlas.bitmap))
        Else
            material = New DiffuseMaterial(New SolidColorBrush(Color.FromRgb(190, 197, 204)))
        End If

        Dim model As New GeometryModel3D(geometry, material) With {
            .BackMaterial = material ' widoczne też "od środka" - typowe przy otwartych/niedomkniętych skanach
        }
        Return model
    End Function

    ''' <summary>Buduje wizualizację krawędzi siatki (wireframe) jako nakładkę na model cieniowany.</summary>
    Public Function BuildWireframe(mesh As Mesh) As Visual3D
        Dim points As New Point3DCollection()
        For Each edge In MeshTopology.BuildEdgeUsage(mesh).Keys
            Dim a = mesh.Vertices(edge.Item1)
            Dim b = mesh.Vertices(edge.Item2)
            points.Add(New Point3D(a.X, a.Y, a.Z))
            points.Add(New Point3D(b.X, b.Y, b.Z))
        Next

        Return New LinesVisual3D With {
            .Points = points,
            .Color = Colors.Black,
            .Thickness = 0.6
        }
    End Function

    Private Function LoadBitmap(path As String) As BitmapImage
        Dim bmp As New BitmapImage()
        bmp.BeginInit()
        bmp.CacheOption = BitmapCacheOption.OnLoad
        bmp.UriSource = New Uri(path, UriKind.Absolute)
        bmp.EndInit()
        bmp.Freeze()
        Return bmp
    End Function

    ''' <summary>
    ''' WPF MeshGeometry3D nie ma pojęcia "kolor per-wierzchołek" - trik: zapisz każdy kolor wierzchołka
    ''' jako osobny piksel w małej wygenerowanej bitmapie i przypisz każdemu wierzchołkowi UV wskazujące
    ''' na jego własny piksel. To rozwiązanie tylko do WYŚWIETLANIA - nie zmienia danych siatki.
    ''' </summary>
    Private Function BuildVertexColorAtlas(mesh As Mesh) As (bitmap As WriteableBitmap, uvs As List(Of Point))
        Dim n = Math.Max(mesh.VertexCount, 1)
        Dim side = Math.Max(CInt(Math.Ceiling(Math.Sqrt(n))), 1)

        Dim bmp As New WriteableBitmap(side, side, 96, 96, PixelFormats.Bgra32, Nothing)
        Dim pixels(side * side - 1) As Integer
        Dim uvs As New List(Of Point)(mesh.VertexCount)

        For i = 0 To mesh.VertexCount - 1
            Dim px = i Mod side
            Dim py = i \ side
            Dim c = mesh.VertexColors(i)
            pixels(py * side + px) = (CInt(c.A) << 24) Or (CInt(c.R) << 16) Or (CInt(c.G) << 8) Or CInt(c.B)
            uvs.Add(New Point((px + 0.5) / side, (py + 0.5) / side))
        Next

        bmp.WritePixels(New Int32Rect(0, 0, side, side), pixels, side * 4, 0)
        bmp.Freeze()
        Return (bmp, uvs)
    End Function

End Module
