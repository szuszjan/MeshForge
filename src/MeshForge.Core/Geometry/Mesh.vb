Imports System.Numerics

Namespace Geometry

    ''' <summary>
    ''' Centralny model siatki trójkątów używany w całej aplikacji.
    ''' Model "unified index": każdy wierzchołek ma co najwyżej jedną normalną, jeden UV i jeden kolor
    ''' (tak jak oczekuje WPF MeshGeometry3D) - importery dzielą wierzchołki OBJ/PLY tam, gdzie to potrzebne.
    ''' </summary>
    Public Class Mesh
        Public Property Name As String = "Siatka"

        Public Property Vertices As New List(Of Vector3)
        Public Property Triangles As New List(Of Triangle)

        ''' <summary>Normalne per-wierzchołek. Pusta lista = brak policzonych normalnych (użyj NormalCalculator).</summary>
        Public Property Normals As New List(Of Vector3)

        ''' <summary>Współrzędne UV per-wierzchołek (0..1). Pusta lista = brak mapowania tekstury.</summary>
        Public Property UVs As New List(Of Vector2)

        ''' <summary>Kolor per-wierzchołek (np. z chmury punktów skanera). Pusta lista = brak kolorów wierzchołków.</summary>
        Public Property VertexColors As New List(Of RgbColor)

        ''' <summary>Ścieżka do pliku obrazu użytego jako tekstura kolorowa (diffuse), albo "" jeśli brak.</summary>
        Public Property TexturePath As String = ""

        Public ReadOnly Property HasNormals As Boolean
            Get
                Return Normals.Count = Vertices.Count AndAlso Vertices.Count > 0
            End Get
        End Property

        Public ReadOnly Property HasUVs As Boolean
            Get
                Return UVs.Count = Vertices.Count AndAlso Vertices.Count > 0
            End Get
        End Property

        Public ReadOnly Property HasVertexColors As Boolean
            Get
                Return VertexColors.Count = Vertices.Count AndAlso Vertices.Count > 0
            End Get
        End Property

        Public ReadOnly Property HasTexture As Boolean
            Get
                Return Not String.IsNullOrEmpty(TexturePath) AndAlso HasUVs
            End Get
        End Property

        Public ReadOnly Property VertexCount As Integer
            Get
                Return Vertices.Count
            End Get
        End Property

        Public ReadOnly Property TriangleCount As Integer
            Get
                Return Triangles.Count
            End Get
        End Property

        ''' <summary>Głęboka kopia siatki - używana m.in. przez stos Cofnij (Undo) w UI.</summary>
        Public Function Clone() As Mesh
            Dim m As New Mesh With {
                .Name = Me.Name,
                .TexturePath = Me.TexturePath
            }
            m.Vertices.AddRange(Me.Vertices)
            m.Triangles.AddRange(Me.Triangles)
            m.Normals.AddRange(Me.Normals)
            m.UVs.AddRange(Me.UVs)
            m.VertexColors.AddRange(Me.VertexColors)
            Return m
        End Function

        ''' <summary>Usuwa policzone normalne (np. po edycji topologii, przed ponownym przeliczeniem).</summary>
        Public Sub ClearNormals()
            Normals.Clear()
        End Sub

        Public Function GetBoundingBox() As (min As Vector3, max As Vector3)
            If Vertices.Count = 0 Then Return (Vector3.Zero, Vector3.Zero)
            Dim mn = Vertices(0)
            Dim mx = Vertices(0)
            For Each v In Vertices
                mn = Vector3.Min(mn, v)
                mx = Vector3.Max(mx, v)
            Next
            Return (mn, mx)
        End Function
    End Class

End Namespace
