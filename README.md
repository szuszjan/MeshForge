# MeshForge

Program (VB.NET, WPF, .NET 9) do obróbki skanów 3D: import/eksport typowych formatów, wypełnianie
otworów, teselacja (zagęszczanie i upraszczanie siatki), nakładanie kolorowej tekstury ze zdjęcia
oraz zestaw innych przydatnych funkcji porządkujących siatkę.

Rzeczywiście działający, przetestowany kod — nie prototyp/atrapa. Rdzeń algorytmiczny (16 testów
jednostkowych) jest oddzielony od interfejsu, więc łatwo go rozwijać albo użyć w innym projekcie.

## Wymagania

- Windows (WPF jest technologią tylko-Windows)
- [.NET 9 SDK](https://dotnet.microsoft.com/download) — do budowania i uruchamiania

Visual Studio **nie jest wymagany** — całość buduje się i uruchamia z linii poleceń — ale możesz
też otworzyć `MeshForge.sln` w Visual Studio 2022+, jeśli wolisz.

## Uruchomienie

```bash
dotnet run --project src/MeshForge.App
```

Budowanie całej solucji i uruchomienie testów rdzenia:

```bash
dotnet build MeshForge.sln
dotnet test src/MeshForge.Tests
```

Na start otwórz jeden z plików w folderze [przyklady/](przyklady/) (Plik → Otwórz skan...).

## Instalator

Gotowy `Setup.exe` (Windows Installer w stylu Inno Setup — skrót w Menu Start, opcjonalny na pulpicie,
odinstalowywanie z listy aplikacji) buduje się jednym poleceniem:

```powershell
installer\build.ps1
```

Wynik: `installer_output\MeshForge-Setup-<wersja>.exe`. Wymaga [Inno Setup 6](https://jrsoftware.org/isdl.php)
(`winget install JRSoftware.InnoSetup`) — skrypt sam publikuje aplikację jako samodzielny plik .exe
(nie wymaga zainstalowanego .NET na komputerze docelowym) i pakuje ją w instalator.

## Struktura projektu

```
MeshForge.sln
src/
  MeshForge.Core/     biblioteka klas - cały model siatki i algorytmy, ZERO zależności od WPF/UI
    Geometry/            Mesh, Triangle, topologia siatki (krawędzie/otwory), spawanie wierzchołków
    IO/                   import/eksport OBJ, STL (ASCII+binarny), PLY (ASCII), XYZ (chmura punktów)
    Algorithms/           HoleFiller, Tessellator (subdivide+decymacja QEM), PointCloudTriangulator,
                          Smoother, Texturizer, MeshCleaner, MeshTransformer, NormalCalculator
  MeshForge.App/       aplikacja WPF (VB.NET) - okno, podgląd 3D (HelixToolkit), obsługa zdarzeń
  MeshForge.Tests/     16 testów xUnit na algorytmach Core (w tym testy na plikach z przyklady/)
przyklady/                gotowe pliki do wypróbowania funkcji
```

Rdzeń (`MeshForge.Core`) jest celowo niezależny od WPF — cała logika geometrii działa też
w konsoli/testach, bez otwierania okna.

## Funkcje

### Import / eksport
- **Import:** OBJ (z materiałem/teksturą przez MTL), STL (ASCII i binarny, auto-wykrywanie), PLY
  (tylko wariant ASCII — patrz "Ograniczenia"), XYZ/TXT (surowa chmura punktów: `x y z` lub
  `x y z r g b`, spacja/tabulator/przecinek jako separator)
- **Eksport:** OBJ (z MTL + kopią tekstury, jeśli jest), STL (binarny), PLY (ASCII, z kolorem
  wierzchołków), XYZ

### Naprawa siatki
- **Wypełnij otwory** — wykrywa kontury brzegowe (krawędzie należące do jednego trójkąta) i
  łata je metodą "ear clipping" w płaszczyźnie dopasowanej metodą Newella. Bardzo duże kontury
  (domyślnie >2000 wierzchołków) są pomijane — to zwykle zewnętrzny brzeg niedomkniętego skanu,
  a nie dziura do załatania.
- **Wyczyść siatkę** — spawa wierzchołki leżące bliżej siebie niż epsilon (typowe po eksporcie
  STL, który nie indeksuje wierzchołków), usuwa zdegenerowane/zdublowane trójkąty i martwe
  wierzchołki.
- **Przelicz normalne** — normalne per-wierzchołek ważone polem powierzchni sąsiednich trójkątów.
- **Wygładź (Laplace)** — redukuje szum skanera; wierzchołki na brzegu otworów są celowo pomijane,
  żeby wygładzanie nie "kurczyło" brzegu do środka.

### Teselacja
- **Zagęść (subdivision)** — dzieli każdy trójkąt na 4 w środkach krawędzi (współdzielone
  punkty środkowe, więc siatka zostaje spójna, bez pęknięć).
- **Uprość (decymacja)** — klasyczny algorytm błędu kwadratowego Garland-Heckbert (QEM):
  ściąga najtańsze krawędzie do optymalnego punktu (rozwiązanie układu 3×3), aż do zadanego
  procentu obecnej liczby trójkątów.
- **Teseluj chmurę punktów** — dla surowej chmury punktów (bez trójkątów): dopasowanie
  płaszczyzny metodą PCA + triangulacja Delaunaya 2D (Bowyer-Watson). Działa bardzo dobrze dla
  pojedynczego ujęcia skanera (powierzchnia widoczna mniej więcej z jednej strony, jak mapa
  wysokości) — patrz "Ograniczenia" niżej.

### Tekstura kolorowa ze zdjęcia
Generuje współrzędne UV metodą rzutowania (planarne / cylindryczne / sferyczne — wybierz typ
dopasowany do kształtu modelu) i podpina wskazane zdjęcie jako teksturę. To mapowanie przez
rzut z jednego zdjęcia, nie pełna fotogrametria wieloujęciowa.

### Transformacje
Wyśrodkowanie, normalizacja rozmiaru do 1.0, odwrócenie normalnych (naprawia siatkę
"wywróconą na lewą stronę"), **auto-orientacja do najbardziej płaskiej powierzchni** — wykrywa
płaskie powierzchnie modelu (grupując trójkąty wg kierunku normalnej, ważąc polem), pokazuje listę
od największej i pozwala wybrać, która ma się stać poziomą podstawą (model zostaje obrócony i
"postawiony" na tej powierzchni). Przydatne po skanowaniu, gdy model ma przypadkową orientację.

### Preferencje
Menu **Preferencje...** — język interfejsu (polski/angielski, przełączany na żywo, bez restartu)
i tryb ciemny. Oba wybory są zapamiętywane między uruchomieniami
(`%AppData%\MeshForge\settings.txt`).

### Inne
Cofnij ostatnią operację (historia 15 kroków), statystyki siatki na żywo (liczba
wierzchołków/trójkątów, wymiary, pole powierzchni, objętość, czy siatka jest wodoszczelna),
podgląd wireframe, obrót/przesuwanie/zoom kamery w podglądzie 3D, ekran startowy i własna ikona
aplikacji.

## Ograniczenia wersji 1 (świadome uproszczenia, nie "będzie kiedyś")

- **Teselacja chmury punktów** to metoda "2,5D" (rzut na jedną najlepiej dopasowaną płaszczyznę).
  Nie rekonstruuje poprawnie zamkniętej bryły z chmury zeskanowanej z każdej strony (360°) — do
  tego potrzebny byłby algorytm typu Poisson surface reconstruction / ball-pivoting, znacznie
  bardziej złożony. W praktyce większość skanerów i tak eksportuje już gotową siatkę (OBJ/STL/PLY),
  więc to ograniczenie dotyczy tylko surowych chmur punktów ze skanowania 360°.
- **PLY** — obsługiwany jest tylko wariant ASCII (binarny rzuca czytelny komunikat z podpowiedzią,
  jak przekonwertować w MeshLab).
- **Tekstura** to mapowanie przez rzut z jednego zdjęcia, nie automatyczne łączenie wielu zdjęć
  z różnych kątów (jak RealityCapture/Metashape).
- **Łączenie wielu skanów** — jest proste doklejenie siatek (`MeshTransformer.Append`), ale bez
  automatycznego wyrównania (ICP/rejestracja). Jeśli dwa skany nie są już w tym samym układzie
  współrzędnych, trzeba je wyrównać ręcznie przed połączeniem.

Żadne z powyższych nie jest "udawane" — po prostu jest to zakres rozsądny do realnego zbudowania
i przetestowania w jednej sesji. Najbardziej naturalne rozszerzenia na później: Poisson
reconstruction dla pełnych chmur 360°, ICP do automatycznego wyrównywania skanów, import PLY
binarnego, multi-photo texture baking.

## Dlaczego takie wybory techniczne

- **WPF + HelixToolkit.Wpf** zamiast czystego WinForms+OpenGL: viewport 3D z obrotem/zoomem/panem
  "za darmo", bez pisania własnej kamery, i bez zależności od natywnych bibliotek graficznych.
- **System.Numerics.Vector3/Vector2** w Core zamiast własnych typów wektorowych — to wbudowana
  w .NET, szybka biblioteka matematyczna, więc nie trzeba jej pisać od zera.
- **Model "unified index"** (jeden zestaw normalna/UV/kolor na wierzchołek) zamiast osobnego
  indeksowania v/vt/vn jak w surowym OBJ — prostszy kod i pasuje bezpośrednio do tego, czego
  oczekuje `MeshGeometry3D` w WPF.
