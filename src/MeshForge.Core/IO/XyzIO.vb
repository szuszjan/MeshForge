Imports System.Globalization
Imports System.IO
Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace IO

    ''' <summary>
    ''' Surowa chmura punktów w formacie tekstowym: jedna linia = jeden punkt, "x y z" lub "x y z r g b".
    ''' Separator spacja, tabulator lub przecinek. To typowy "najmniejszy wspólny mianownik" eksportu ze skanerów.
    ''' Siatka wynikowa nie ma trójkątów - użyj Algorithms.PointCloudTriangulator, żeby ją zteselować.
    ''' </summary>
    Public Module XyzImporter

        Private ReadOnly IC As CultureInfo = CultureInfo.InvariantCulture
        Private ReadOnly Seps As Char() = {" "c, ControlChars.Tab, ","c, ";"c}

        Public Function Load(filePath As String) As Mesh
            Dim mesh As New Mesh With {.Name = Path.GetFileNameWithoutExtension(filePath)}
            Dim anyColor = False

            For Each rawLine In File.ReadLines(filePath)
                Dim line = rawLine.Trim()
                If line.Length = 0 OrElse line(0) = "#"c Then Continue For

                Dim tokens = line.Split(Seps, StringSplitOptions.RemoveEmptyEntries)
                If tokens.Length < 3 Then Continue For

                Dim x As Double, y As Double, z As Double
                If Not Double.TryParse(tokens(0), NumberStyles.Float, IC, x) Then Continue For ' pomija ew. wiersz nagłówka
                If Not Double.TryParse(tokens(1), NumberStyles.Float, IC, y) Then Continue For
                If Not Double.TryParse(tokens(2), NumberStyles.Float, IC, z) Then Continue For

                mesh.Vertices.Add(New Vector3(CSng(x), CSng(y), CSng(z)))

                If tokens.Length >= 6 Then
                    Dim r As Double, g As Double, b As Double
                    If Double.TryParse(tokens(3), NumberStyles.Float, IC, r) AndAlso
                       Double.TryParse(tokens(4), NumberStyles.Float, IC, g) AndAlso
                       Double.TryParse(tokens(5), NumberStyles.Float, IC, b) Then
                        anyColor = True
                        ' kolory bywają zapisane 0..1 (float) albo 0..255 (int) - rozpoznaj po wartości
                        Dim scale = If(r > 1.0 OrElse g > 1.0 OrElse b > 1.0, 1.0, 255.0)
                        mesh.VertexColors.Add(New RgbColor(CByte(Math.Min(255, r * scale)), CByte(Math.Min(255, g * scale)), CByte(Math.Min(255, b * scale))))
                    Else
                        mesh.VertexColors.Add(RgbColor.Gray)
                    End If
                End If
            Next

            If Not anyColor Then mesh.VertexColors.Clear()
            Return mesh
        End Function

    End Module

    ''' <summary>Eksport chmury punktów (lub siatki - wtedy trójkąty są pomijane) do prostego formatu XYZ.</summary>
    Public Module XyzExporter

        Private ReadOnly IC As CultureInfo = CultureInfo.InvariantCulture

        Public Sub Save(mesh As Mesh, filePath As String)
            Using sw As New StreamWriter(filePath, append:=False)
                For i = 0 To mesh.Vertices.Count - 1
                    Dim v = mesh.Vertices(i)
                    If mesh.HasVertexColors Then
                        Dim c = mesh.VertexColors(i)
                        sw.WriteLine($"{v.X.ToString(IC)} {v.Y.ToString(IC)} {v.Z.ToString(IC)} {c.R} {c.G} {c.B}")
                    Else
                        sw.WriteLine($"{v.X.ToString(IC)} {v.Y.ToString(IC)} {v.Z.ToString(IC)}")
                    End If
                Next
            End Using
        End Sub

    End Module

End Namespace
