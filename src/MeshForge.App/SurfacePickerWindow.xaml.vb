Imports System.Numerics
Imports System.Windows.Media.Media3D
Imports MeshForge.Core.Algorithms
Imports MeshForge.Core.Geometry

''' <summary>
''' Pokazuje wykryte płaskie powierzchnie modelu bezpośrednio na podglądzie 3D (tak jak funkcja
''' "postaw na ściance" w Orca/PrusaSlicer) i pozwala wybrać, która ma się stać podstawą - kliknięciem
''' na liście albo bezpośrednio na podświetlonej powierzchni w podglądzie.
''' </summary>
Class SurfacePickerWindow

    Public Property SelectedCandidate As FlatCandidate

    Private ReadOnly _candidates As List(Of FlatCandidate)
    Private ReadOnly _patchVisuals As New List(Of ModelUIElement3D)
    Private ReadOnly _patchIndexByVisual As New Dictionary(Of ModelUIElement3D, Integer)
    Private _hoveredIndex As Integer = -1
    Private _normalOffset As Single = 0

    Public Sub New(mesh As Mesh, candidates As List(Of FlatCandidate))
        InitializeComponent()
        _candidates = candidates
        ListCandidates.ItemsSource = candidates

        BuildScene(mesh)

        If candidates.Count > 0 Then ListCandidates.SelectedIndex = 0
    End Sub

    ''' <summary>Przygotowuje podgląd 3D: szary model siatki plus obliczenie odsunięcia nakładek. Same nakładki buduje RefreshHighlightMaterials (wywołane jako pierwsze przez SelectedIndex=0 w Sub New).</summary>
    Private Sub BuildScene(mesh As Mesh)
        MeshVisualHost.Content = MeshVisualBuilder.BuildModel(mesh)

        Dim bbox = mesh.GetBoundingBox()
        Dim diag = (bbox.max - bbox.min).Length()
        _normalOffset = diag * 0.0015F ' odsunięcie nakładki od prawdziwej powierzchni (proporcjonalne do rozmiaru modelu) - unika "migotania" z siatką bazową
    End Sub

    ''' <summary>BackMaterial = ta sama nakładka jest widoczna niezależnie od nawinięcia trójkątów (tak jak baza siatki w MeshVisualBuilder) - bez tego część powierzchni potrafiła nie renderować się wcale.</summary>
    Private Function BuildPatchVisual(candidate As FlatCandidate, isSelected As Boolean, isHovered As Boolean) As ModelUIElement3D
        Dim geometry As New MeshGeometry3D()
        Dim offset = candidate.Normal * _normalOffset
        Dim n = ToVector3D(candidate.Normal)

        For Each tri In candidate.Triangles
            Dim baseIndex = geometry.Positions.Count
            geometry.Positions.Add(ToPoint3D(tri.A + offset))
            geometry.Positions.Add(ToPoint3D(tri.B + offset))
            geometry.Positions.Add(ToPoint3D(tri.C + offset))
            geometry.Normals.Add(n)
            geometry.Normals.Add(n)
            geometry.Normals.Add(n)
            geometry.TriangleIndices.Add(baseIndex)
            geometry.TriangleIndices.Add(baseIndex + 1)
            geometry.TriangleIndices.Add(baseIndex + 2)
        Next

        Dim mat = MakePatchMaterial(isSelected, isHovered)
        Dim model As New GeometryModel3D(geometry, mat) With {.BackMaterial = mat}
        Return New ModelUIElement3D With {.Model = model}
    End Function

    Private Sub PatchVisual_MouseDown(sender As Object, e As MouseButtonEventArgs)
        If e.ChangedButton <> MouseButton.Left Then Return
        Dim visual = TryCast(sender, ModelUIElement3D)
        If visual Is Nothing Then Return
        Dim index As Integer
        If _patchIndexByVisual.TryGetValue(visual, index) Then
            ListCandidates.SelectedIndex = index
            e.Handled = True
        End If
    End Sub

    Private Sub PatchVisual_MouseEnter(sender As Object, e As MouseEventArgs)
        Dim visual = TryCast(sender, ModelUIElement3D)
        If visual Is Nothing Then Return
        Dim index As Integer
        If _patchIndexByVisual.TryGetValue(visual, index) Then SetHovered(index)
    End Sub

    Private Sub PatchVisual_MouseLeave(sender As Object, e As MouseEventArgs)
        SetHovered(-1)
    End Sub

    Private Sub SetHovered(index As Integer)
        If _hoveredIndex = index Then Return
        _hoveredIndex = index
        Viewport.Cursor = If(index >= 0, Cursors.Hand, Cursors.Arrow)
        RefreshHighlightMaterials()
    End Sub

    Private Sub ListCandidates_SelectionChanged(sender As Object, e As SelectionChangedEventArgs) Handles ListCandidates.SelectionChanged
        RefreshHighlightMaterials()
    End Sub

    ''' <summary>
    ''' Odbudowuje CAŁKOWICIE wszystkie nakładki powierzchni (nowe ModelUIElement3D, nie tylko nowy Material)
    ''' wg stanu: wybrana (jasna, "świecąca") > pod kursorem > zwykła (delikatna, ale widoczna - żeby było
    ''' widać, gdzie można kliknąć). Samo podmienianie Material/Model na już dodanym do drzewa wizualnego
    ''' ModelUIElement3D potrafi nie odświeżyć obrazu (zweryfikowane empirycznie - stan logiczny poprawny,
    ''' podgląd 3D się nie zmieniał) - pełne usunięcie i dodanie od nowa jest niezawodne.
    ''' </summary>
    Private Sub RefreshHighlightMaterials()
        Dim selectedIndex = ListCandidates.SelectedIndex

        For Each visual In _patchVisuals
            RemoveHandler visual.MouseDown, AddressOf PatchVisual_MouseDown
            RemoveHandler visual.MouseEnter, AddressOf PatchVisual_MouseEnter
            RemoveHandler visual.MouseLeave, AddressOf PatchVisual_MouseLeave
        Next
        PatchVisualHost.Children.Clear()
        _patchVisuals.Clear()
        _patchIndexByVisual.Clear()

        For i = 0 To _candidates.Count - 1
            Dim visual = BuildPatchVisual(_candidates(i), isSelected:=(i = selectedIndex), isHovered:=(i = _hoveredIndex))
            _patchIndexByVisual(visual) = i
            AddHandler visual.MouseDown, AddressOf PatchVisual_MouseDown
            AddHandler visual.MouseEnter, AddressOf PatchVisual_MouseEnter
            AddHandler visual.MouseLeave, AddressOf PatchVisual_MouseLeave
            _patchVisuals.Add(visual)
            PatchVisualHost.Children.Add(visual)
        Next
    End Sub

    Private Function MakePatchMaterial(isSelected As Boolean, isHovered As Boolean) As Material
        Dim accent = ThemeColor("AccentBrush")
        Dim group As New MaterialGroup()
        If isSelected Then
            group.Children.Add(New DiffuseMaterial(New SolidColorBrush(Color.FromArgb(235, accent.R, accent.G, accent.B))))
            group.Children.Add(New EmissiveMaterial(New SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B))))
        ElseIf isHovered Then
            group.Children.Add(New DiffuseMaterial(New SolidColorBrush(Color.FromArgb(170, accent.R, accent.G, accent.B))))
        Else
            group.Children.Add(New DiffuseMaterial(New SolidColorBrush(Color.FromArgb(70, accent.R, accent.G, accent.B))))
        End If
        Return group
    End Function

    Private Function ThemeColor(key As String) As Color
        Return CType(Me.FindResource(key), SolidColorBrush).Color
    End Function

    Private Function ToPoint3D(v As Vector3) As Point3D
        Return New Point3D(v.X, v.Y, v.Z)
    End Function

    Private Function ToVector3D(v As Vector3) As Vector3D
        Return New Vector3D(v.X, v.Y, v.Z)
    End Function

    Private Sub BtnApply_Click(sender As Object, e As RoutedEventArgs) Handles BtnApply.Click
        Dim chosen = TryCast(ListCandidates.SelectedItem, FlatCandidate)
        If chosen Is Nothing Then Return
        SelectedCandidate = chosen
        Me.DialogResult = True
        Me.Close()
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs) Handles BtnCancel.Click
        Me.DialogResult = False
        Me.Close()
    End Sub

End Class
