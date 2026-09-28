Imports System.Globalization
Imports System.IO
Imports System.Numerics
Imports System.Text
Imports MeshForge.Core.Geometry

Namespace IO

    ''' <summary>Import pliku STL (ASCII i binarny, z automatycznym wykrywaniem formatu).</summary>
    Public Module StlImporter

        Private ReadOnly IC As CultureInfo = CultureInfo.InvariantCulture

        Public Function Load(filePath As String) As Mesh
            Dim bytes = File.ReadAllBytes(filePath)
            If IsBinary(bytes) Then
                Return LoadBinary(bytes, Path.GetFileNameWithoutExtension(filePath))
            Else
                Return LoadAscii(filePath)
            End If
        End Function

        ''' <summary>
        ''' STL binarne i ASCII oba mogą zaczynać się od słowa "solid", więc same nagłówka nie da się użyć.
        ''' Rozstrzygamy porównując deklarowaną w nagłówku binarnym liczbę trójkątów z rzeczywistym rozmiarem pliku.
        ''' </summary>
        Private Function IsBinary(bytes As Byte()) As Boolean
            If bytes.Length < 84 Then Return False ' za krótki na poprawny plik binarny - spróbuj parsować jako ASCII
            Dim header = Encoding.ASCII.GetString(bytes, 0, Math.Min(5, bytes.Length))
            If Not header.Equals("solid", StringComparison.OrdinalIgnoreCase) Then Return True

            Dim triCount = BitConverter.ToUInt32(bytes, 80)
            Dim expectedBinarySize = 84L + CLng(triCount) * 50L
            Return bytes.LongLength = expectedBinarySize
        End Function

        Private Function LoadBinary(bytes As Byte(), name As String) As Mesh
            Using ms As New MemoryStream(bytes)
                Using br As New BinaryReader(ms)
                    br.ReadBytes(80) ' nagłówek (ignorowany)
                    Dim triCount = CInt(br.ReadUInt32())

                    Dim positions As New List(Of Vector3)(triCount * 3)
                    For i = 1 To triCount
                        br.ReadSingle() : br.ReadSingle() : br.ReadSingle() ' normalna trójkąta - pomijamy, przeliczymy własne
                        For v = 1 To 3
                            Dim x = br.ReadSingle()
                            Dim y = br.ReadSingle()
                            Dim z = br.ReadSingle()
                            positions.Add(New Vector3(x, y, z))
                        Next
                        br.ReadUInt16() ' attribute byte count
                    Next

                    Return BuildWeldedMesh(positions, name)
                End Using
            End Using
        End Function

        Private Function LoadAscii(filePath As String) As Mesh
            Dim positions As New List(Of Vector3)
            For Each rawLine In File.ReadLines(filePath)
                Dim line = rawLine.Trim()
                If line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase) Then
                    Dim parts = line.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                    If parts.Length >= 4 Then
                        Dim x = CSng(Double.Parse(parts(1), NumberStyles.Float, IC))
                        Dim y = CSng(Double.Parse(parts(2), NumberStyles.Float, IC))
                        Dim z = CSng(Double.Parse(parts(3), NumberStyles.Float, IC))
                        positions.Add(New Vector3(x, y, z))
                    End If
                End If
            Next
            Return BuildWeldedMesh(positions, Path.GetFileNameWithoutExtension(filePath))
        End Function

        Private Function BuildWeldedMesh(rawPositions As List(Of Vector3), name As String) As Mesh
            Dim mesh As New Mesh With {.Name = name}
            If rawPositions.Count = 0 Then Return mesh

            Dim epsilon = VertexWelder.SuggestEpsilon(rawPositions)
            Dim welded = VertexWelder.WeldPositions(rawPositions, epsilon)
            mesh.Vertices.AddRange(welded.uniquePositions)

            For i = 0 To rawPositions.Count - 1 Step 3
                Dim a = welded.mapping(i)
                Dim b = welded.mapping(i + 1)
                Dim c = welded.mapping(i + 2)
                Dim tri As New Triangle(a, b, c)
                If Not tri.IsDegenerate Then mesh.Triangles.Add(tri)
            Next

            Return mesh
        End Function

    End Module

    ''' <summary>Eksport do binarnego STL (kompaktowy, szeroko wspierany format; nie przenosi kolorów/tekstury).</summary>
    Public Module StlExporter

        Public Sub Save(mesh As Mesh, filePath As String)
            Using fs As New FileStream(filePath, FileMode.Create, FileAccess.Write)
                Using bw As New BinaryWriter(fs)
                    Dim header = New Byte(79) {}
                    Dim headerText = Encoding.ASCII.GetBytes("MeshForge STL export")
                    Array.Copy(headerText, header, Math.Min(headerText.Length, 80))
                    bw.Write(header)

                    bw.Write(CUInt(mesh.Triangles.Count))

                    For Each t In mesh.Triangles
                        Dim p0 = mesh.Vertices(t.A)
                        Dim p1 = mesh.Vertices(t.B)
                        Dim p2 = mesh.Vertices(t.C)
                        Dim n = Vector3.Cross(p1 - p0, p2 - p0)
                        If n.LengthSquared() > 0 Then n = Vector3.Normalize(n)

                        bw.Write(n.X) : bw.Write(n.Y) : bw.Write(n.Z)
                        bw.Write(p0.X) : bw.Write(p0.Y) : bw.Write(p0.Z)
                        bw.Write(p1.X) : bw.Write(p1.Y) : bw.Write(p1.Z)
                        bw.Write(p2.X) : bw.Write(p2.Y) : bw.Write(p2.Z)
                        bw.Write(CUShort(0))
                    Next
                End Using
            End Using
        End Sub

    End Module

End Namespace
