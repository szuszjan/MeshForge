Imports System.Globalization
Imports System.IO
Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace IO

    ''' <summary>
    ''' Import plików PLY (Stanford Polygon Format) w wariancie ASCII - typowy eksport skanerów i MeshLaba,
    ''' często niesie kolor wierzchołków z chmury punktów. Wariant binarny PLY nie jest obsługiwany w v1
    ''' (rzuca czytelny wyjątek z sugestią konwersji, np. w MeshLab: File → Export Mesh → PLY (ASCII)).
    ''' </summary>
    Public Module PlyImporter

        Private ReadOnly IC As CultureInfo = CultureInfo.InvariantCulture

        Public Function Load(filePath As String) As Mesh
            Using sr As New StreamReader(filePath)
                Dim line = sr.ReadLine()
                If line Is Nothing OrElse line.Trim() <> "ply" Then
                    Throw New InvalidDataException("To nie jest poprawny plik PLY (brak nagłówka 'ply').")
                End If

                Dim vertexCount As Integer = 0
                Dim faceCount As Integer = 0
                Dim vertexProps As New List(Of String)
                Dim currentElement As String = ""
                Dim isBinary = False

                Do
                    line = sr.ReadLine()
                    If line Is Nothing Then Throw New InvalidDataException("Nieoczekiwany koniec pliku PLY w nagłówku.")
                    line = line.Trim()
                    If line = "end_header" Then Exit Do

                    Dim tokens = line.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                    If tokens.Length = 0 Then Continue Do

                    Select Case tokens(0)
                        Case "format"
                            isBinary = Not tokens(1).StartsWith("ascii", StringComparison.OrdinalIgnoreCase)
                        Case "element"
                            currentElement = tokens(1)
                            Dim cnt = Integer.Parse(tokens(2), IC)
                            If currentElement = "vertex" Then vertexCount = cnt
                            If currentElement = "face" Then faceCount = cnt
                        Case "property"
                            If currentElement = "vertex" Then
                                vertexProps.Add(tokens(tokens.Length - 1)) ' nazwa właściwości to ostatni token linii
                            End If
                    End Select
                Loop

                If isBinary Then
                    Throw New NotSupportedException(Loc.Translated(
                        "Ten plik PLY jest w formacie binarnym - nieobsługiwany w tej wersji. " &
                        "Skonwertuj go do PLY (ASCII), np. w MeshLab: File → Export Mesh As → PLY, odznaczając 'Binary encoding', " &
                        "albo wyeksportuj skan bezpośrednio jako OBJ lub STL.",
                        "This PLY file is in binary format - not supported in this version. " &
                        "Convert it to PLY (ASCII), e.g. in MeshLab: File → Export Mesh As → PLY, unchecking 'Binary encoding', " &
                        "or export the scan directly as OBJ or STL."))
                End If

                Dim xi = vertexProps.IndexOf("x")
                Dim yi = vertexProps.IndexOf("y")
                Dim zi = vertexProps.IndexOf("z")
                Dim ri = FirstIndexOfAny(vertexProps, "red", "r", "diffuse_red")
                Dim gi = FirstIndexOfAny(vertexProps, "green", "g", "diffuse_green")
                Dim bi = FirstIndexOfAny(vertexProps, "blue", "b", "diffuse_blue")
                Dim hasColor = ri >= 0 AndAlso gi >= 0 AndAlso bi >= 0

                If xi < 0 OrElse yi < 0 OrElse zi < 0 Then
                    Throw New InvalidDataException("Plik PLY nie zawiera właściwości x/y/z dla wierzchołków.")
                End If

                Dim mesh As New Mesh With {.Name = Path.GetFileNameWithoutExtension(filePath)}

                For i = 1 To vertexCount
                    line = sr.ReadLine()
                    If line Is Nothing Then Throw New InvalidDataException("Nieoczekiwany koniec pliku PLY w danych wierzchołków.")
                    Dim tokens = line.Trim().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)

                    Dim x = CSng(Double.Parse(tokens(xi), NumberStyles.Float, IC))
                    Dim y = CSng(Double.Parse(tokens(yi), NumberStyles.Float, IC))
                    Dim z = CSng(Double.Parse(tokens(zi), NumberStyles.Float, IC))
                    mesh.Vertices.Add(New Vector3(x, y, z))

                    If hasColor Then
                        Dim r = Byte.Parse(tokens(ri), IC)
                        Dim g = Byte.Parse(tokens(gi), IC)
                        Dim b = Byte.Parse(tokens(bi), IC)
                        mesh.VertexColors.Add(New RgbColor(r, g, b))
                    End If
                Next

                For i = 1 To faceCount
                    line = sr.ReadLine()
                    If line Is Nothing Then Exit For ' plik z samą chmurą punktów (0 face) albo obcięty - kończymy łagodnie
                    Dim tokens = line.Trim().Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
                    If tokens.Length = 0 Then Continue For

                    Dim n = Integer.Parse(tokens(0), IC)
                    If tokens.Length < 1 + n Then Continue For

                    Dim idx(n - 1) As Integer
                    For k = 0 To n - 1
                        idx(k) = Integer.Parse(tokens(1 + k), IC)
                    Next

                    ' triangulacja wachlarzowa dla wielokątów
                    For k = 1 To n - 2
                        mesh.Triangles.Add(New Triangle(idx(0), idx(k), idx(k + 1)))
                    Next
                Next

                Return mesh
            End Using
        End Function

        Private Function FirstIndexOfAny(list As List(Of String), ParamArray names As String()) As Integer
            For Each n In names
                Dim i = list.IndexOf(n)
                If i >= 0 Then Return i
            Next
            Return -1
        End Function

    End Module

    ''' <summary>Eksport do PLY (ASCII) - zachowuje kolory wierzchołków; do modeli teksturowanych używaj eksportu OBJ.</summary>
    Public Module PlyExporter

        Private ReadOnly IC As CultureInfo = CultureInfo.InvariantCulture

        Public Sub Save(mesh As Mesh, filePath As String)
            Using sw As New StreamWriter(filePath, append:=False)
                sw.WriteLine("ply")
                sw.WriteLine("format ascii 1.0")
                sw.WriteLine("comment Wyeksportowano ze MeshForge")
                sw.WriteLine($"element vertex {mesh.Vertices.Count}")
                sw.WriteLine("property float x")
                sw.WriteLine("property float y")
                sw.WriteLine("property float z")
                If mesh.HasVertexColors Then
                    sw.WriteLine("property uchar red")
                    sw.WriteLine("property uchar green")
                    sw.WriteLine("property uchar blue")
                End If
                sw.WriteLine($"element face {mesh.Triangles.Count}")
                sw.WriteLine("property list uchar int vertex_indices")
                sw.WriteLine("end_header")

                For i = 0 To mesh.Vertices.Count - 1
                    Dim v = mesh.Vertices(i)
                    If mesh.HasVertexColors Then
                        Dim c = mesh.VertexColors(i)
                        sw.WriteLine($"{v.X.ToString(IC)} {v.Y.ToString(IC)} {v.Z.ToString(IC)} {c.R} {c.G} {c.B}")
                    Else
                        sw.WriteLine($"{v.X.ToString(IC)} {v.Y.ToString(IC)} {v.Z.ToString(IC)}")
                    End If
                Next

                For Each t In mesh.Triangles
                    sw.WriteLine($"3 {t.A} {t.B} {t.C}")
                Next
            End Using
        End Sub

    End Module

End Namespace
