Imports System.Globalization
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Threading.Tasks
Imports System.Windows.Input
Imports System.Windows.Interop
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
    Private ReadOnly _undoHistory As New List(Of Mesh)
    Private Const MaxUndoSteps = 15

    Private _pendingTexturePath As String = ""
    Private _showWireframe As Boolean = False

    Public Sub New()
        InitializeComponent()
        RefreshComboBoxItemTexts()
        RefreshViewportAndStats()
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
            _undoHistory.Clear()
            _pendingTexturePath = ""
            TxtSelectedPhoto.Text = UiStr("Str_NoPhotoSelected")
            AppendLog($"{UiStr("Str_LogLoaded")} '{Path.GetFileName(selectedPath)}': {_currentMesh.VertexCount:N0} {UiStr("Str_LogVertices")}, {_currentMesh.TriangleCount:N0} {UiStr("Str_LogTriangles")}.")
            RefreshViewportAndStats()
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
        DoUndo()
    End Sub

    Private Sub MenuZoomExtents_Click(sender As Object, e As RoutedEventArgs) Handles MenuZoomExtents.Click
        Viewport.ZoomExtents()
    End Sub

    Private Sub MenuToggleWireframe_Click(sender As Object, e As RoutedEventArgs) Handles MenuToggleWireframe.Click
        _showWireframe = MenuToggleWireframe.IsChecked
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
        Await RunOperation(Function(m) Tessellator.Subdivide(m, iterations))
    End Sub

    Private Async Sub BtnDecimate_Click(sender As Object, e As RoutedEventArgs) Handles BtnDecimate.Click
        Dim percent = ParseSingleOrDefault(TxtDecimatePercent.Text, 50.0F)
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

#End Region

#Region "Wspólna infrastruktura operacji (undo, wątek w tle, log)"

    ''' <summary>
    ''' Uruchamia operację Core w tle (żeby nie blokować UI), zdejmuje migawkę do Cofnij PRZED operacją,
    ''' i po sukcesie odświeża podgląd. Migawka jest odrzucana, jeśli operacja się nie powiodła (nic się
    ''' wtedy nie zmieniło, więc nie ma czego cofać).
    ''' </summary>
    Private Async Function RunOperation(op As Func(Of Mesh, OperationResult)) As Task
        If Not EnsureMeshLoaded() Then Return

        SetBusy(True)
        Dim snapshot = _currentMesh.Clone()
        Try
            Dim mesh = _currentMesh
            Dim result = Await Task.Run(Function() op(mesh))

            If result.Success Then
                PushUndoSnapshot(snapshot)
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

    Private Sub PushUndoSnapshot(snapshot As Mesh)
        _undoHistory.Add(snapshot)
        If _undoHistory.Count > MaxUndoSteps Then _undoHistory.RemoveAt(0)
    End Sub

    Private Sub DoUndo()
        If _undoHistory.Count = 0 Then
            AppendLog(UiStr("Str_LogUndoNone"))
            Return
        End If
        _currentMesh = _undoHistory(_undoHistory.Count - 1)
        _undoHistory.RemoveAt(_undoHistory.Count - 1)
        AppendLog(UiStr("Str_LogUndoDone"))
        RefreshViewportAndStats()
    End Sub

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
        StatsBox.Text = stats.ToString()
        StatusText.Text = $"{_currentMesh.VertexCount:N0} {UiStr("Str_LogVertices")}, {_currentMesh.TriangleCount:N0} {UiStr("Str_LogTriangles")}" &
                           If(_currentMesh.HasTexture, $" • {UiStr("Str_LogTexture")}", "") &
                           If(_currentMesh.HasVertexColors, $" • {UiStr("Str_LogVertexColor")}", "")
    End Sub

#End Region

End Class
