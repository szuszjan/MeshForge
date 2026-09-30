Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Numerics
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Input
Imports System.Windows.Interop
Imports System.Windows.Media.Media3D
Imports HelixToolkit.Wpf
Imports Microsoft.Win32
Imports MeshForge.Core
Imports MeshForge.Core.Algorithms
Imports MeshForge.Core.Geometry
Imports MeshForge.Core.IO

''' <summary>
''' Główne okno aplikacji. Trzyma bieżąco wczytaną siatkę (_currentMesh), historię cofania (_undoHistory)
''' i deleguje wszystkie operacje geometryczne do MeshForge.Core - to okno tylko wywołuje Core, loguje
''' wynik i odświeża podgląd 3D.
''' </summary>
Class MainWindow

    Private _currentMesh As Mesh

    ''' <summary>
    ''' Historia w stylu "pasek osi czasu" (Fusion 360): _historyBase to stan tuż po wczytaniu pliku
    ''' (nigdy nie mutowany), _history to kolejne kroki (etykieta + pełna migawka siatki PO operacji),
    ''' _historyIndex wskazuje, na którym kroku aktualnie jesteśmy (-1 = na _historyBase). Kliknięcie
    ''' dowolnego kroku na pasku (JumpToHistory) przeskakuje tam bezpośrednio - cofanie/ponawianie to
    ''' po prostu przesunięcie _historyIndex o 1. Nowa operacja wykonana PO cofnięciu się obcina
    ''' "przyszłość" (standardowe zachowanie undo/redo w każdym edytorze).
    ''' </summary>
    Private _historyBase As Mesh
    Private ReadOnly _history As New List(Of HistoryStep)
    Private _historyIndex As Integer = -1

    Private _pendingTexturePath As String = ""
    Private _showWireframe As Boolean = False

    ''' <summary>Aktywne proste narzędzie paska na górze (None = normalna obsługa myszy przez kamerę Viewportu).</summary>
    Private Enum ToolMode
        None
        Rotate
        Lasso
        Extrude
    End Enum
    Private _activeTool As ToolMode = ToolMode.None
    Private ReadOnly _lassoPoints As New List(Of Point)
    Private _isDrawingLasso As Boolean = False
    Private ReadOnly _lassoSelectedTriangles As New HashSet(Of Integer)

    Public Sub New()
        InitializeComponent()
        RefreshComboBoxItemTexts()
        RestorePreferenceDefaults()
        RefreshViewportAndStats()
        RefreshHistoryBar()
    End Sub

    ''' <summary>Wczytuje zapamiętane preferencje (ostatnie parametry operacji, stan siatki krawędzi) do kontrolek - wołane raz przy starcie.</summary>
    Private Sub RestorePreferenceDefaults()
        TxtSmoothIterations.Text = AppSettings.LastSmoothIterations.ToString()
        TxtSmoothFactor.Text = AppSettings.LastSmoothStrength.ToString(CultureInfo.InvariantCulture)
        TxtSubdivideIterations.Text = AppSettings.LastSubdivideIterations.ToString()
        TxtDecimatePercent.Text = AppSettings.LastDecimatePercent.ToString(CultureInfo.InvariantCulture)

        _showWireframe = AppSettings.ShowWireframe
        MenuToggleWireframe.IsChecked = _showWireframe
    End Sub

    ''' <summary>
    ''' Ustawia teksty pozycji ComboBoxa z kodu (zwykły string), zamiast przez {DynamicResource} w XAML -
    ''' WPF ma znany problem z renderowaniem DynamicResource w zamkniętym polu wyboru ComboBoxa (pole
    ''' potrafi zostać puste, mimo że pozycja jest poprawnie zaznaczona - zweryfikowane empirycznie).
    ''' Wołane raz przy starcie i ponownie po zamknięciu Preferencji (gdyby język się zmienił).
    ''' </summary>
    Private Sub RefreshComboBoxItemTexts()
        CmbProjectionPlanar.Content = UiStr("Str_ProjectionPlanar")
        CmbProjectionCylindrical.Content = UiStr("Str_ProjectionCylindrical")
        CmbProjectionSpherical.Content = UiStr("Str_ProjectionSpherical")
    End Sub

    ''' <summary>Odczytuje bieżący (aktualnie wybrany językowo) napis interfejsu ze słownika zasobów - patrz Styles/Lang.*.xaml.</summary>
    Private Function UiStr(key As String) As String
        Return CStr(Me.FindResource(key))
    End Function

    ''' <summary>Uchwyt okna (HWND) już istnieje w tym momencie (w przeciwieństwie do Sub New) - dopiero teraz można poprosić DWM o ciemny pasek tytułu.</summary>
    Private Sub MainWindow_SourceInitialized(sender As Object, e As EventArgs) Handles Me.SourceInitialized
        ApplyTitleBarTheme(AppSettings.IsDarkMode)
    End Sub

    <DllImport("dwmapi.dll")>
    Private Shared Function DwmSetWindowAttribute(hwnd As IntPtr, attribute As Integer, ByRef value As Integer, size As Integer) As Integer
    End Function

    ''' <summary>Ciemny/jasny natywny pasek tytułu Windows (DWMWA_USE_IMMERSIVE_DARK_MODE) - kosmetyka, żeby pasował do reszty okna. Na starszych Windows (bez wsparcia) po prostu nic się nie stanie. Publiczne, bo woła to też PreferencesWindow.</summary>
    Public Sub ApplyTitleBarTheme(dark As Boolean)
        Try
            Dim hwnd = New WindowInteropHelper(Me).Handle
            If hwnd = IntPtr.Zero Then Return
            Dim useDark = If(dark, 1, 0)
            Const DWMWA_USE_IMMERSIVE_DARK_MODE = 20
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, useDark, Marshal.SizeOf(Of Integer)())
        Catch
            ' brak wsparcia (starszy Windows) - zostaw domyślny jasny pasek tytułu, to tylko kosmetyka
        End Try
    End Sub

