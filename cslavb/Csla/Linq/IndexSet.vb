Imports System.Reflection
Imports System.Linq.Expressions
Imports Ex = System.Linq.Expressions.Expression

Namespace Linq
  Friend Class IndexSet(Of T)
    Implements IIndexSet(Of T)
    Private _internalIndexSet As Dictionary(Of String, IIndex(Of T)) = New Dictionary(Of String, IIndex(Of T))()

    Public Sub New()
      Dim allProps() As PropertyInfo = GetType(T).GetProperties()
      For Each [property] As PropertyInfo In allProps
        Dim attributes() As Object = [property].GetCustomAttributes(True)
        For Each attribute As Object In attributes
          If TypeOf attribute Is IndexableAttribute Then
            _internalIndexSet.Add([property].Name, New Index(Of T)([property].Name, TryCast(attribute, IndexableAttribute)))
          End If
        Next attribute
      Next [property]
    End Sub

#Region "IIndexSet<T> Members"

    Private Sub InsertItem(ByVal item As T) Implements IIndexSet(Of T).InsertItem
      For Each index As IIndex(Of T) In _internalIndexSet.Values
        If index.Loaded Then
          index.Add(item)
        End If
      Next index
    End Sub

    Private Sub InsertItem(ByVal item As T, ByVal [property] As String) Implements IIndexSet(Of T).InsertItem
      If _internalIndexSet.ContainsKey([property]) Then
        If _internalIndexSet([property]).Loaded Then
          _internalIndexSet([property]).Add(item)
        End If
      End If
    End Sub

    Private Sub RemoveItem(ByVal item As T) Implements IIndexSet(Of T).RemoveItem
      For Each index As IIndex(Of T) In _internalIndexSet.Values
        index.Remove(item)
      Next index
    End Sub

    Private Sub RemoveItem(ByVal item As T, ByVal [property] As String) Implements IIndexSet(Of T).RemoveItem
      If _internalIndexSet.ContainsKey([property]) Then
        _internalIndexSet([property]).Remove(item)
      End If
    End Sub

    Private Sub ReIndexItem(ByVal item As T, ByVal [property] As String) Implements IIndexSet(Of T).ReIndexItem
      _internalIndexSet([property]).Remove(item)
      _internalIndexSet([property]).Add(item)
    End Sub

    Private Sub ReIndexItem(ByVal item As T) Implements IIndexSet(Of T).ReIndexItem
      For Each index As IIndex(Of T) In _internalIndexSet.Values
        index.Remove(item)
        index.Add(item)
      Next index
    End Sub

    Private Sub ClearIndexes() Implements IIndexSet(Of T).ClearIndexes
      For Each index As IIndex(Of T) In _internalIndexSet.Values
        index.Clear()
      Next index
    End Sub

    Private Sub ClearIndex(ByVal [property] As String) Implements IIndexSet(Of T).ClearIndex
      If _internalIndexSet.ContainsKey([property]) Then
        _internalIndexSet([property]).Clear()
      End If
    End Sub

    Private Function HasIndexFor(ByVal [property] As String) As Boolean Implements IIndexSet(Of T).HasIndexFor
      Return _internalIndexSet.ContainsKey([property])
    End Function

    Private Function HasIndexFor(ByVal expr As Expression(Of Func(Of T, Boolean))) As String Implements IIndexSet(Of T).HasIndexFor
      If expr.Body.NodeType = ExpressionType.Equal AndAlso TypeOf expr.Body Is BinaryExpression Then
        Dim binExp As BinaryExpression = CType(expr.Body, BinaryExpression)
        If HasIndexablePropertyOnLeft(binExp.Left) Then
          Return (CType(binExp.Left, MemberExpression)).Member.Name
        Else
          Return Nothing
        End If
      Else
        Return Nothing
      End If
    End Function

    Public ReadOnly Property IIndexSet_Item(ByVal [property] As String) As IIndex(Of T) Implements IIndexSet(Of T).Item
      Get
        Return _internalIndexSet([property])
      End Get
    End Property

    Private Function HasIndexablePropertyOnLeft(ByVal leftSide As Expression) As Boolean
      If leftSide.NodeType = ExpressionType.MemberAccess Then
        Return (TryCast(Me, IIndexSet(Of T))).HasIndexFor((CType(leftSide, MemberExpression)).Member.Name)
      Else
        Return False
      End If
    End Function

    Private Function GetHashRight(ByVal rightSide As Expression) As Nullable(Of Integer)
      'rightside is where we get our hash...
      Select Case rightSide.NodeType
        'shortcut constants, dont eval, will be faster
        Case ExpressionType.Constant
          Dim constExp As ConstantExpression = CType(rightSide, ConstantExpression)
          Return (constExp.Value.GetHashCode())

          'if not constant (which is provably terminal in a tree), convert back to Lambda and eval to get the hash.
        Case Else
          'Lambdas can be created from expressions... yay
          Dim evalRight As LambdaExpression = Ex.Lambda(rightSide, Nothing)
          'Compile that mutherf-ker, invoke it, and get the resulting hash
          Return (evalRight.Compile().DynamicInvoke(Nothing).GetHashCode())
      End Select
    End Function

    Private Function Search(ByVal expr As Expression(Of Func(Of T, Boolean)), ByVal [property] As String) As IEnumerable(Of T) Implements IIndexSet(Of T).Search
      If expr.Body.NodeType = ExpressionType.Equal AndAlso TypeOf expr.Body Is BinaryExpression Then
        Dim exprCompiled As Func(Of T, Boolean) = expr.Compile()
        Dim binExp As BinaryExpression = CType(expr.Body, BinaryExpression)
        Dim leftSide As Expression = binExp.Left
        Dim rightSide As Expression = binExp.Right
        Dim rightHash As Nullable(Of Integer) = GetHashRight(rightSide)
        Return _internalIndexSet([property]).WhereEqual(rightHash.Value, exprCompiled)
      Else
        Return _internalIndexSet([property]).Where(expr.Compile())
      End If
    End Function


#End Region

    Public Sub LoadIndex(ByVal [property] As String) Implements IIndexSet(Of T).LoadIndex
      _internalIndexSet([property]).LoadComplete()
    End Sub
  End Class
End Namespace