Namespace Geometry

    ''' <summary>Trójkąt siatki jako trójka indeksów do listy wierzchołków. Kolejność A-B-C ustala nawinięcie (i normalną).</summary>
    Public Structure Triangle
        Public A As Integer
        Public B As Integer
        Public C As Integer

        Public Sub New(a As Integer, b As Integer, c As Integer)
            Me.A = a
            Me.B = b
            Me.C = c
        End Sub

        Public Function Contains(v As Integer) As Boolean
            Return A = v OrElse B = v OrElse C = v
        End Function

        ''' <summary>Zwraca trójkąt z odwróconym nawinięciem (odwrócona normalna).</summary>
        Public Function Flipped() As Triangle
            Return New Triangle(A, C, B)
        End Function

        ''' <summary>Zamienia indeks "from" na "to" (używane przy scalaniu wierzchołków).</summary>
        Public Function WithReplacedIndex(fromIndex As Integer, toIndex As Integer) As Triangle
            Dim na = If(A = fromIndex, toIndex, A)
            Dim nb = If(B = fromIndex, toIndex, B)
            Dim nc = If(C = fromIndex, toIndex, C)
            Return New Triangle(na, nb, nc)
        End Function

        ''' <summary>Czy trójkąt jest zdegenerowany (dwa lub trzy takie same indeksy).</summary>
        Public ReadOnly Property IsDegenerate As Boolean
            Get
                Return A = B OrElse B = C OrElse A = C
            End Get
        End Property

        Public Iterator Function Indices() As IEnumerable(Of Integer)
            Yield A
            Yield B
            Yield C
        End Function

        ''' <summary>Trzy krawędzie trójkąta jako pary indeksów, w kolejności nawinięcia A-B, B-C, C-A.</summary>
        Public Iterator Function DirectedEdges() As IEnumerable(Of (from As Integer, [to] As Integer))
            Yield (A, B)
            Yield (B, C)
            Yield (C, A)
        End Function
    End Structure

End Namespace
