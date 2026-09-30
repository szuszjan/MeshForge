Imports System.Numerics

Namespace Geometry

    ''' <summary>Zestaw statystyk siatki pokazywany w UI (pasek stanu / panel informacyjny).</summary>
    Public Class MeshStatistics
        Public Property VertexCount As Integer
        Public Property TriangleCount As Integer
        Public Property BoundsMin As Vector3
        Public Property BoundsMax As Vector3
        Public Property SurfaceArea As Double
        Public Property Volume As Double
        Public Property IsWatertight As Boolean
        Public Property OpenBoundaryEdgeCount As Integer
        Public Property HasVertexColors As Boolean
        Public Property HasTexture As Boolean

        Public ReadOnly Property Size As Vector3
            Get
                Return BoundsMax - BoundsMin
            End Get
        End Property

        Public Shared Function Compute(mesh As Mesh) As MeshStatistics
            Dim stats As New MeshStatistics With {
                .VertexCount = mesh.VertexCount,
                .TriangleCount = mesh.TriangleCount,
                .HasVertexColors = mesh.HasVertexColors,
                .HasTexture = mesh.HasTexture
            }

            Dim bbox = mesh.GetBoundingBox()
            stats.BoundsMin = bbox.min
            stats.BoundsMax = bbox.max

            Dim area As Double = 0
            Dim volume As Double = 0
            For Each t In mesh.Triangles
                Dim p0 = mesh.Vertices(t.A)
                Dim p1 = mesh.Vertices(t.B)
                Dim p2 = mesh.Vertices(t.C)
                Dim cross = Vector3.Cross(p1 - p0, p2 - p0)
                area += cross.Length() * 0.5

                ' suma objętości podpisanych czworościanów (początek + trójkąt) - działa dla siatek zamkniętych
                volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6.0
            Next
            stats.SurfaceArea = area
            stats.Volume = Math.Abs(volume)

            Dim boundaryEdges = MeshTopology.FindBoundaryDirectedEdges(mesh)
            stats.OpenBoundaryEdgeCount = boundaryEdges.Count
            stats.IsWatertight = boundaryEdges.Count = 0

            Return stats
        End Function

        Public Overrides Function ToString() As String
            Return ToString("")
        End Function

        ''' <summary>Jak ToString(), ale z opcjonalnym sufiksem jednostki (np. "mm") doklejonym do wymiarów/pola/objętości - czysto kosmetyczne, Core nie wie nic o "preferencjach", to tylko string od wywołującego.</summary>
        Public Overloads Function ToString(unitSuffix As String) As String
            Dim yes = Loc.Translated("tak", "yes")
            Dim no = Loc.Translated("nie", "no")
            Dim lenU = unitSuffix
            Dim areaU = If(String.IsNullOrEmpty(unitSuffix), "", unitSuffix & "²")
            Dim volU = If(String.IsNullOrEmpty(unitSuffix), "", unitSuffix & "³")
            Dim sb As New Text.StringBuilder()
            sb.AppendLine(Loc.Translated($"Wierzchołki: {VertexCount:N0}    Trójkąty: {TriangleCount:N0}", $"Vertices: {VertexCount:N0}    Triangles: {TriangleCount:N0}"))
            sb.AppendLine(Loc.Translated($"Wymiary (SxWxG): {Size.X:F2} x {Size.Y:F2} x {Size.Z:F2} {lenU}", $"Size (WxHxD): {Size.X:F2} x {Size.Y:F2} x {Size.Z:F2} {lenU}"))
            sb.AppendLine(Loc.Translated($"Pole powierzchni: {SurfaceArea:F3} {areaU}    Objętość (przybliżona): {Volume:F3} {volU}", $"Surface area: {SurfaceArea:F3} {areaU}    Volume (approx.): {Volume:F3} {volU}"))
            sb.AppendLine(Loc.Translated($"Wodoszczelna: {If(IsWatertight, yes, $"{no} ({OpenBoundaryEdgeCount} krawędzi brzegowych)")}",
                                 $"Watertight: {If(IsWatertight, yes, $"{no} ({OpenBoundaryEdgeCount} boundary edges)")}"))
            sb.AppendLine(Loc.Translated($"Kolor wierzchołków: {If(HasVertexColors, yes, no)}    Tekstura: {If(HasTexture, yes, no)}",
                                 $"Vertex colors: {If(HasVertexColors, yes, no)}    Texture: {If(HasTexture, yes, no)}"))
            Return sb.ToString()
        End Function
    End Class

End Namespace
