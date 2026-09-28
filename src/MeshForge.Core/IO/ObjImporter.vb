Imports System.Globalization
Imports System.IO
Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace IO

    ''' <summary>Wczytuje siatkę z pliku Wavefront OBJ (+ opcjonalnie towarzyszący plik MTL z teksturą).</summary>
    Public Module ObjImporter

        Private Const NF As NumberStyles = NumberStyles.Float
        Private ReadOnly IC As CultureInfo = CultureInfo.InvariantCulture

        Public Function Load(filePath As String) As Mesh
            Dim positions As New List(Of Vector3)
            Dim texCoords As New List(Of Vector2)
            Dim normals As New List(Of Vector3)

            ' unikalna kombinacja (pos, uv, normal) -> indeks w ujednoliconej siatce
            Dim unified As New Dictionary(Of (Integer, Integer, Integer), Integer)
            Dim mesh As New Mesh With {.Name = Path.GetFileNameWithoutExtension(filePath)}

            Dim mtlFile As String = ""
            Dim baseDir = Path.GetDirectoryName(Path.GetFullPath(filePath))

            Dim resolveVertex =
                Function(pIdx As Integer, tIdx As Integer, nIdx As Integer) As Integer
                    Dim key = (pIdx, tIdx, nIdx)
                    Dim existing As Integer
                    If unified.TryGetValue(key, existing) Then Return existing

                    Dim newIndex = mesh.Vertices.Count
                    mesh.Vertices.Add(positions(pIdx))

                    If tIdx >= 0 AndAlso tIdx < texCoords.Count Then
                        While mesh.UVs.Count < newIndex
                            mesh.UVs.Add(Vector2.Zero)
                        End While
                        mesh.UVs.Add(texCoords(tIdx))
                    End If

                    If nIdx >= 0 AndAlso nIdx < normals.Count Then
                        While mesh.Normals.Count < newIndex
                            mesh.Normals.Add(Vector3.UnitY)
                        End While
                        mesh.Normals.Add(normals(nIdx))
                    End If

                    unified(key) = newIndex
                    Return newIndex
                End Function

            For Each rawLine In File.ReadLines(filePath)
                Dim line = rawLine.Trim()
                If line.Length = 0 OrElse line(0) = "#"c Then Continue For

                Dim parts = line.Split(New Char() {" "c, ControlChars.Tab}, StringSplitOptions.RemoveEmptyEntries)
                If parts.Length = 0 Then Continue For

                Select Case parts(0)
                    Case "v"
                        Dim x = CSng(Double.Parse(parts(1), NF, IC))
                        Dim y = CSng(Double.Parse(parts(2), NF, IC))
                        Dim z = CSng(Double.Parse(parts(3), NF, IC))
                        positions.Add(New Vector3(x, y, z))

                        ' rozszerzenie nieformalne: "v x y z r g b" (kolor 0..1) - używane m.in. przez niektóre skanery/MeshLab
                        If parts.Length >= 7 Then
                            Dim r = Double.Parse(parts(4), NF, IC)
                            Dim g = Double.Parse(parts(5), NF, IC)
                            Dim b = Double.Parse(parts(6), NF, IC)
                            While mesh.VertexColors.Count < positions.Count - 1
                                mesh.VertexColors.Add(RgbColor.White)
                            End While
                            mesh.VertexColors.Add(New RgbColor(CByte(Clamp01(r) * 255), CByte(Clamp01(g) * 255), CByte(Clamp01(b) * 255)))
                        End If

                    Case "vt"
                        Dim u = CSng(Double.Parse(parts(1), NF, IC))
                        Dim v = CSng(Double.Parse(parts(2), NF, IC))
                        texCoords.Add(New Vector2(u, v))

                    Case "vn"
                        Dim x = CSng(Double.Parse(parts(1), NF, IC))
                        Dim y = CSng(Double.Parse(parts(2), NF, IC))
                        Dim z = CSng(Double.Parse(parts(3), NF, IC))
                        normals.Add(New Vector3(x, y, z))

                    Case "f"
                        Dim faceVerts As New List(Of Integer)
                        For i = 1 To parts.Length - 1
                            Dim token = parts(i)
                            Dim pIdx As Integer = -1, tIdx As Integer = -1, nIdx As Integer = -1
                            Dim segs = token.Split("/"c)

                            pIdx = ResolveIndex(segs(0), positions.Count)
                            If segs.Length >= 2 AndAlso segs(1).Length > 0 Then
                                tIdx = ResolveIndex(segs(1), texCoords.Count)
                            End If
                            If segs.Length >= 3 AndAlso segs(2).Length > 0 Then
                                nIdx = ResolveIndex(segs(2), normals.Count)
                            End If

                            faceVerts.Add(resolveVertex(pIdx, tIdx, nIdx))
                        Next

                        ' triangulacja wachlarzowa dla wielokątów (quady itp.)
                        For i = 1 To faceVerts.Count - 2
                            mesh.Triangles.Add(New Triangle(faceVerts(0), faceVerts(i), faceVerts(i + 1)))
                        Next

                    Case "mtllib"
                        mtlFile = line.Substring(7).Trim()

                    Case Else
                        ' usemtl, o, g, s itp. - pomijane w wersji 1
                End Select
            Next

            ' domknij listy UV/kolorów do pełnej długości (na wypadek gdy nie każdy wierzchołek miał UV/kolor)
            While mesh.UVs.Count > 0 AndAlso mesh.UVs.Count < mesh.Vertices.Count
                mesh.UVs.Add(Vector2.Zero)
            End While
            While mesh.Normals.Count > 0 AndAlso mesh.Normals.Count < mesh.Vertices.Count
                mesh.Normals.Add(Vector3.UnitY)
            End While
            While mesh.VertexColors.Count > 0 AndAlso mesh.VertexColors.Count < mesh.Vertices.Count
                mesh.VertexColors.Add(RgbColor.White)
            End While

            If Not String.IsNullOrEmpty(mtlFile) Then
                Dim mtlPath = Path.Combine(baseDir, mtlFile)
                If File.Exists(mtlPath) Then
                    Dim texturePath = TryFindDiffuseTexture(mtlPath)
                    If Not String.IsNullOrEmpty(texturePath) Then
                        mesh.TexturePath = texturePath
                    End If
                End If
            End If

            Return mesh
        End Function

        Private Function Clamp01(v As Double) As Double
            Return Math.Max(0.0, Math.Min(1.0, v))
        End Function

        ''' <summary>Indeksy OBJ są 1-based; ujemne oznaczają odniesienie względne od końca listy.</summary>
        Private Function ResolveIndex(token As String, currentCount As Integer) As Integer
            Dim n = Integer.Parse(token, IC)
            If n > 0 Then Return n - 1
            Return currentCount + n ' n jest ujemne
        End Function

        Private Function TryFindDiffuseTexture(mtlPath As String) As String
            Dim baseDir = Path.GetDirectoryName(mtlPath)
            For Each rawLine In File.ReadLines(mtlPath)
                Dim line = rawLine.Trim()
                If line.StartsWith("map_Kd", StringComparison.OrdinalIgnoreCase) Then
                    Dim texName = line.Substring(6).Trim()
                    Dim fullPath = Path.Combine(baseDir, texName)
                    If File.Exists(fullPath) Then Return fullPath
                End If
            Next
            Return ""
        End Function

    End Module

End Namespace
