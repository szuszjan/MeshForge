Imports System.Numerics

Namespace Algorithms

    ''' <summary>Jedna ściana otoczki: trójka indeksów do WEJŚCIOWEJ listy punktów (nie do jakiejś przekompresowanej listy) + normalna skierowana na zewnątrz.</summary>
    Public Structure HullFace
        Public A As Integer
        Public B As Integer
        Public C As Integer
        Public Normal As Vector3

        Public Sub New(a As Integer, b As Integer, c As Integer, normal As Vector3)
            Me.A = a
            Me.B = b
            Me.C = c
            Me.Normal = normal
        End Sub
    End Structure

    ''' <summary>
    ''' Inkrementalna otoczka wypukła 3D - ten sam rodzaj algorytmu geometrycznego, na którym opiera się
    ''' funkcja "postaw na ściance" w Orca/PrusaSlicer. Używana przez AutoOrient: fizyczna, stabilna podstawa
    ''' modelu musi leżeć na otoczce wypukłej (powierzchnia wklęsła "od wewnątrz" siatki nigdy nie może być
    ''' stabilną podstawą, więc nie ma sensu jej proponować).
    ''' </summary>
    Public Module ConvexHull3D

        ''' <summary>
        ''' Liczy otoczkę wypukłą podanych punktów. Zwraca listę ścian (trójkątów), każda jako indeksy
        ''' do "points" z normalną skierowaną na zewnątrz. Zwraca pustą listę, jeśli punkty są zdegenerowane
        ''' (mniej niż 4, albo wszystkie współpłaszczyznowe/współliniowe/identyczne - brak bryły 3D).
        ''' </summary>
        Public Function Compute(points As IReadOnlyList(Of Vector3)) As List(Of HullFace)
            Dim result As New List(Of HullFace)
            Dim n = points.Count
            If n < 4 Then Return result

            Dim bbMin = points(0)
            Dim bbMax = points(0)
            For i = 1 To n - 1
                bbMin = Vector3.Min(bbMin, points(i))
                bbMax = Vector3.Max(bbMax, points(i))
            Next
            Dim diag = (bbMax - bbMin).Length()
            Dim epsilon = Math.Max(diag * 0.00001F, 0.0000001F)

            ' Wierzchołki startowego czworościanu - kolejne najdalsze punkty (od punktu, potem od prostej,
            ' potem od płaszczyzny). Solidny, standardowy sposób na znalezienie 4 punktów "rozpinających" 3D,
            ' odporny na zdegenerowane układy (np. wszystkie punkty o tym samym X).
            Dim i0 = 0
            For i = 1 To n - 1
                If points(i).X < points(i0).X Then i0 = i
            Next

            Dim i1 = FarthestFromPoint(points, points(i0), i0, -1, epsilon)
            If i1 = -1 Then Return result ' wszystkie punkty praktycznie identyczne

            Dim i2 = FarthestFromLine(points, points(i0), points(i1), i0, i1, epsilon)
            If i2 = -1 Then Return result ' wszystkie punkty współliniowe

            Dim i3 = FarthestFromPlane(points, points(i0), points(i1), points(i2), i0, i1, i2, epsilon)
            If i3 = -1 Then Return result ' wszystkie punkty współpłaszczyznowe - siatka jest płaska, brak bryły

            ' Punkt na pewno wewnątrz każdej otoczki, jaka powstanie (leży ściśle wewnątrz startowego
            ' czworościanu) - używany do ustalania spójnego, skierowanego na zewnątrz nawinięcia każdej ściany.
            Dim interiorPoint = (points(i0) + points(i1) + points(i2) + points(i3)) / 4.0F

            Dim faces As New List(Of (a As Integer, b As Integer, c As Integer, normal As Vector3))
            AddFace(faces, points, i0, i1, i2, interiorPoint, epsilon)
            AddFace(faces, points, i0, i3, i1, interiorPoint, epsilon)
            AddFace(faces, points, i0, i2, i3, interiorPoint, epsilon)
            AddFace(faces, points, i1, i3, i2, interiorPoint, epsilon)

            Dim used As New HashSet(Of Integer) From {i0, i1, i2, i3}

            For pi = 0 To n - 1
                If used.Contains(pi) Then Continue For
                Dim p = points(pi)

                ' Jeden przebieg po ścianach, W MIEJSCU (bez alokowania nowej listy "zachowanych" ścian
                ' za każdym razem - dla gęstych, "okrągłych" skanów, gdzie prawie każdy punkt jest
                ' wierzchołkiem otoczki, ścian szybko robi się tysiące, a tworzenie od zera nowej listy
                ' tego rozmiaru dla KAŻDEGO z tysięcy punktów zdominowało czas wykonania - zmierzone 24s
                ' zamiast ułamka sekundy). Zachowane ściany nadpisują tablicę faces od początku (writeIdx),
                ' widoczne (zwykle nieliczne) trafiają do osobnej, małej listy; na końcu jedno RemoveRange
                ' obcina "ogon" - O(usuniętych), a nie O(widoczne × wszystkie), jak przy repeated RemoveAt.
                Dim visibleFaces As New List(Of (a As Integer, b As Integer, c As Integer, normal As Vector3))
                Dim writeIdx = 0
                For fi = 0 To faces.Count - 1
                    Dim f = faces(fi)
                    If Vector3.Dot(f.normal, p - points(f.a)) > epsilon Then
                        visibleFaces.Add(f)
                    Else
                        faces(writeIdx) = f
                        writeIdx += 1
                    End If
                Next
                If visibleFaces.Count = 0 Then Continue For ' punkt jest wewnątrz bieżącej otoczki - pomiń
                faces.RemoveRange(writeIdx, faces.Count - writeIdx)

                ' Krawędzie horyzontu: skierowane krawędzie widocznych ścian, których odwrócona para NIE
                ' należy do żadnej innej widocznej ściany (czyli krawędzie na granicy widocznego "łatka").
                Dim visibleEdges As New HashSet(Of (Integer, Integer))
                For Each f In visibleFaces
                    visibleEdges.Add((f.a, f.b))
                    visibleEdges.Add((f.b, f.c))
                    visibleEdges.Add((f.c, f.a))
                Next
                Dim horizon As New List(Of (Integer, Integer))
                For Each e In visibleEdges
                    If Not visibleEdges.Contains((e.Item2, e.Item1)) Then horizon.Add(e)
                Next

                For Each e In horizon
                    AddFace(faces, points, e.Item1, e.Item2, pi, interiorPoint, epsilon)
                Next

                used.Add(pi)

                ' Zabezpieczenie: prawdziwa otoczka wypukła ma najwyżej 2n-4 ścian. Przy bardzo gęstym,
                ' prawie idealnie krzywoliniowym próbkowaniu (np. mocno zaokrąglony skan bez płaskich
                ' powierzchni) sąsiednie ściany otoczki stają się niemal współpłaszczyznowe, a błąd
                ' zaokrąglenia w tekście widoczności potrafi (rzadko) zostawić "osierocone" ściany zamiast
                ' je poprawnie usunąć - to się kaskadowo nakręca i bez tego zabezpieczenia potrafiło
                ' zawiesić program na wiele minut (setki tysięcy ścian zamiast tysięcy). Zamiast tego -
                ' przerwij i zwróć pustą otoczkę; wywołujący (AutoOrient) ma już zapasowe, prostsze
                ' grupowanie po normalnej na wypadek braku otoczki.
                If faces.Count > Math.Max(8 * n, 2000) Then Return New List(Of HullFace)
            Next

            For Each f In faces
                result.Add(New HullFace(f.a, f.b, f.c, f.normal))
            Next
            Return result
        End Function

        ''' <summary>
        ''' Dodaje ścianę (a,b,c), automatycznie poprawiając nawinięcie tak, żeby normalna wskazywała na
        ''' zewnątrz (z dala od interiorPoint). Pomija zdegenerowane (współliniowe punkty) trójkąty - próg
        ''' WZGLĘDNY do długości krawędzi TEGO trójkąta, nie do globalnego epsilon (opartego o skalę całego
        ''' modelu)! Przy gęstym próbkowaniu ściany otoczki są naturalnie coraz mniejsze - globalny epsilon
        ''' zaczynał wtedy fałszywie odrzucać prawidłowe, po prostu małe trójkąty, zostawiając "dziury" na
        ''' horyzoncie i psując resztę otoczki (zmierzone: kaskadowa eksplozja do setek tysięcy ścian).
        ''' </summary>
        Private Sub AddFace(faces As List(Of (a As Integer, b As Integer, c As Integer, normal As Vector3)),
                             points As IReadOnlyList(Of Vector3), a As Integer, b As Integer, c As Integer,
                             interiorPoint As Vector3, epsilon As Single)
            Dim pa = points(a) : Dim pb = points(b) : Dim pc = points(c)
            Dim edge1 = pb - pa
            Dim edge2 = pc - pa
            Dim cross = Vector3.Cross(edge1, edge2)
            Dim maxEdgeLenSq = Math.Max(edge1.LengthSquared(), edge2.LengthSquared())
            If maxEdgeLenSq <= 0.0F Then Return
            ' cross.LengthSquared() = |edge1|²|edge2|²sin²(kąt) - porównanie do maxEdgeLenSq² sprowadza się
            ' więc (w przybliżeniu) do sprawdzenia sin²(kąt) - miary "spłaszczenia" niezależnej od skali.
            If cross.LengthSquared() < maxEdgeLenSq * maxEdgeLenSq * 0.00000001F Then Return
            Dim normal = Vector3.Normalize(cross)

            If Vector3.Dot(normal, pa - interiorPoint) < 0 Then
                faces.Add((a, c, b, -normal))
            Else
                faces.Add((a, b, c, normal))
            End If
        End Sub

        Private Function FarthestFromPoint(points As IReadOnlyList(Of Vector3), from As Vector3, excludeA As Integer, excludeB As Integer, epsilon As Single) As Integer
            Dim best = -1
            Dim bestDistSq As Single = epsilon * epsilon
            For i = 0 To points.Count - 1
                If i = excludeA OrElse i = excludeB Then Continue For
                Dim d = Vector3.DistanceSquared(points(i), from)
                If d > bestDistSq Then
                    bestDistSq = d
                    best = i
                End If
            Next
            Return best
        End Function

        Private Function FarthestFromLine(points As IReadOnlyList(Of Vector3), a As Vector3, b As Vector3, excludeA As Integer, excludeB As Integer, epsilon As Single) As Integer
            Dim dir = b - a
            Dim lenSq = dir.LengthSquared()
            Dim best = -1
            Dim bestDistSq As Single = epsilon * epsilon
            For i = 0 To points.Count - 1
                If i = excludeA OrElse i = excludeB Then Continue For
                Dim toP = points(i) - a
                Dim t = If(lenSq > 0, Vector3.Dot(toP, dir) / lenSq, 0.0F)
                Dim closest = a + dir * t
                Dim d = Vector3.DistanceSquared(points(i), closest)
                If d > bestDistSq Then
                    bestDistSq = d
                    best = i
                End If
            Next
            Return best
        End Function

        Private Function FarthestFromPlane(points As IReadOnlyList(Of Vector3), a As Vector3, b As Vector3, c As Vector3, excludeA As Integer, excludeB As Integer, excludeC As Integer, epsilon As Single) As Integer
            Dim n = Vector3.Cross(b - a, c - a)
            If n.LengthSquared() < epsilon * epsilon Then Return -1
            n = Vector3.Normalize(n)
            Dim best = -1
            Dim bestDist As Single = epsilon
            For i = 0 To points.Count - 1
                If i = excludeA OrElse i = excludeB OrElse i = excludeC Then Continue For
                Dim d = Math.Abs(Vector3.Dot(n, points(i) - a))
                If d > bestDist Then
                    bestDist = d
                    best = i
                End If
            Next
            Return best
        End Function

    End Module

End Namespace
