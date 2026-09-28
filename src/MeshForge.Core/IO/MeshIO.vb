Imports System.IO
Imports MeshForge.Core.Geometry

Namespace IO

    ''' <summary>Fasada importu/eksportu - wybiera odpowiedni format na podstawie rozszerzenia pliku.</summary>
    Public Module MeshIO

        ''' <summary>Filtr do okna dialogowego "Otwórz" (WPF OpenFileDialog.Filter). Właściwość (nie pole!),
        ''' żeby zawsze odzwierciedlać AKTUALNY Loc.Current, nawet po zmianie języka w Preferencjach.</summary>
        Public ReadOnly Property ImportFilter As String
            Get
                Return Loc.Translated(
                    "Wszystkie wspierane pliki (*.obj;*.stl;*.ply;*.xyz;*.txt)|*.obj;*.stl;*.ply;*.xyz;*.txt|" &
                    "Wavefront OBJ (*.obj)|*.obj|" &
                    "Stereolithography STL (*.stl)|*.stl|" &
                    "Stanford PLY - ASCII (*.ply)|*.ply|" &
                    "Chmura punktów XYZ (*.xyz;*.txt)|*.xyz;*.txt|" &
                    "Wszystkie pliki (*.*)|*.*",
                    "All supported files (*.obj;*.stl;*.ply;*.xyz;*.txt)|*.obj;*.stl;*.ply;*.xyz;*.txt|" &
                    "Wavefront OBJ (*.obj)|*.obj|" &
                    "Stereolithography STL (*.stl)|*.stl|" &
                    "Stanford PLY - ASCII (*.ply)|*.ply|" &
                    "XYZ point cloud (*.xyz;*.txt)|*.xyz;*.txt|" &
                    "All files (*.*)|*.*")
            End Get
        End Property

        ''' <summary>Filtr do okna dialogowego "Zapisz jako" (WPF SaveFileDialog.Filter). Też właściwość - patrz komentarz przy ImportFilter.</summary>
        Public ReadOnly Property ExportFilter As String
            Get
                Return Loc.Translated(
                    "Wavefront OBJ - z teksturą/kolorem (*.obj)|*.obj|" &
                    "Stereolithography STL binarny (*.stl)|*.stl|" &
                    "Stanford PLY - ASCII, z kolorem (*.ply)|*.ply|" &
                    "Chmura punktów XYZ (*.xyz)|*.xyz",
                    "Wavefront OBJ - with texture/color (*.obj)|*.obj|" &
                    "Stereolithography STL binary (*.stl)|*.stl|" &
                    "Stanford PLY - ASCII, with color (*.ply)|*.ply|" &
                    "XYZ point cloud (*.xyz)|*.xyz")
            End Get
        End Property

        Public Function Load(filePath As String) As Mesh
            Dim ext = Path.GetExtension(filePath).ToLowerInvariant()
            Select Case ext
                Case ".obj"
                    Return ObjImporter.Load(filePath)
                Case ".stl"
                    Return StlImporter.Load(filePath)
                Case ".ply"
                    Return PlyImporter.Load(filePath)
                Case ".xyz", ".txt", ".pts", ".asc"
                    Return XyzImporter.Load(filePath)
                Case Else
                    Throw New NotSupportedException(Loc.Translated($"Nieobsługiwane rozszerzenie pliku: '{ext}'. Wspierane formaty: OBJ, STL, PLY (ASCII), XYZ/TXT.",
                                                           $"Unsupported file extension: '{ext}'. Supported formats: OBJ, STL, PLY (ASCII), XYZ/TXT."))
            End Select
        End Function

        Public Sub Save(mesh As Mesh, filePath As String)
            Dim ext = Path.GetExtension(filePath).ToLowerInvariant()
            Select Case ext
                Case ".obj"
                    ObjExporter.Save(mesh, filePath)
                Case ".stl"
                    StlExporter.Save(mesh, filePath)
                Case ".ply"
                    PlyExporter.Save(mesh, filePath)
                Case ".xyz", ".txt"
                    XyzExporter.Save(mesh, filePath)
                Case Else
                    Throw New NotSupportedException(Loc.Translated($"Nieobsługiwane rozszerzenie pliku: '{ext}'. Wspierane formaty eksportu: OBJ, STL, PLY, XYZ.",
                                                           $"Unsupported file extension: '{ext}'. Supported export formats: OBJ, STL, PLY, XYZ."))
            End Select
        End Sub

    End Module

End Namespace
