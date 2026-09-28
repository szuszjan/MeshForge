Imports System.IO
Imports System.Numerics
Imports MeshForge.Core.Geometry

Namespace Algorithms

    ''' <summary>Sposób rzutowania współrzędnych UV na siatkę - decyduje, jak zdjęcie "owija się" wokół modelu.</summary>
    Public Enum ProjectionType
        ''' <summary>Rzut płaski wzdłuż osi o najmniejszym wymiarze modelu (jak "zdjęcie z przodu") - dobre dla płaskich/prawie płaskich skanów.</summary>
        Planar
        ''' <summary>Rzut cylindryczny wokół osi Y (pionowej) - dobre dla przedmiotów obrotowych: kubki, słupki, figury.</summary>
        Cylindrical
        ''' <summary>Rzut sferyczny od środka modelu - dobre dla obiektów zbliżonych do kuli/głowy.</summary>
        Spherical
    End Enum

    ''' <summary>
    ''' Nakłada kolorową teksturę ze zdjęcia na siatkę. Wersja 1: mapowanie przez rzutowanie (planarne/cylindryczne/sferyczne)
    ''' pojedynczego zdjęcia - nie jest to pełna fotogrametria wielo-zdjęciowa (jak RealityCapture/Metashape),
    ''' ale wystarcza do szybkiego pokolorowania modelu jednym zdjęciem referencyjnym.
    ''' </summary>
    Public Module Texturizer

        Public Function GenerateUVs(mesh As Mesh, projection As ProjectionType) As OperationResult
            If mesh.VertexCount = 0 Then Return OperationResult.Fail(Loc.Translated("Siatka jest pusta.", "The mesh is empty."))

            Dim bbox = mesh.GetBoundingBox()
            Dim size = bbox.max - bbox.min
            Dim center = (bbox.min + bbox.max) * 0.5F

            Dim uvs As New List(Of Vector2)(mesh.VertexCount)

            Select Case projection
                Case ProjectionType.Planar
                    ' rzutuj wzdłuż osi o najmniejszym wymiarze (oś "grubości") na pozostałe dwie
                    Dim axis = SmallestAxis(size)
                    For Each p In mesh.Vertices
                        Dim u As Single, v As Single
                        Select Case axis
                            Case 0 ' oś X najmniejsza -> rzutuj na (Y,Z)
                                u = SafeNormalize(p.Y, bbox.min.Y, bbox.max.Y)
                                v = SafeNormalize(p.Z, bbox.min.Z, bbox.max.Z)
                            Case 1 ' oś Y najmniejsza -> rzutuj na (X,Z)
                                u = SafeNormalize(p.X, bbox.min.X, bbox.max.X)
                                v = SafeNormalize(p.Z, bbox.min.Z, bbox.max.Z)
                            Case Else ' oś Z najmniejsza -> rzutuj na (X,Y)
                                u = SafeNormalize(p.X, bbox.min.X, bbox.max.X)
                                v = SafeNormalize(p.Y, bbox.min.Y, bbox.max.Y)
                        End Select
                        uvs.Add(New Vector2(u, v))
                    Next

                Case ProjectionType.Cylindrical
                    For Each p In mesh.Vertices
                        Dim u = CSng(Math.Atan2(p.Z - center.Z, p.X - center.X) / (2 * Math.PI) + 0.5)
                        Dim v = SafeNormalize(p.Y, bbox.min.Y, bbox.max.Y)
                        uvs.Add(New Vector2(u, v))
                    Next

                Case ProjectionType.Spherical
                    For Each p In mesh.Vertices
                        Dim dir = p - center
                        If dir.LengthSquared() < 0.0000001F Then dir = Vector3.UnitY
                        dir = Vector3.Normalize(dir)
                        Dim u = CSng(Math.Atan2(dir.Z, dir.X) / (2 * Math.PI) + 0.5)
                        Dim clampedY = Math.Max(-1.0F, Math.Min(1.0F, dir.Y))
                        Dim v = CSng(Math.Acos(clampedY) / Math.PI)
                        uvs.Add(New Vector2(u, v))
                    Next
            End Select

            mesh.UVs.Clear()
            mesh.UVs.AddRange(uvs)

            Return OperationResult.Ok(Loc.Translated($"Wygenerowano współrzędne UV ({ProjectionName(projection)}) dla {mesh.VertexCount:N0} wierzchołków.",
                                             $"Generated UV coordinates ({ProjectionName(projection)}) for {mesh.VertexCount:N0} vertices."))
        End Function

        ''' <summary>Generuje UV (jeśli trzeba) i podpina zdjęcie jako teksturę kolorową modelu.</summary>
        Public Function ApplyPhotoTexture(mesh As Mesh, imagePath As String, projection As ProjectionType) As OperationResult
            If Not File.Exists(imagePath) Then Return OperationResult.Fail(Loc.Translated($"Nie znaleziono pliku zdjęcia: {imagePath}", $"Photo file not found: {imagePath}"))

            Dim uvResult = GenerateUVs(mesh, projection)
            If Not uvResult.Success Then Return uvResult

            mesh.TexturePath = imagePath
            Return OperationResult.Ok(
                Loc.Translated($"Nałożono teksturę '{Path.GetFileName(imagePath)}' (rzut: {ProjectionName(projection)}).",
                      $"Applied texture '{Path.GetFileName(imagePath)}' (projection: {ProjectionName(projection)})."),
                Loc.Translated("Jeśli tekstura wygląda na rozjechaną, spróbuj innego typu rzutu dopasowanego do kształtu modelu.",
                      "If the texture looks distorted, try a different projection type matching the model's shape."))
        End Function

        Private Function ProjectionName(p As ProjectionType) As String
            Select Case p
                Case ProjectionType.Planar : Return Loc.Translated("planarny", "planar")
                Case ProjectionType.Cylindrical : Return Loc.Translated("cylindryczny", "cylindrical")
                Case ProjectionType.Spherical : Return Loc.Translated("sferyczny", "spherical")
                Case Else : Return p.ToString()
            End Select
        End Function

        Private Function SmallestAxis(size As Vector3) As Integer
            If size.X <= size.Y AndAlso size.X <= size.Z Then Return 0
            If size.Y <= size.X AndAlso size.Y <= size.Z Then Return 1
            Return 2
        End Function

        Private Function SafeNormalize(value As Single, lo As Single, hi As Single) As Single
            Dim range = hi - lo
            If range <= 0.0000001F Then Return 0.5F
            Return (value - lo) / range
        End Function

    End Module

End Namespace