#Region "Plik / Edycja / Widok / Preferencje - menu"

    Private Async Sub MenuOpen_Click(sender As Object, e As RoutedEventArgs) Handles MenuOpen.Click
        Dim dlg As New OpenFileDialog With {.Filter = MeshIO.ImportFilter, .Title = UiStr("Str_MenuOpen")}
        If dlg.ShowDialog() <> True Then Return

        SetBusy(True)
        Try
            Dim selectedPath = dlg.FileName
            Dim loaded = Await Task.Run(Function() MeshIO.Load(selectedPath))
            _currentMesh = loaded
            _historyBase = loaded.Clone()
            _history.Clear()
            _historyIndex = -1
            _pendingTexturePath = ""
            TxtSelectedPhoto.Text = UiStr("Str_NoPhotoSelected")
            AppendLog($"{UiStr("Str_LogLoaded")} '{Path.GetFileName(selectedPath)}': {_currentMesh.VertexCount:N0} {UiStr("Str_LogVertices")}, {_currentMesh.TriangleCount:N0} {UiStr("Str_LogTriangles")}.")
            RefreshViewportAndStats()
            RefreshHistoryBar()
            Viewport.ZoomExtents()
        Catch ex As Exception
            MessageBox.Show(Me, $"{UiStr("Str_MsgOpenError")}{Environment.NewLine}{ex.Message}", UiStr("Str_MsgOpenErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            SetBusy(False)
        End Try
    End Sub

    Private Async Sub MenuSaveAs_Click(sender As Object, e As RoutedEventArgs) Handles MenuSaveAs.Click
        If Not EnsureMeshLoaded() Then Return
        Dim dlg As New SaveFileDialog With {.Filter = MeshIO.ExportFilter, .Title = UiStr("Str_MenuSaveAs")}
        If dlg.ShowDialog() <> True Then Return

        SetBusy(True)
        Try
            Dim selectedPath = dlg.FileName
            Dim meshToSave = _currentMesh
            Await Task.Run(Sub() MeshIO.Save(meshToSave, selectedPath))
            AppendLog($"{UiStr("Str_LogSaved")} '{Path.GetFileName(selectedPath)}'.")
            StatusText.Text = $"{UiStr("Str_LogSavedTo")} {selectedPath}"
        Catch ex As Exception
            MessageBox.Show(Me, $"{UiStr("Str_MsgSaveError")}{Environment.NewLine}{ex.Message}", UiStr("Str_MsgSaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error)
        Finally
            SetBusy(False)
        End Try
    End Sub

    Private Sub MenuExit_Click(sender As Object, e As RoutedEventArgs) Handles MenuExit.Click
        Me.Close()
    End Sub

    Private Sub MenuUndo_Click(sender As Object, e As RoutedEventArgs) Handles MenuUndo.Click
        If _historyIndex < 0 Then
            AppendLog(UiStr("Str_LogUndoNone"))
            Return
        End If
        JumpToHistory(_historyIndex - 1)
    End Sub

    Private Sub MenuRedo_Click(sender As Object, e As RoutedEventArgs) Handles MenuRedo.Click
        If _historyIndex >= _history.Count - 1 Then
            AppendLog(UiStr("Str_LogRedoNone"))
            Return
        End If
        JumpToHistory(_historyIndex + 1)
    End Sub

    ''' <summary>Ctrl+Z/Ctrl+Y jako realne skróty (InputGestureText w XAML to tylko etykieta, nie wiąże klawisza). Pomijane, gdy fokus jest w polu tekstowym, żeby nie psuć wbudowanego undo/redo edycji tekstu.</summary>
    Private Sub Window_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        If TypeOf Keyboard.FocusedElement Is TextBox Then Return
        If Keyboard.Modifiers <> ModifierKeys.Control Then Return

        If e.Key = Key.Z Then
            MenuUndo_Click(sender, Nothing)
            e.Handled = True
        ElseIf e.Key = Key.Y Then
            MenuRedo_Click(sender, Nothing)
            e.Handled = True
        End If
    End Sub

    Private Sub MenuZoomExtents_Click(sender As Object, e As RoutedEventArgs) Handles MenuZoomExtents.Click
        Viewport.ZoomExtents()
    End Sub

    Private Sub MenuToggleWireframe_Click(sender As Object, e As RoutedEventArgs) Handles MenuToggleWireframe.Click
        _showWireframe = MenuToggleWireframe.IsChecked
        AppSettings.ShowWireframe = _showWireframe
        AppSettings.Save()
        RefreshViewportAndStats()
    End Sub

    Private Sub MenuPreferences_Click(sender As Object, e As RoutedEventArgs) Handles MenuPreferences.Click
        Dim prefs As New PreferencesWindow With {.Owner = Me}
        prefs.ShowDialog()
        ' po zamknięciu odśwież to, co nie jest samo-aktualizującym się {DynamicResource} (ComboBox, statystyki), gdyby język się zmienił
        RefreshComboBoxItemTexts()
        RefreshViewportAndStats()
    End Sub

    Private Sub MenuAbout_Click(sender As Object, e As RoutedEventArgs) Handles MenuAbout.Click
        MessageBox.Show(Me, UiStr("Str_AboutText"), UiStr("Str_AboutTitle"), MessageBoxButton.OK, MessageBoxImage.Information)
    End Sub

#End Region

#Region "Naprawa siatki"

    Private Async Sub BtnFillHoles_Click(sender As Object, e As RoutedEventArgs) Handles BtnFillHoles.Click
        Await RunOperation(Function(m) HoleFiller.FillHoles(m))
    End Sub

    Private Async Sub BtnCleanMesh_Click(sender As Object, e As RoutedEventArgs) Handles BtnCleanMesh.Click
        Await RunOperation(Function(m) MeshCleaner.CleanAll(m))
    End Sub

    Private Async Sub BtnRecalcNormals_Click(sender As Object, e As RoutedEventArgs) Handles BtnRecalcNormals.Click
        Await RunOperation(Function(m) NormalCalculator.Recalculate(m))
    End Sub

    Private Async Sub BtnSmooth_Click(sender As Object, e As RoutedEventArgs) Handles BtnSmooth.Click
        Dim iterations = ParseIntOrDefault(TxtSmoothIterations.Text, 3)
        Dim factor = ParseSingleOrDefault(TxtSmoothFactor.Text, 0.5F)
        AppSettings.LastSmoothIterations = iterations
        AppSettings.LastSmoothStrength = factor
        AppSettings.Save()
        Await RunOperation(Function(m) Smoother.Smooth(m, iterations, factor))
    End Sub

#End Region

#Region "Teselacja"

    Private Async Sub BtnSubdivide_Click(sender As Object, e As RoutedEventArgs) Handles BtnSubdivide.Click
        Dim iterations = ParseIntOrDefault(TxtSubdivideIterations.Text, 1)
        If iterations > 3 Then
            Dim factor4 = Math.Pow(4, iterations)
            Dim confirm = MessageBox.Show(Me,
                $"{iterations} {UiStr("Str_MsgSubdivideConfirm").Replace("{0:N0}", factor4.ToString("N0"))}",
                UiStr("Str_MsgSubdivideConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning)
            If confirm <> MessageBoxResult.Yes Then Return
        End If
        AppSettings.LastSubdivideIterations = iterations
        AppSettings.Save()
        Await RunOperation(Function(m) Tessellator.Subdivide(m, iterations))
    End Sub

    Private Async Sub BtnDecimate_Click(sender As Object, e As RoutedEventArgs) Handles BtnDecimate.Click
        Dim percent = ParseSingleOrDefault(TxtDecimatePercent.Text, 50.0F)
        AppSettings.LastDecimatePercent = percent
        AppSettings.Save()
        Await RunOperation(Function(m) Tessellator.DecimateToFraction(m, percent / 100.0F))
    End Sub

    Private Async Sub BtnTriangulatePointCloud_Click(sender As Object, e As RoutedEventArgs) Handles BtnTriangulatePointCloud.Click
        Await RunOperation(Function(m) PointCloudTriangulator.Triangulate(m))
    End Sub

#End Region

#Region "Tekstura"

    Private Sub BtnLoadTexturePhoto_Click(sender As Object, e As RoutedEventArgs) Handles BtnLoadTexturePhoto.Click
        Dim dlg As New OpenFileDialog With {
            .Filter = UiStr("Str_ImageFilter"),
            .Title = UiStr("Str_BtnLoadTexturePhoto")
        }
        If dlg.ShowDialog() = True Then
            _pendingTexturePath = dlg.FileName
            TxtSelectedPhoto.Text = Path.GetFileName(dlg.FileName)
        End If
    End Sub

    Private Async Sub BtnApplyTexture_Click(sender As Object, e As RoutedEventArgs) Handles BtnApplyTexture.Click
        If String.IsNullOrEmpty(_pendingTexturePath) Then
            MessageBox.Show(Me, UiStr("Str_MsgNoPhoto"), UiStr("Str_MsgNoPhotoTitle"), MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Dim projection = CType(Math.Max(CmbProjection.SelectedIndex, 0), ProjectionType)
        Dim texturePath = _pendingTexturePath
        Await RunOperation(Function(m) Texturizer.ApplyPhotoTexture(m, texturePath, projection))
    End Sub

#End Region

#Region "Transformacje"

    Private Async Sub BtnCenter_Click(sender As Object, e As RoutedEventArgs) Handles BtnCenter.Click
        Await RunOperation(Function(m) MeshTransformer.Center(m))
    End Sub

    Private Async Sub BtnNormalizeSize_Click(sender As Object, e As RoutedEventArgs) Handles BtnNormalizeSize.Click
        Await RunOperation(Function(m) MeshTransformer.NormalizeToSize(m, 1.0F))
    End Sub

    Private Async Sub BtnFlipNormals_Click(sender As Object, e As RoutedEventArgs) Handles BtnFlipNormals.Click
        Await RunOperation(Function(m) MeshTransformer.FlipNormals(m))
    End Sub

    ''' <summary>Wykrywa płaskie powierzchnie modelu, pokazuje listę do wyboru, i prostuje model do wybranej.</summary>
    Private Async Sub BtnAutoOrient_Click(sender As Object, e As RoutedEventArgs) Handles BtnAutoOrient.Click
        If Not EnsureMeshLoaded() Then Return

        SetBusy(True)
        Dim candidates As List(Of FlatCandidate) = Nothing
        Try
            Dim mesh = _currentMesh
            candidates = Await Task.Run(Function() AutoOrient.DetectFlatSurfaces(mesh))
        Catch ex As Exception
            AppendLog($"[{UiStr("Str_MsgOpenErrorTitle")}] {ex.Message}")
        Finally
            SetBusy(False)
        End Try

        If candidates Is Nothing OrElse candidates.Count = 0 Then
            MessageBox.Show(Me, UiStr("Str_PickerNoSurfaces"), UiStr("Str_PickerTitle"), MessageBoxButton.OK, MessageBoxImage.Information)
            Return
        End If

        Dim picker As New SurfacePickerWindow(_currentMesh, candidates) With {.Owner = Me}
        If picker.ShowDialog() = True AndAlso picker.SelectedCandidate IsNot Nothing Then
            Dim chosen = picker.SelectedCandidate
            Await RunOperation(Function(m) AutoOrient.OrientToSurface(m, chosen))
        End If
    End Sub

    ''' <summary>Wczytuje drugi plik i dokleja go do bieżącej siatki (MeshTransformer.Append) - proste złączenie bez automatycznego wyrównania, patrz komentarz w Append.</summary>
    Private Async Sub BtnMergeModel_Click(sender As Object, e As RoutedEventArgs) Handles BtnMergeModel.Click
        If Not EnsureMeshLoaded() Then Return
        Dim dlg As New OpenFileDialog With {.Filter = MeshIO.ImportFilter, .Title = UiStr("Str_BtnMergeModel")}
        If dlg.ShowDialog() <> True Then Return

        Dim otherPath = dlg.FileName
        Await RunOperation(Function(m)
                                Dim other = MeshIO.Load(otherPath)
                                Return MeshTransformer.Append(m, other)
                            End Function)
    End Sub

#End Region

#Region "Pasek narzędzi (obrót ręczny, usuwanie lassem, wysunięcie)"

    ''' <summary>Przełącza aktywne narzędzie (kliknięcie aktywnego przycisku ponownie wyłącza narzędzie) i pokazuje odpowiedni panel kontekstowy.</summary>
    Private Sub SetActiveTool(mode As ToolMode)
        _activeTool = If(_activeTool = mode, ToolMode.None, mode)

        RotateOptionsPanel.Visibility = If(_activeTool = ToolMode.Rotate, Visibility.Visible, Visibility.Collapsed)
        LassoOptionsPanel.Visibility = If(_activeTool = ToolMode.Lasso, Visibility.Visible, Visibility.Collapsed)
        ExtrudeOptionsPanel.Visibility = If(_activeTool = ToolMode.Extrude, Visibility.Visible, Visibility.Collapsed)

        ' nakładka przechwytuje mysz tylko dla narzędzi, które rysują/klikają na Viewporcie - Obrót działa wyłącznie przyciskami,
        ' więc kamera (orbit/pan/zoom) zostaje w pełni aktywna także wtedy, gdy narzędzie Obrót jest "włączone"
        Dim needsOverlay = (_activeTool = ToolMode.Lasso OrElse _activeTool = ToolMode.Extrude)
        ToolOverlayCanvas.IsHitTestVisible = needsOverlay
        ToolOverlayCanvas.Cursor = If(needsOverlay, Cursors.Cross, Cursors.Arrow)

        ClearLassoSelection()
        UpdateToolButtonHighlight()
    End Sub

    Private Sub UpdateToolButtonHighlight()
        Dim activeBrush = TryCast(Me.FindResource("AccentPaleBrush"), Brush)
        Dim normalBrush = TryCast(Me.FindResource("CardBackgroundBrush"), Brush)
        ToolBtnRotate.Background = If(_activeTool = ToolMode.Rotate, activeBrush, normalBrush)
        ToolBtnLasso.Background = If(_activeTool = ToolMode.Lasso, activeBrush, normalBrush)
        ToolBtnExtrude.Background = If(_activeTool = ToolMode.Extrude, activeBrush, normalBrush)
    End Sub

    Private Sub ToolBtnRotate_Click(sender As Object, e As RoutedEventArgs) Handles ToolBtnRotate.Click
        SetActiveTool(ToolMode.Rotate)
    End Sub

    Private Sub ToolBtnLasso_Click(sender As Object, e As RoutedEventArgs) Handles ToolBtnLasso.Click
        SetActiveTool(ToolMode.Lasso)
    End Sub

    Private Sub ToolBtnExtrude_Click(sender As Object, e As RoutedEventArgs) Handles ToolBtnExtrude.Click
        SetActiveTool(ToolMode.Extrude)
    End Sub

    ' ----- Obrót ręczny: kroki 90°, zastosowane od razu (proste, przewidywalne, bez konfliktu z kamerą) -----

    Private Async Sub BtnRotateXMinus90_Click(sender As Object, e As RoutedEventArgs) Handles BtnRotateXMinus90.Click
        Await RunOperation(Function(m) MeshTransformer.Rotate(m, Vector3.UnitX, -90.0F))
    End Sub
    Private Async Sub BtnRotateXPlus90_Click(sender As Object, e As RoutedEventArgs) Handles BtnRotateXPlus90.Click
        Await RunOperation(Function(m) MeshTransformer.Rotate(m, Vector3.UnitX, 90.0F))
    End Sub
    Private Async Sub BtnRotateYMinus90_Click(sender As Object, e As RoutedEventArgs) Handles BtnRotateYMinus90.Click
        Await RunOperation(Function(m) MeshTransformer.Rotate(m, Vector3.UnitY, -90.0F))
    End Sub
    Private Async Sub BtnRotateYPlus90_Click(sender As Object, e As RoutedEventArgs) Handles BtnRotateYPlus90.Click
        Await RunOperation(Function(m) MeshTransformer.Rotate(m, Vector3.UnitY, 90.0F))
    End Sub
    Private Async Sub BtnRotateZMinus90_Click(sender As Object, e As RoutedEventArgs) Handles BtnRotateZMinus90.Click
        Await RunOperation(Function(m) MeshTransformer.Rotate(m, Vector3.UnitZ, -90.0F))
    End Sub
    Private Async Sub BtnRotateZPlus90_Click(sender As Object, e As RoutedEventArgs) Handles BtnRotateZPlus90.Click
        Await RunOperation(Function(m) MeshTransformer.Rotate(m, Vector3.UnitZ, 90.0F))
    End Sub

    ' ----- Nakładka myszy: rysowanie lassa i kliknięcie do wysunięcia -----

    Private Async Sub ToolOverlayCanvas_MouseLeftButtonDown(sender As Object, e As MouseButtonEventArgs)
        Select Case _activeTool
            Case ToolMode.Lasso
                _isDrawingLasso = True
                _lassoPoints.Clear()
                Dim p = e.GetPosition(ToolOverlayCanvas)
                _lassoPoints.Add(p)
                LassoPolyline.Points.Clear()
                LassoPolyline.Points.Add(p)
                ToolOverlayCanvas.CaptureMouse()
            Case ToolMode.Extrude
                Await TryExtrudeAtPoint(e.GetPosition(ToolOverlayCanvas))
        End Select
    End Sub

    Private Sub ToolOverlayCanvas_MouseMove(sender As Object, e As MouseEventArgs)
        If _activeTool <> ToolMode.Lasso OrElse Not _isDrawingLasso Then Return
        Dim p = e.GetPosition(ToolOverlayCanvas)
        _lassoPoints.Add(p)
        LassoPolyline.Points.Add(p)
    End Sub

    Private Sub ToolOverlayCanvas_MouseLeftButtonUp(sender As Object, e As MouseButtonEventArgs)
        If _activeTool <> ToolMode.Lasso OrElse Not _isDrawingLasso Then Return
        _isDrawingLasso = False
        ToolOverlayCanvas.ReleaseMouseCapture()
        If _lassoPoints.Count >= 3 Then ComputeLassoSelection()
        LassoPolyline.Points.Clear()
        _lassoPoints.Clear()
    End Sub

    ''' <summary>Nakładka przechwytuje mysz (żeby przeciąganie nie obracało kamery), ale scroll-zoom ma nadal działać - przekaż go ręcznie do Viewportu.</summary>
    Private Sub ToolOverlayCanvas_MouseWheel(sender As Object, e As MouseWheelEventArgs)
        Dim forwarded As New MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) With {.RoutedEvent = UIElement.MouseWheelEvent}
        Viewport.RaiseEvent(forwarded)
    End Sub

    Private Async Function TryExtrudeAtPoint(p As Point) As Task
        If _currentMesh Is Nothing Then Return
        Dim hitList = Viewport3DHelper.FindHits(Viewport.Viewport, p, Nothing)
        If hitList Is Nothing OrElse hitList.Count = 0 Then Return
        Dim rh = hitList(0).RayHit
        If rh Is Nothing Then Return

        Dim triIdx = FindTriangleIndex(_currentMesh, rh.VertexIndex1, rh.VertexIndex2, rh.VertexIndex3)
        If triIdx < 0 Then Return

        Dim distance = ParseSingleOrDefault(TxtExtrudeDistance.Text, 1.0F)
        Await RunOperation(Function(m) MeshTransformer.ExtrudeTriangle(m, triIdx, distance))
    End Function

    Private Function FindTriangleIndex(mesh As Mesh, v1 As Integer, v2 As Integer, v3 As Integer) As Integer
        For i = 0 To mesh.Triangles.Count - 1
            Dim t = mesh.Triangles(i)
            If t.Contains(v1) AndAlso t.Contains(v2) AndAlso t.Contains(v3) Then Return i
        Next
        Return -1
    End Function

    ''' <summary>Gęste próbkowanie punktowe wnętrza wielokąta lassa (co kilka pikseli, hit-test przez Viewport3DHelper.FindHits) -
    ''' unika ręcznego liczenia macierzy projekcji kamery, kosztem odrobiny dokładności na samych krawędziach zaznaczenia.</summary>
    Private Sub ComputeLassoSelection()
        If _currentMesh Is Nothing Then Return
        SetBusy(True)
        Try
            Dim minX = _lassoPoints.Min(Function(pt) pt.X)
            Dim maxX = _lassoPoints.Max(Function(pt) pt.X)
            Dim minY = _lassoPoints.Min(Function(pt) pt.Y)
            Dim maxY = _lassoPoints.Max(Function(pt) pt.Y)

            Dim triLookup = BuildTriangleLookup(_currentMesh)
            Dim hits As New HashSet(Of Integer)
            Const sampleStep As Double = 5.0

            Dim y = minY
            While y <= maxY
                Dim x = minX
                While x <= maxX
                    Dim pt As New Point(x, y)
                    If IsPointInPolygon(pt, _lassoPoints) Then
                        Dim hitList = Viewport3DHelper.FindHits(Viewport.Viewport, pt, Nothing)
                        If hitList IsNot Nothing AndAlso hitList.Count > 0 Then
                            Dim rh = hitList(0).RayHit
                            If rh IsNot Nothing Then
                                Dim key = SortedTriple(rh.VertexIndex1, rh.VertexIndex2, rh.VertexIndex3)
                                Dim triIdx As Integer
                                If triLookup.TryGetValue(key, triIdx) Then hits.Add(triIdx)
                            End If
                        End If
                    End If
                    x += sampleStep
                End While
                y += sampleStep
            End While

            _lassoSelectedTriangles.Clear()
            For Each h In hits
                _lassoSelectedTriangles.Add(h)
            Next
            HighlightSelectedTriangles()
            BtnLassoDeleteSelected.IsEnabled = _lassoSelectedTriangles.Count > 0
            BtnLassoClearSelection.IsEnabled = _lassoSelectedTriangles.Count > 0

            If _lassoSelectedTriangles.Count > 0 Then
                AppendLog($"{UiStr("Str_LogLassoSelectedPrefix")} {_lassoSelectedTriangles.Count:N0} {UiStr("Str_LogTriangles")}.")
            Else
                AppendLog(UiStr("Str_LogLassoNone"))
            End If
        Finally
            SetBusy(False)
        End Try
    End Sub

    Private Function BuildTriangleLookup(mesh As Mesh) As Dictionary(Of (Integer, Integer, Integer), Integer)
        Dim dict As New Dictionary(Of (Integer, Integer, Integer), Integer)(mesh.Triangles.Count)
        For i = 0 To mesh.Triangles.Count - 1
            Dim t = mesh.Triangles(i)
            Dim key = SortedTriple(t.A, t.B, t.C)
            If Not dict.ContainsKey(key) Then dict(key) = i
        Next
        Return dict
    End Function

    Private Function SortedTriple(a As Integer, b As Integer, c As Integer) As (Integer, Integer, Integer)
        Dim arr = {a, b, c}
        Array.Sort(arr)
        Return (arr(0), arr(1), arr(2))
    End Function

    ''' <summary>Standardowy test "punkt w wielokącie" (algorytm rzucania promienia / parzysto-nieparzysty, PNPOLY).</summary>
    Private Function IsPointInPolygon(p As Point, polygon As List(Of Point)) As Boolean
        Dim inside = False
        Dim j = polygon.Count - 1
        For i = 0 To polygon.Count - 1
            If ((polygon(i).Y > p.Y) <> (polygon(j).Y > p.Y)) AndAlso
               (p.X < (polygon(j).X - polygon(i).X) * (p.Y - polygon(i).Y) / (polygon(j).Y - polygon(i).Y) + polygon(i).X) Then
                inside = Not inside
            End If
            j = i
        Next
        Return inside
    End Function

    ''' <summary>Podświetla zaznaczone trójkąty osobną, na czerwono zabarwioną geometrią nad modelem - pełny rebuild przy każdej zmianie (ten sam sprawdzony wzorzec co SurfacePickerWindow).</summary>
    Private Sub HighlightSelectedTriangles()
        SelectionVisualHost.Content = Nothing
        If _currentMesh Is Nothing OrElse _lassoSelectedTriangles.Count = 0 Then Return

        Dim geometry As New MeshGeometry3D()
        Dim idx = 0
        For Each triIdx In _lassoSelectedTriangles
            Dim t = _currentMesh.Triangles(triIdx)
            Dim a = _currentMesh.Vertices(t.A)
            Dim b = _currentMesh.Vertices(t.B)
            Dim c = _currentMesh.Vertices(t.C)
            geometry.Positions.Add(New Point3D(a.X, a.Y, a.Z))
            geometry.Positions.Add(New Point3D(b.X, b.Y, b.Z))
            geometry.Positions.Add(New Point3D(c.X, c.Y, c.Z))
            geometry.TriangleIndices.Add(idx) : geometry.TriangleIndices.Add(idx + 1) : geometry.TriangleIndices.Add(idx + 2)
            idx += 3
        Next

        Dim material As New DiffuseMaterial(New SolidColorBrush(Color.FromArgb(220, 255, 60, 60)))
        Dim model As New GeometryModel3D(geometry, material) With {.BackMaterial = material}
        SelectionVisualHost.Content = model
    End Sub

    Private Sub ClearLassoSelection()
        _lassoSelectedTriangles.Clear()
        If SelectionVisualHost IsNot Nothing Then SelectionVisualHost.Content = Nothing
        BtnLassoDeleteSelected.IsEnabled = False
        BtnLassoClearSelection.IsEnabled = False
    End Sub

    Private Async Sub BtnLassoDeleteSelected_Click(sender As Object, e As RoutedEventArgs) Handles BtnLassoDeleteSelected.Click
        If _lassoSelectedTriangles.Count = 0 Then Return
        Dim indices = _lassoSelectedTriangles.ToList()
        Await RunOperation(Function(m) MeshTransformer.DeleteTriangles(m, indices))
        ClearLassoSelection()
    End Sub

    Private Sub BtnLassoClearSelection_Click(sender As Object, e As RoutedEventArgs) Handles BtnLassoClearSelection.Click
        ClearLassoSelection()
    End Sub

#End Region

#Region "Wspólna infrastruktura operacji (undo, wątek w tle, log)"

    ''' <summary>
    ''' Uruchamia operację Core w tle (żeby nie blokować UI), a po sukcesie: opcjonalnie przelicza
    ''' normalne (jeśli włączone w Preferencjach), dokłada krok do paska historii i odświeża podgląd.
    ''' Migawka "sprzed" operacji nie jest tu potrzebna osobno - to po prostu poprzedni krok historii
    ''' (albo _historyBase), już zapisany.
    ''' </summary>
    Private Async Function RunOperation(op As Func(Of Mesh, OperationResult)) As Task
        If Not EnsureMeshLoaded() Then Return

        SetBusy(True)
        Try
            Dim mesh = _currentMesh
            Dim result = Await Task.Run(Function()
                                             Dim r = op(mesh)
                                             If r.Success AndAlso AppSettings.AutoRecalcNormals Then NormalCalculator.Recalculate(mesh)
                                             Return r
                                         End Function)

            If result.Success Then
                PushHistoryStep(result.Message)
                AppendLog(FormatResult(result, isError:=False))
                RefreshViewportAndStats()
                Viewport.ZoomExtents() ' geometria (a czasem i orientacja) się zmieniła - dopasuj widok, inaczej podgląd zostaje z nieaktualnym kadrem sprzed operacji
            Else
                AppendLog(FormatResult(result, isError:=True))
            End If
        Catch ex As Exception
            AppendLog($"[Exception] {ex.Message}")
        Finally
            SetBusy(False)
        End Try
    End Function

    ''' <summary>Dokłada nowy krok na koniec historii (obcinając "przyszłość", jeśli byliśmy cofnięci), po czym przycina do skonfigurowanej głębokości, przesuwając _historyBase.</summary>
    Private Sub PushHistoryStep(label As String)
        If _historyIndex < _history.Count - 1 Then
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1)
        End If
        _history.Add(New HistoryStep With {.Label = label, .Snapshot = _currentMesh.Clone()})
        _historyIndex = _history.Count - 1

        Dim maxSteps = Math.Max(AppSettings.MaxHistorySteps, 1)
        While _history.Count > maxSteps
            _historyBase = _history(0).Snapshot
            _history.RemoveAt(0)
            _historyIndex -= 1
        End While

        RefreshHistoryBar()
    End Sub

    ''' <summary>Przeskakuje bezpośrednio do dowolnego punktu w historii (-1 = stan tuż po wczytaniu pliku, 0..N-1 = po danym kroku). Klonuje migawkę, żeby dalsza praca nie mutowała zapisanej historii.</summary>
    Private Sub JumpToHistory(index As Integer)
        If index = _historyIndex Then Return
        If index < -1 OrElse index >= _history.Count Then Return

        _currentMesh = If(index = -1, _historyBase, _history(index).Snapshot).Clone()
        _historyIndex = index
        RefreshViewportAndStats()
        Viewport.ZoomExtents()
        RefreshHistoryBar()
        AppendLog(UiStr("Str_LogHistoryJump"))
    End Sub

    ''' <summary>Odbudowuje pasek historii (Base + każdy krok jako klikalny "chip", bieżący podświetlony akcentem).</summary>
    Private Sub RefreshHistoryBar()
        HistoryBar.Children.Clear()
        If _currentMesh Is Nothing Then Return

        HistoryBar.Children.Add(BuildHistoryChip(UiStr("Str_HistoryBase"), -1))
        For i = 0 To _history.Count - 1
            HistoryBar.Children.Add(BuildHistoryChipConnector())
            HistoryBar.Children.Add(BuildHistoryChip(_history(i).Label, i))
        Next
    End Sub

    Private Function BuildHistoryChip(label As String, index As Integer) As Border
        Dim isCurrent = (index = _historyIndex)
        Dim chip As New Border With {
            .CornerRadius = New CornerRadius(14),
            .Padding = New Thickness(12, 6, 12, 6),
            .Margin = New Thickness(2, 0, 2, 0),
            .Cursor = Cursors.Hand,
            .Background = If(isCurrent, TryCast(Me.FindResource("AccentBrush"), Brush), TryCast(Me.FindResource("InputBackgroundBrush"), Brush)),
            .BorderBrush = TryCast(Me.FindResource("CardBorderBrush"), Brush),
            .BorderThickness = New Thickness(If(isCurrent, 0, 1))
        }
        Dim shortLabel = If(label.Length > 22, label.Substring(0, 20) & "…", label)
        chip.Child = New TextBlock With {
            .Text = shortLabel,
            .Foreground = If(isCurrent, Brushes.White, TryCast(Me.FindResource("TextPrimaryBrush"), Brush)),
            .FontSize = 11,
            .FontWeight = If(isCurrent, FontWeights.SemiBold, FontWeights.Normal)
        }
        ToolTipService.SetToolTip(chip, label)
        AddHandler chip.MouseLeftButtonUp, Sub() JumpToHistory(index)
        Return chip
    End Function

    Private Function BuildHistoryChipConnector() As TextBlock
        Return New TextBlock With {
            .Text = "→",
            .VerticalAlignment = VerticalAlignment.Center,
            .Foreground = TryCast(Me.FindResource("TextSecondaryBrush"), Brush),
            .Margin = New Thickness(2, 0, 2, 0)
        }
    End Function

    Private Function EnsureMeshLoaded() As Boolean
        If _currentMesh Is Nothing OrElse _currentMesh.VertexCount = 0 Then
            MessageBox.Show(Me, UiStr("Str_MsgLoadFirst"), UiStr("Str_MsgLoadFirstTitle"), MessageBoxButton.OK, MessageBoxImage.Information)
            Return False
        End If
        Return True
    End Function

    Private Sub SetBusy(busy As Boolean)
        Me.IsEnabled = Not busy
        Me.Cursor = If(busy, Cursors.Wait, Cursors.Arrow)
    End Sub

    Private Function FormatResult(r As OperationResult, isError As Boolean) As String
        Dim prefix = If(isError, "[Info] ", "[OK] ")
        If String.IsNullOrWhiteSpace(r.Details) Then Return prefix & r.Message
        Return prefix & r.Message & Environment.NewLine & "     " & r.Details.Replace(Environment.NewLine, Environment.NewLine & "     ")
    End Function

    Private Sub AppendLog(text As String)
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}")
        LogBox.ScrollToEnd()
    End Sub

    Private Function ParseIntOrDefault(text As String, defaultValue As Integer) As Integer
        Dim v As Integer
        If Integer.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then Return v
        If Integer.TryParse(text, v) Then Return v ' np. użytkownik wpisał w formacie zgodnym z polskim regionem
        Return defaultValue
    End Function

    Private Function ParseSingleOrDefault(text As String, defaultValue As Single) As Single
        Dim v As Single
        If Single.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return v
        If Single.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, v) Then Return v ' obsłuż przecinek dziesiętny (pl-PL)
        Return defaultValue
    End Function

#End Region

#Region "Podgląd 3D i statystyki"

    Private Sub RefreshViewportAndStats()
        MeshVisualHost.Content = Nothing
        WireframeVisualHost.Children.Clear()
        ClearLassoSelection() ' stare indeksy trójkątów zaznaczenia tracą ważność przy każdej zmianie siatki

        If _currentMesh Is Nothing OrElse _currentMesh.VertexCount = 0 Then
            StatsBox.Text = UiStr("Str_NoModelLoaded")
            StatusText.Text = UiStr("Str_StatusReady")
            Return
        End If

        Try
            MeshVisualHost.Content = MeshVisualBuilder.BuildModel(_currentMesh)
        Catch ex As Exception
            AppendLog($"[Warning] {ex.Message}")
        End Try

        If _showWireframe Then
            Try
                WireframeVisualHost.Children.Add(MeshVisualBuilder.BuildWireframe(_currentMesh))
            Catch ex As Exception
                ' nakładka z krawędziami jest tylko pomocnicza - błąd tutaj nie powinien przerywać pracy
            End Try
        End If

        Dim stats = MeshStatistics.Compute(_currentMesh)
        StatsBox.Text = stats.ToString(AppSettings.UnitSuffix())
        StatusText.Text = $"{_currentMesh.VertexCount:N0} {UiStr("Str_LogVertices")}, {_currentMesh.TriangleCount:N0} {UiStr("Str_LogTriangles")}" &
                           If(_currentMesh.HasTexture, $" • {UiStr("Str_LogTexture")}", "") &
                           If(_currentMesh.HasVertexColors, $" • {UiStr("Str_LogVertexColor")}", "")
    End Sub

#End Region

End Class
