Namespace Geometry

    ''' <summary>
    ''' Prosty, niezależny od WPF/GDI kolor RGBA (0-255 na kanał).
    ''' Używany do przechowywania kolorów wierzchołków (np. z chmury punktów skanera).
    ''' </summary>
    Public Structure RgbColor
        Public R As Byte
        Public G As Byte
        Public B As Byte
        Public A As Byte

        Public Sub New(r As Byte, g As Byte, b As Byte, Optional a As Byte = 255)
            Me.R = r
            Me.G = g
            Me.B = b
            Me.A = a
        End Sub

        Public Shared ReadOnly Property White As RgbColor
            Get
                Return New RgbColor(255, 255, 255, 255)
            End Get
        End Property

        Public Shared ReadOnly Property Gray As RgbColor
            Get
                Return New RgbColor(200, 200, 200, 255)
            End Get
        End Property

        ''' <summary>Liniowa interpolacja między dwoma kolorami (t w zakresie 0..1).</summary>
        Public Shared Function Lerp(a As RgbColor, b As RgbColor, t As Single) As RgbColor
            t = Math.Max(0F, Math.Min(1F, t))
            Return New RgbColor(
                CByte(a.R + (b.R - a.R) * t),
                CByte(a.G + (b.G - a.G) * t),
                CByte(a.B + (b.B - a.B) * t),
                CByte(a.A + (b.A - a.A) * t))
        End Function

        Public Shared Function Average(colors As IEnumerable(Of RgbColor)) As RgbColor
            Dim sumR As Double = 0, sumG As Double = 0, sumB As Double = 0, sumA As Double = 0
            Dim n As Integer = 0
            For Each c In colors
                sumR += c.R : sumG += c.G : sumB += c.B : sumA += c.A
                n += 1
            Next
            If n = 0 Then Return RgbColor.Gray
            Return New RgbColor(CByte(sumR / n), CByte(sumG / n), CByte(sumB / n), CByte(sumA / n))
        End Function
    End Structure

End Namespace
