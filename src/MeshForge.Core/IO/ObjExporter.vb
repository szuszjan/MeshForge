Imports System.Globalization
Imports System.IO
Imports System.Text
Imports MeshForge.Core.Geometry

Namespace IO

    ''' <summary>Zapisuje siatkę do pliku Wavefront OBJ. Jeśli siatka ma teksturę, dopisuje plik MTL i kopiuje obraz obok eksportu.</summary>
    Public Module ObjExporter

        Private ReadOnly IC As CultureInfo = CultureInfo.InvariantCulture

        Public Sub Save(mesh As Mesh, filePath As String)
            Dim sb As New StringBuilder()
            sb.AppendLine("# Wyeksportowano ze MeshForge")

            Dim baseName = Path.GetFileNameWithoutExtension(filePath)
            Dim mtlName = baseName & ".mtl"
            Dim writeVertexColors = mesh.HasVertexColors AndAlso Not mesh.HasTexture

            If mesh.HasTexture Then
                sb.AppendLine($"mtllib {mtlName}")
                sb.AppendLine("usemtl material0")
            End If

            For i = 0 To mesh.Vertices.Count - 1
                Dim v = mesh.Vertices(i)
                If writeVertexColors Then
                    Dim c = mesh.VertexColors(i)
                    sb.AppendLine($"v {v.X.ToString(IC)} {v.Y.ToString(IC)} {v.Z.ToString(IC)} {(c.R / 255.0).ToString(IC)} {(c.G / 255.0).ToString(IC)} {(c.B / 255.0).ToString(IC)}")
                Else
                    sb.AppendLine($"v {v.X.ToString(IC)} {v.Y.ToString(IC)} {v.Z.ToString(IC)}")
                End If
            Next

            If mesh.HasUVs Then
                For Each uv In mesh.UVs
                    sb.AppendLine($"vt {uv.X.ToString(IC)} {uv.Y.ToString(IC)}")
                Next
            End If

            If mesh.HasNormals Then
                For Each n In mesh.Normals
                    sb.AppendLine($"vn {n.X.ToString(IC)} {n.Y.ToString(IC)} {n.Z.ToString(IC)}")
                Next
            End If

            Dim hasUV = mesh.HasUVs
            Dim hasN = mesh.HasNormals
            For Each t In mesh.Triangles
                sb.AppendLine($"f {FaceToken(t.A, hasUV, hasN)} {FaceToken(t.B, hasUV, hasN)} {FaceToken(t.C, hasUV, hasN)}")
            Next

            File.WriteAllText(filePath, sb.ToString())

            If mesh.HasTexture Then
                Dim outDir = Path.GetDirectoryName(Path.GetFullPath(filePath))
                Dim texExt = Path.GetExtension(mesh.TexturePath)
                Dim texOutName = baseName & "_texture" & texExt
                Dim texOutPath = Path.Combine(outDir, texOutName)
                Try
                    If File.Exists(mesh.TexturePath) AndAlso Path.GetFullPath(mesh.TexturePath) <> Path.GetFullPath(texOutPath) Then
                        File.Copy(mesh.TexturePath, texOutPath, overwrite:=True)
                    End If
                Catch
                    ' jeśli kopiowanie się nie uda, MTL wskaże bezpośrednio na oryginalną ścieżkę
                    texOutName = mesh.TexturePath
                End Try

                Dim mtl As New StringBuilder()
                mtl.AppendLine("newmtl material0")
                mtl.AppendLine("Ka 1.000 1.000 1.000")
                mtl.AppendLine("Kd 1.000 1.000 1.000")
                mtl.AppendLine("Ks 0.000 0.000 0.000")
                mtl.AppendLine($"map_Kd {texOutName}")
                File.WriteAllText(Path.Combine(outDir, mtlName), mtl.ToString())
            End If
        End Sub

        Private Function FaceToken(index As Integer, hasUV As Boolean, hasN As Boolean) As String
            Dim i1 = index + 1 ' OBJ jest 1-based
            If hasUV AndAlso hasN Then Return $"{i1}/{i1}/{i1}"
            If hasUV Then Return $"{i1}/{i1}"
            If hasN Then Return $"{i1}//{i1}"
            Return i1.ToString()
        End Function

    End Module

End Namespace
