Imports System.ComponentModel
Imports System.Collections.Specialized
Imports Csla.Core
Imports System.Linq.Expressions

''' <summary>
''' This is the base class from which most business collections
''' or lists will be derived.
''' </summary>
''' <typeparam name="T">Type of the business object being defined.</typeparam>
''' <typeparam name="C">Type of the child objects contained in the list.</typeparam>
<System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")> _
<Serializable()> _
Public MustInherit Class BusinessListBase( _
  Of T As BusinessListBase(Of T, C), C As {Core.IEditableBusinessObject})
  Inherits Core.ExtendedBindingList(Of C)

  Implements Core.IEditableCollection
  Implements Core.IUndoableObject
  Implements ICloneable
  Implements ISavable
  Implements IParent
  Implements IQueryable(Of C)
  Implements Linq.IIndexSearchable(Of C)
  Implements Core.IPositionMappable(Of C)
  Implements Server.IDataPortalTarget

#Region " Constructors "

  ''' <summary>
  ''' Creates an instance of the object.
  ''' </summary>
  ''' <remarks></remarks>
  Protected Sub New()

    _expression = Expression.Constant(Me)
    Initialize()

  End Sub

#End Region

#Region " Initialize "

  ''' <summary>
  ''' Override this method to set up event handlers so user
  ''' code in a partial class can respond to events raised by
  ''' generated code.
  ''' </summary>
  Protected Overridable Sub Initialize()
    ' allows a generated class to set up events to be
    ' handled by a partial class containing user code
  End Sub

#End Region

#Region " IsDirty, IsValid, IsSavable  "

  ''' <summary>
  ''' Gets a value indicating whether this object's data has been changed.
  ''' </summary>
  Public ReadOnly Property IsDirty() As Boolean Implements IEditableCollection.IsDirty, IEditableCollection.IsSelfDirty
    Get
      ' any non-new deletions make us dirty
      For Each item As C In DeletedList
        If Not item.IsNew Then
          Return True
        End If
      Next

      ' run through all the child objects
      ' and if any are dirty then the
      ' collection is dirty
      For Each Child As C In Me
        If Child.IsDirty Then Return True
      Next
      Return False
    End Get
  End Property

  ''' <summary>
  ''' Gets a value indicating whether this object is currently in
  ''' a valid state (has no broken validation rules).
  ''' </summary>
  Public Overridable ReadOnly Property IsValid() As Boolean Implements IEditableCollection.IsValid, IEditableCollection.IsSelfValid
    Get
      ' run through all the child objects
      ' and if any are invalid then the
      ' collection is invalid
      For Each child As C In Me
        If Not child.IsValid Then Return False
      Next
      Return True
    End Get
  End Property

  ''' <summary>
  ''' Returns <see langword="true" /> if this object is both dirty and valid.
  ''' </summary>
  ''' <returns>A value indicating if this object is both dirty and valid.</returns>
  <Browsable(False)> _
  Public Overridable ReadOnly Property IsSavable() As Boolean Implements IEditableCollection.IsSavable
    Get
      Dim auth = Csla.Security.AuthorizationRules.CanEditObject(Me.GetType())
      Return (IsDirty AndAlso IsValid AndAlso auth)
    End Get
  End Property

#End Region

#Region " Begin/Cancel/ApplyEdit "

  ''' <summary>
  ''' Starts a nested edit on the object.
  ''' </summary>
  ''' <remarks>
  ''' <para>
  ''' When this method is called the object takes a snapshot of
  ''' its current state (the values of its variables). This snapshot
  ''' can be restored by calling <see cref="CancelEdit" />
  ''' or committed by calling <see cref="ApplyEdit" />.
  ''' </para><para>
  ''' This is a nested operation. Each call to BeginEdit adds a new
  ''' snapshot of the object's state to a stack. You should ensure that 
  ''' for each call to BeginEdit there is a corresponding call to either 
  ''' CancelEdit or ApplyEdit to remove that snapshot from the stack.
  ''' </para><para>
  ''' See Chapters 2 and 3 for details on n-level undo and state stacking.
  ''' </para><para>
  ''' This method triggers the copying of all child object states.
  ''' </para>
  ''' </remarks>
  Public Sub BeginEdit() Implements IEditableCollection.BeginEdit
    If Me.IsChild Then
      Throw New  _
        NotSupportedException(My.Resources.NoBeginEditChildException)
    End If

    CopyState(Me.EditLevel + 1)
  End Sub

  ''' <summary>
  ''' Cancels the current edit process, restoring the object's state to
  ''' its previous values.
  ''' </summary>
  ''' <remarks>
  ''' Calling this method causes the most recently taken snapshot of the 
  ''' object's state to be restored. This resets the object's values
  ''' to the point of the last <see cref="BeginEdit" />
  ''' call.
  ''' <para>
  ''' This method triggers an undo in all child objects.
  ''' </para>
  ''' </remarks>
  Public Sub CancelEdit() Implements IEditableCollection.CancelEdit
    If Me.IsChild Then
      Throw New  _
        NotSupportedException(My.Resources.NoCancelEditChildException)
    End If

    UndoChanges(Me.EditLevel - 1)

  End Sub

  ''' <summary>
  ''' Commits the current edit process.
  ''' </summary>
  ''' <remarks>
  ''' Calling this method causes the most recently taken snapshot of the 
  ''' object's state to be discarded, thus committing any changes made
  ''' to the object's state since the last 
  ''' <see cref="BeginEdit" /> call.
  ''' <para>
  ''' This method triggers an <see cref="Core.BusinessBase.ApplyEdit"/>
  '''  in all child objects.
  ''' </para>
  ''' </remarks>
  Public Sub ApplyEdit() Implements IEditableCollection.ApplyEdit
    If Me.IsChild Then
      Throw New  _
        NotSupportedException(My.Resources.NoApplyEditChildException)
    End If

    AcceptChanges(Me.EditLevel - 1)
  End Sub

  ''' <summary>
  ''' Override this method to be notified when a child object's
  ''' <see cref="Core.BusinessBase.ApplyEdit" /> method has
  ''' completed.
  ''' </summary>
  ''' <param name="child">The child object that was edited.</param>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub EditChildComplete( _
    ByVal child As Core.IEditableBusinessObject) _
    Implements Core.IParent.ApplyEditChild

    ' do nothing, we don't really care
    ' when a child has its edits applied
  End Sub

#End Region

#Region " N-level undo "

  Private Sub CopyState(ByVal parentEditLevel As Integer) Implements Core.IUndoableObject.CopyState
    Dim Child As C

    If Me.EditLevel + 1 > parentEditLevel Then
      Throw New UndoException( _
        String.Format(My.Resources.EditLevelMismatchException, "CopyState"))
    End If

    ' we are going a level deeper in editing
    _editLevel += 1

    ' cascade the call to all child objects
    For Each Child In Me
      Child.CopyState(_editLevel)
    Next

    ' cascade the call to all deleted child objects
    For Each Child In DeletedList
      Child.CopyState(_editLevel)
    Next
  End Sub

  Private _completelyRemoveChild As Boolean

  Private Sub UndoChanges(ByVal parentEditLevel As Integer) Implements Core.IUndoableObject.UndoChanges
    Dim child As C
    Dim index As Integer

    If Me.EditLevel - 1 < parentEditLevel Then
      Throw New UndoException( _
        String.Format(My.Resources.EditLevelMismatchException, "UndoChanges"))
    End If

    ' we are coming up one edit level
    _editLevel -= 1
    If _editLevel < 0 Then _editLevel = 0

    Dim oldRLCE = RaiseListChangedEvents
    RaiseListChangedEvents = False
    Try
      ' Cancel edit on all current items
      For index = Count - 1 To 0 Step -1
        child = Me(index)
        child.UndoChanges(_editLevel)
        ' if item is below its point of addition, remove
        If child.EditLevelAdded > _editLevel Then
          Dim oldAllowRemove As Boolean = Me.AllowRemove
          Try
            Me.AllowRemove = True
            _completelyRemoveChild = True
            RemoveAt(index)
          Finally
            _completelyRemoveChild = False
            Me.AllowRemove = oldAllowRemove
          End Try
        End If
      Next

      ' cancel edit on all deleted items
      For index = DeletedList.Count - 1 To 0 Step -1
        child = DeletedList.Item(index)
        child.UndoChanges(_editLevel)
        If child.EditLevelAdded > _editLevel Then
          ' if item is below its point of addition, remove
          DeletedList.RemoveAt(index)
        Else
          ' if item is no longer deleted move back to main list
          If Not child.IsDeleted Then UnDeleteChild(child)
        End If
      Next

    Finally
      RaiseListChangedEvents = oldRLCE
      OnListChanged(New ListChangedEventArgs(ListChangedType.Reset, -1))
    End Try

  End Sub

  Private Sub AcceptChanges(ByVal parentEditLevel As Integer) _
    Implements Core.IUndoableObject.AcceptChanges
    Dim child As C
    Dim index As Integer

    If Me.EditLevel - 1 < parentEditLevel Then
      Throw New UndoException( _
        String.Format(My.Resources.EditLevelMismatchException, "AcceptChanges"))
    End If

    ' we are coming up one edit level
    _editLevel -= 1
    If _editLevel < 0 Then _editLevel = 0

    ' cascade the call to all child objects
    For Each child In Me
      child.AcceptChanges(_editLevel)
      ' if item is below its point of addition, lower point of addition
      If child.EditLevelAdded > _editLevel Then child.EditLevelAdded = _editLevel
    Next

    ' cascade the call to all deleted child objects
    'For Each Child In deletedList
    For index = DeletedList.Count - 1 To 0 Step -1
      child = DeletedList.Item(index)
      child.AcceptChanges(_editLevel)
      ' if item is below its point of addition, remove
      If child.EditLevelAdded > _editLevel Then
        DeletedList.RemoveAt(index)
      End If
    Next
  End Sub

#End Region

#Region " Delete and Undelete child "

  Private _deletedList As List(Of C)

  ''' <summary>
  ''' A collection containing all child objects marked
  ''' for deletion.
  ''' </summary>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")> _
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected ReadOnly Property DeletedList() As List(Of C)
    Get
      If _deletedList Is Nothing Then
        _deletedList = New List(Of C)
      End If
      Return _deletedList
    End Get
  End Property

  Private Sub DeleteChild(ByVal child As C)
    ' set child edit level
    UndoableBase.ResetChildEditLevel(child, Me.EditLevel, False)
    'remove from the index
    RemoveIndexItem(child)
    'remove from the position map
    RemoveFromMap(child)
    ' mark the object as deleted
    child.DeleteChild()
    ' and add it to the deleted collection for storage
    DeletedList.Add(child)
  End Sub

  Private Sub UnDeleteChild(ByVal child As C)

    ' since the object is no longer deleted, remove it from
    ' the deleted collection
    DeletedList.Remove(child)

    ' we are inserting an _existing_ object so
    ' we need to preserve the object's editleveladded value
    ' because it will be changed by the normal add process
    Dim SaveLevel As Integer = child.EditLevelAdded
    Add(child)
    child.EditLevelAdded = SaveLevel

  End Sub

  ''' <summary>
  ''' Returns <see langword="true"/> if the internal deleted list
  ''' contains the specified child object.
  ''' </summary>
  ''' <param name="item">Child object to check.</param>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Public Function ContainsDeleted(ByVal item As C) As Boolean

    Return DeletedList.Contains(item)

  End Function

#End Region

#Region " Insert, Remove, Clear "

  ''' <summary>
  ''' This method is called by a child object when it
  ''' wants to be removed from the collection.
  ''' </summary>
  ''' <param name="child">The child object to remove.</param>
  Private Sub RemoveChild(ByVal child As Core.IEditableBusinessObject) _
    Implements Core.IEditableCollection.RemoveChild, IParent.RemoveChild
    RemoveFromMap(DirectCast(child, C))
    Remove(DirectCast(child, C))
    RemoveIndexItem(DirectCast(child, C))
  End Sub

  ''' <summary>
  ''' Sets the edit level of the child object as it is added.
  ''' </summary>
  ''' <param name="index">Index of the item to insert.</param>
  ''' <param name="item">Item to insert.</param>
  Protected Overrides Sub InsertItem(ByVal index As Integer, ByVal item As C)

    ' set parent reference
    item.SetParent(Me)
    ' set child edit level
    UndoableBase.ResetChildEditLevel(item, Me.EditLevel, False)
    ' when an object is inserted we assume it is
    ' a new object and so the edit level when it was
    ' added must be set
    item.EditLevelAdded = _editLevel

    InsertIndexItem(item)
    MyBase.InsertItem(index, item)
    InsertIntoMap(item, index)
  End Sub

  ''' <summary>
  ''' Marks the child object for deletion and moves it to
  ''' the collection of deleted objects.
  ''' </summary>
  ''' <param name="index">Index of the item to remove.</param>
  Protected Overrides Sub RemoveItem(ByVal index As Integer)
    ' when an object is 'removed' it is really
    ' being deleted, so do the deletion work
    Dim child As C = Me(index)
    Dim oldRaiseListChangedEvents As Boolean = _
      Me.RaiseListChangedEvents
    Try
      Me.RaiseListChangedEvents = False
      RemoveIndexItem(child)
      RemoveFromMap(child)
      MyBase.RemoveItem(index)

    Finally
      Me.RaiseListChangedEvents = oldRaiseListChangedEvents
    End Try
    If Not _completelyRemoveChild Then
      ' the child shouldn't be completely removed,
      ' so copy it to the deleted list
      CopyToDeletedList(child)
    End If
    If RaiseListChangedEvents Then
      OnListChanged(New ListChangedEventArgs(ListChangedType.ItemDeleted, index))
    End If
  End Sub

  Private Sub CopyToDeletedList(ByVal child As C)

    DeleteChild(child)
    Dim c As System.ComponentModel.INotifyPropertyChanged = TryCast(child, System.ComponentModel.INotifyPropertyChanged)
    If c IsNot Nothing Then
      RemoveHandler c.PropertyChanged, AddressOf Child_PropertyChanged
    End If

  End Sub

  ''' <summary>
  ''' Clears the collection, moving all active
  ''' items to the deleted list.
  ''' </summary>
  Protected Overrides Sub ClearItems()
    While MyBase.Count > 0
      RemoveItem(0)
    End While
    MyBase.ClearItems()
  End Sub

  ''' <summary>
  ''' Replaces the item at the specified index with
  ''' the specified item, first moving the original
  ''' item to the deleted list.
  ''' </summary>
  ''' <param name="index">The zero-based index of the item to replace.</param>
  ''' <param name="item">
  ''' The new value for the item at the specified index. 
  ''' The value can be null for reference types.
  ''' </param>
  ''' <remarks></remarks>
  Protected Overrides Sub SetItem(ByVal index As Integer, ByVal item As C)
    ' copy the original object to the deleted list,
    ' marking as deleted, etc.
    Dim child As C = Nothing
    If Not ReferenceEquals(DirectCast(Me(index), C), item) Then
      child = Me(index)
    End If
    ' replace the original object with this new
    ' object
    Dim oldRaiseListChangedEvents As Boolean = _
      Me.RaiseListChangedEvents
    Try
      Me.RaiseListChangedEvents = False
      item.SetParent(Me)
      ' set child edit level
      UndoableBase.ResetChildEditLevel(item, Me.EditLevel, False)
      ' reset EditLevelAdded 
      item.EditLevelAdded = Me.EditLevel
      ' add to list
      ReIndexItem(item)
      RemoveFromMap(item)
      MyBase.SetItem(index, item)
      InsertIntoMap(item, index)

    Finally
      Me.RaiseListChangedEvents = oldRaiseListChangedEvents
    End Try
    If child IsNot Nothing Then
      CopyToDeletedList(child)
    End If
    If RaiseListChangedEvents Then
      OnListChanged(New ListChangedEventArgs(ListChangedType.ItemChanged, index))
    End If
  End Sub

#End Region

#Region "Indexing"

  <NonSerialized()> _
  Private _indexSet As Linq.IIndexSet(Of C)

  Private Sub DeferredLoadIndexIfNotLoaded()
    If _indexSet Is Nothing Then
      _indexSet = New Csla.Linq.IndexSet(Of C)()
    End If
  End Sub

  ''' <summary>
  ''' Allows users of CSLA to override the indexing behavior of BLB
  ''' </summary>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Public Property IndexingProvider() As Type
    Get
      DeferredLoadIndexIfNotLoaded()
      Return _indexSet.GetType()
    End Get
    Set(ByVal value As Type)
      If value.IsClass AndAlso (Not value.IsAbstract) AndAlso value.IsAssignableFrom(GetType(Linq.IIndexSet(Of C))) Then
        _indexSet = TryCast(Activator.CreateInstance(value), Linq.IIndexSet(Of C))
        ReIndexAll()
      End If
    End Set
  End Property


  Private Function IndexModeFor(ByVal [property] As String) As IndexModeEnum
    DeferredLoadIndexIfNotLoaded()
    If _indexSet.HasIndexFor([property]) Then
      Return _indexSet([property]).IndexMode
    Else
      Return IndexModeEnum.IndexModeNever
    End If
  End Function

  Private Function IndexLoadedFor(ByVal [property] As String) As Boolean
    DeferredLoadIndexIfNotLoaded()
    If _indexSet.HasIndexFor([property]) Then
      Return _indexSet([property]).Loaded
    Else
      Return False
    End If
  End Function

  Private Sub LoadIndexIfNotLoaded(ByVal [property] As String)
    If IndexModeFor([property]) <> IndexModeEnum.IndexModeNever Then
      If (Not IndexLoadedFor([property])) Then
        _indexSet.LoadIndex([property])
        ReIndex([property])
      End If
    End If
  End Sub

  Private Sub InsertIndexItem(ByVal item As C)
    DeferredLoadIndexIfNotLoaded()
    _indexSet.InsertItem(item)
  End Sub

  Private Sub InsertIndexItem(ByVal item As C, ByVal [property] As String)
    DeferredLoadIndexIfNotLoaded()
    _indexSet.InsertItem(item, [property])
  End Sub

  Private Sub RemoveIndexItem(ByVal item As C)
    DeferredLoadIndexIfNotLoaded()
    _indexSet.RemoveItem(item)
  End Sub

  Private Sub RemoveIndexItem(ByVal item As C, ByVal [property] As String)
    DeferredLoadIndexIfNotLoaded()
    _indexSet.RemoveItem(item, [property])
  End Sub

  Private Sub ReIndexItem(ByVal item As C, ByVal [property] As String)
    DeferredLoadIndexIfNotLoaded()
    _indexSet.ReIndexItem(item, [property])
  End Sub

  Private Sub ReIndexItem(ByVal item As C)
    DeferredLoadIndexIfNotLoaded()
    _indexSet.ReIndexItem(item)
  End Sub

  Private Sub ReIndexAll()
    DeferredLoadIndexIfNotLoaded()
    _indexSet.ClearIndexes()
    For Each item As C In Me
      InsertIndexItem(item)
    Next item
  End Sub

  Private Sub ReIndex(ByVal [property] As String)
    DeferredLoadIndexIfNotLoaded()
    _indexSet.ClearIndex([property])
    For Each item As C In Me
      InsertIndexItem(item, [property])
    Next item
    _indexSet([property]).LoadComplete()
  End Sub

#End Region

#Region "PositionMapping"

  <NonSerialized()> _
  Private _positionMap As Core.PositionMap(Of C)

  Private Sub DeferredLoadPositionMapIfNotLoaded()
    If _positionMap Is Nothing Then
      _positionMap = New Csla.Core.PositionMap(Of C)(Me)
    End If
  End Sub

  Private Sub RemoveFromMap(ByVal child As C)
    DeferredLoadPositionMapIfNotLoaded()
    _positionMap.RemoveFromMap(child)
  End Sub

  Private Sub InsertIntoMap(ByVal child As C, ByVal position As Integer)
    DeferredLoadPositionMapIfNotLoaded()
    _positionMap.InsertIntoMap(child, position)
  End Sub

  Private Sub AddToMap(ByVal child As C)
    DeferredLoadPositionMapIfNotLoaded()
    _positionMap.AddToMap(child)

  End Sub

  Private Function PositionOf(ByVal item As C) As Integer Implements IPositionMappable(Of C).PositionOf
    DeferredLoadPositionMapIfNotLoaded()
    Return _positionMap.PositionOf(item)
  End Function

#End Region

#Region "Where Implementation"
  ''' <summary>
  ''' Iterates through a set of items according to the expression passed to it.
  ''' </summary>
  Public Function SearchByExpression(ByVal expr As Expression(Of Func(Of C, Boolean))) As IEnumerable(Of C) Implements Linq.IIndexSearchable(Of C).SearchByExpression
    DeferredLoadIndexIfNotLoaded()
    Dim [property] As String = _indexSet.HasIndexFor(expr)
    If [property] IsNot Nothing AndAlso IndexModeFor([property]) <> IndexModeEnum.IndexModeNever Then
      LoadIndexIfNotLoaded([property])
      Return _indexSet.Search(expr, [property])
    Else
      Dim sourceEnum As IEnumerable(Of C) = Me.AsEnumerable()
      Return sourceEnum.Where(expr.Compile())
    End If
  End Function

#End Region

#Region " Edit level tracking "

  ' keep track of how many edit levels we have
  Private _editLevel As Integer

  ''' <summary>
  ''' Returns the current edit level of the object.
  ''' </summary>
  <EditorBrowsable(EditorBrowsableState.Never)> _
  Protected ReadOnly Property EditLevel() As Integer Implements IUndoableObject.EditLevel
    Get
      Return _editLevel
    End Get
  End Property

#End Region

#Region " IsChild "

  <NotUndoable()> _
  Private _isChild As Boolean = False

  ''' <summary>
  ''' Indicates whether this collection object is a child object.
  ''' </summary>
  ''' <returns>True if this is a child object.</returns>
  Protected ReadOnly Property IsChild() As Boolean
    Get
      Return _isChild
    End Get
  End Property

  ''' <summary>
  ''' Marks the object as being a child object.
  ''' </summary>
  ''' <remarks>
  ''' <para>
  ''' By default all business objects are 'parent' objects. This means
  ''' that they can be directly retrieved and updated into the database.
  ''' </para><para>
  ''' We often also need child objects. These are objects which are contained
  ''' within other objects. For instance, a parent Invoice object will contain
  ''' child LineItem objects.
  ''' </para><para>
  ''' To create a child object, the MarkAsChild method must be called as the
  ''' object is created. Please see Chapter 7 for details on the use of the
  ''' MarkAsChild method.
  ''' </para>
  ''' </remarks>
  Protected Sub MarkAsChild() Implements Server.IDataPortalTarget.MarkAsChild
    _isChild = True
  End Sub

#End Region

#Region " ICloneable "

  Private Function ICloneable_Clone() As Object Implements ICloneable.Clone

    Return GetClone()

  End Function

  ''' <summary>
  ''' Creates a clone of the object.
  ''' </summary>
  ''' <returns>
  ''' A new object containing the exact data of the original object.
  ''' </returns>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Function GetClone() As Object

    Return ObjectCloner.Clone(Me)

  End Function

  ''' <summary>
  ''' Creates a clone of the object.
  ''' </summary>
  ''' <returns>
  ''' A new object containing the exact data of the original object.
  ''' </returns>
  Public Overloads Function Clone() As T

    Return DirectCast(GetClone(), T)

  End Function

#End Region

#Region " Cascade Child events "

  Private Sub Child_PropertyChanged(ByVal sender As Object, _
    ByVal e As System.ComponentModel.PropertyChangedEventArgs)

    If RaiseListChangedEvents Then
      For index As Integer = 0 To Count - 1
        If ReferenceEquals(Me(index), sender) Then
          Dim descriptor As PropertyDescriptor = GetPropertyDescriptor(e.PropertyName)
          If descriptor IsNot Nothing Then
            OnListChanged(New System.ComponentModel.ListChangedEventArgs( _
              ComponentModel.ListChangedType.ItemChanged, index, descriptor))

          Else
            OnListChanged(New System.ComponentModel.ListChangedEventArgs( _
              ComponentModel.ListChangedType.ItemChanged, index))
          End If
          Exit For
        End If
      Next
    End If

  End Sub

  Private Shared mPropertyDescriptors As PropertyDescriptorCollection

  Private Function GetPropertyDescriptor(ByVal propertyName As String) As PropertyDescriptor

    If mPropertyDescriptors Is Nothing Then
      mPropertyDescriptors = TypeDescriptor.GetProperties(GetType(C))
    End If
    Dim result As PropertyDescriptor = Nothing
    For Each desc As PropertyDescriptor In mPropertyDescriptors
      If desc.Name = propertyName Then
        result = desc
        Exit For
      End If
    Next
    Return result

  End Function

#End Region

#Region " Serialization Notification "

  <OnDeserialized()> _
  Private Sub OnDeserializedHandler(ByVal context As StreamingContext)

    OnDeserialized(context)
    For Each child As Core.IEditableBusinessObject In Me
      child.SetParent(Me)
      Dim c As System.ComponentModel.INotifyPropertyChanged = TryCast(child, System.ComponentModel.INotifyPropertyChanged)
      If c IsNot Nothing Then
        AddHandler c.PropertyChanged, AddressOf Child_PropertyChanged
      End If
    Next
    For Each child As Core.IEditableBusinessObject In DeletedList
      child.SetParent(Me)
    Next

  End Sub

  ''' <summary>
  ''' This method is called on a newly deserialized object
  ''' after deserialization is complete.
  ''' </summary>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub OnDeserialized(ByVal context As StreamingContext)

    ' do nothing - this is here so a subclass
    ' could override if needed

  End Sub

#End Region

#Region " Child Data Access "

  ''' <summary>
  ''' Initializes a new instance of the object
  ''' with default values.
  ''' </summary>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub Child_Create()

    ' do nothing - list self-initializes

  End Sub

  ''' <summary>
  ''' Saves all items in the list, automatically
  ''' performing insert, update or delete operations
  ''' as necessary.
  ''' </summary>
  ''' <param name="parameters">
  ''' Optional parameters passed to child update
  ''' methods.
  ''' </param>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub Child_Update(ByVal ParamArray parameters() As Object)

    Dim oldRLCE = Me.RaiseListChangedEvents
    Me.RaiseListChangedEvents = False
    Try
      For Each child In DeletedList
        DataPortal.UpdateChild(child, parameters)
      Next
      DeletedList.Clear()

      For Each child In Me
        DataPortal.UpdateChild(child, parameters)
      Next

    Finally
      Me.RaiseListChangedEvents = oldRLCE
    End Try

  End Sub

#End Region

#Region " Data Access "

  ''' <summary>
  ''' Saves the object to the database.
  ''' </summary>
  ''' <remarks>
  ''' <para>
  ''' Calling this method starts the save operation, causing the all child
  ''' objects to be inserted, updated or deleted within the database based on the
  ''' each object's current state.
  ''' </para><para>
  ''' All this is contingent on <see cref="IsDirty" />. If
  ''' this value is <see langword="false"/>, no data operation occurs. 
  ''' It is also contingent on <see cref="IsValid" />. If this value is 
  ''' <see langword="false"/> an exception will be thrown to 
  ''' indicate that the UI attempted to save an invalid object.
  ''' </para><para>
  ''' It is important to note that this method returns a new version of the
  ''' business collection that contains any data updated during the save operation.
  ''' You MUST update all object references to use this new version of the
  ''' business collection in order to have access to the correct object data.
  ''' </para><para>
  ''' You can override this method to add your own custom behaviors to the save
  ''' operation. For instance, you may add some security checks to make sure
  ''' the user can save the object. If all security checks pass, you would then
  ''' invoke the base Save method via <c>MyBase.Save()</c>.
  ''' </para>
  ''' </remarks>
  ''' <returns>A new object containing the saved values.</returns>
  Public Overridable Function Save() As T

    If Me.IsChild Then
      Throw New NotSupportedException(My.Resources.NoSaveChildException)
    End If

    If _editLevel > 0 Then
      Throw New Validation.ValidationException(My.Resources.NoSaveEditingException)
    End If

    If Not IsValid Then
      Throw New Validation.ValidationException(My.Resources.NoSaveInvalidException)
    End If

    Dim result As T
    If IsDirty Then
      result = DirectCast(DataPortal.Update(Me), T)

    Else
      result = DirectCast(Me, T)
    End If

    OnSaved(result)
    Return result

  End Function

  ''' <summary>
  ''' Override this method to load a new business object with default
  ''' values from the database.
  ''' </summary>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member")> _
  Protected Overridable Sub DataPortal_Create()
    Throw New NotSupportedException(My.Resources.CreateNotSupportedException)
  End Sub

  ''' <summary>
  ''' Override this method to allow retrieval of an existing business
  ''' object based on data in the database.
  ''' </summary>
  ''' <param name="criteria">An object containing criteria values to identify the object.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member")> _
  Protected Overridable Sub DataPortal_Fetch(ByVal criteria As Object)
    Throw New NotSupportedException(My.Resources.FetchNotSupportedException)
  End Sub

  ''' <summary>
  ''' Override this method to allow insert, update or deletion of a business
  ''' object.
  ''' </summary>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member")> _
  Protected Overridable Sub DataPortal_Update()
    Throw New NotSupportedException(My.Resources.UpdateNotSupportedException)
  End Sub

  ''' <summary>
  ''' Override this method to allow immediate deletion of a business object.
  ''' </summary>
  ''' <param name="criteria">An object containing criteria values to identify the object.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member")> _
  Protected Overridable Sub DataPortal_Delete(ByVal criteria As Object)
    Throw New NotSupportedException(My.Resources.DeleteNotSupportedException)
  End Sub

  ''' <summary>
  ''' Called by the server-side DataPortal prior to calling the 
  ''' requested DataPortal_xyz method.
  ''' </summary>
  ''' <param name="e">The DataPortalContext object passed to the DataPortal.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member")> _
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub DataPortal_OnDataPortalInvoke(ByVal e As DataPortalEventArgs) Implements Server.IDataPortalTarget.DataPortal_OnDataPortalInvoke

  End Sub

  ''' <summary>
  ''' Called by the server-side DataPortal after calling the 
  ''' requested DataPortal_xyz method.
  ''' </summary>
  ''' <param name="e">The DataPortalContext object passed to the DataPortal.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member")> _
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub DataPortal_OnDataPortalInvokeComplete(ByVal e As DataPortalEventArgs) Implements Server.IDataPortalTarget.DataPortal_OnDataPortalInvokeComplete

  End Sub

  ''' <summary>
  ''' Called by the server-side DataPortal if an exception
  ''' occurs during data access.
  ''' </summary>
  ''' <param name="e">The DataPortalContext object passed to the DataPortal.</param>
  ''' <param name="ex">The Exception thrown during data access.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member")> _
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub DataPortal_OnDataPortalException(ByVal e As DataPortalEventArgs, ByVal ex As Exception) Implements Server.IDataPortalTarget.DataPortal_OnDataPortalException

  End Sub

  ''' <summary>
  ''' Called by the server-side DataPortal prior to calling the 
  ''' requested DataPortal_XYZ method.
  ''' </summary>
  ''' <param name="e">The DataPortalContext object passed to the DataPortal.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member"), EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub Child_OnDataPortalInvoke(ByVal e As DataPortalEventArgs) Implements Server.IDataPortalTarget.Child_OnDataPortalInvoke

  End Sub

  ''' <summary>
  ''' Called by the server-side DataPortal after calling the 
  ''' requested DataPortal_XYZ method.
  ''' </summary>
  ''' <param name="e">The DataPortalContext object passed to the DataPortal.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member"), EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub Child_OnDataPortalInvokeComplete(ByVal e As DataPortalEventArgs) Implements Server.IDataPortalTarget.Child_OnDataPortalInvokeComplete

  End Sub

  ''' <summary>
  ''' Called by the server-side DataPortal if an exception
  ''' occurs during data access.
  ''' </summary>
  ''' <param name="e">The DataPortalContext object passed to the DataPortal.</param>
  ''' <param name="ex">The Exception thrown during data access.</param>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores", MessageId:="Member"), EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Overridable Sub Child_OnDataPortalException(ByVal e As DataPortalEventArgs, ByVal ex As Exception) Implements Server.IDataPortalTarget.Child_OnDataPortalException

  End Sub

#End Region

#Region " ISavable implementation "

  Private Function ISavable_Save() As Object Implements ISavable.Save
    Return Save()
  End Function

  Private Sub ISavable_SaveComplete(ByVal newObject As Object) Implements Core.ISavable.SaveComplete
    OnSaved(DirectCast(newObject, T))
  End Sub

  <NonSerialized()> _
  <NotUndoable()> _
  Private _nonSerializableSavedHandlers As EventHandler(Of Csla.Core.SavedEventArgs)
  <NotUndoable()> _
  Private _serializableSavedHandlers As EventHandler(Of Csla.Core.SavedEventArgs)

  ''' <summary>
  ''' Event raised when an object has been saved.
  ''' </summary>
  <System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")> _
  Public Custom Event Saved As EventHandler(Of Csla.Core.SavedEventArgs) Implements Core.ISavable.Saved
    AddHandler(ByVal value As EventHandler(Of Csla.Core.SavedEventArgs))
      If value.Method.IsPublic AndAlso (value.Method.DeclaringType.IsSerializable OrElse value.Method.IsStatic) Then
        _serializableSavedHandlers = CType(System.Delegate.Combine(_serializableSavedHandlers, value), EventHandler(Of Csla.Core.SavedEventArgs))
      Else
        _nonSerializableSavedHandlers = CType(System.Delegate.Combine(_nonSerializableSavedHandlers, value), EventHandler(Of Csla.Core.SavedEventArgs))
      End If
    End AddHandler
    RemoveHandler(ByVal value As EventHandler(Of Csla.Core.SavedEventArgs))
      If value.Method.IsPublic AndAlso (value.Method.DeclaringType.IsSerializable OrElse value.Method.IsStatic) Then
        _serializableSavedHandlers = CType(System.Delegate.Remove(_serializableSavedHandlers, value), EventHandler(Of Csla.Core.SavedEventArgs))
      Else
        _nonSerializableSavedHandlers = CType(System.Delegate.Remove(_nonSerializableSavedHandlers, value), EventHandler(Of Csla.Core.SavedEventArgs))
      End If
    End RemoveHandler
    RaiseEvent(ByVal sender As System.Object, ByVal e As Csla.Core.SavedEventArgs)
      If Not _nonSerializableSavedHandlers Is Nothing Then
        _nonSerializableSavedHandlers.Invoke(Me, e)
      End If
      If Not _serializableSavedHandlers Is Nothing Then
        _serializableSavedHandlers.Invoke(Me, e)
      End If
    End RaiseEvent
  End Event

  ''' <summary>
  ''' Raises the <see cref="Saved"/> event, indicating that the
  ''' object has been saved, and providing a reference
  ''' to the new object instance.
  ''' </summary>
  ''' <param name="newObject">The new object instance.</param>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected Sub OnSaved(ByVal newObject As T)

    RaiseEvent Saved(Me, New Csla.Core.SavedEventArgs(newObject))

  End Sub

#End Region

#Region " Parent/Child link "

  <NotUndoable()> _
  <NonSerialized()> _
  Private _parent As Core.IParent

  ''' <summary>
  ''' Provide access to the parent reference for use
  ''' in child object code.
  ''' </summary>
  ''' <remarks>
  ''' This value will be Nothing for root objects.
  ''' </remarks>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Protected ReadOnly Property Parent() As Core.IParent
    Get
      Return _parent
    End Get
  End Property

  ''' <summary>
  ''' Used by BusinessListBase as a child object is 
  ''' created to tell the child object about its
  ''' parent.
  ''' </summary>
  ''' <param name="parent">A reference to the parent collection object.</param>
  Friend Sub SetParent(ByVal parent As Core.IParent) Implements IEditableCollection.SetParent

    _parent = parent

  End Sub

#End Region

#Region " ToArray "

  ''' <summary>
  ''' Get an array containing all items in the list.
  ''' </summary>
  Public Function ToArray() As C()

    Dim result As New List(Of C)
    For Each Item As C In Me
      result.Add(Item)
    Next
    Return result.ToArray

  End Function

#End Region

#Region " ITrackStatus "

  Private ReadOnly Property IsNew() As Boolean Implements ITrackStatus.IsNew
    Get
      Return False
    End Get
  End Property

  Private ReadOnly Property IsDeleted() As Boolean Implements ITrackStatus.IsDeleted
    Get
      Return False
    End Get
  End Property

#End Region

#Region "LinqIntegration"

  <NonSerialized()> _
  Private _queryProvider As IQueryProvider

  <NonSerialized()> _
  Private _expression As Expression

  Friend Sub SetCurrentExpression(ByVal ex As Expression)
    _expression = ex
  End Sub

  Private Sub LoadProviderIfNotLoaded()
    _queryProvider = New Csla.Linq.CslaQueryProvider(Of T, C)(Me)
  End Sub

#Region "IQueryable Members"

  ''' <summary>
  ''' Required for IQueryable - returns the ElementType (maps to C)
  ''' </summary>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Public ReadOnly Property ElementType() As Type Implements IQueryable(Of C).ElementType
    Get
      Return GetType(C)
    End Get
  End Property

  ''' <summary>
  ''' Last expression used in a linq query
  ''' </summary>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Public ReadOnly Property Expression() As Expression Implements IQueryable(Of C).Expression
    Get
      Return _expression
    End Get
  End Property

  ''' <summary>
  ''' Query provider currently being used
  ''' </summary>
  <EditorBrowsable(EditorBrowsableState.Advanced)> _
  Public ReadOnly Property Provider() As IQueryProvider Implements IQueryable(Of C).Provider
    Get
      Return New Linq.CslaQueryProvider(Of T, C)(Me)
    End Get
  End Property

#End Region

#End Region

#Region " IDataPortalTarget implementation "

  Private Sub MarkNew() Implements Server.IDataPortalTarget.MarkNew

  End Sub

  Private Sub MarkOld() Implements Server.IDataPortalTarget.MarkOld

  End Sub

#End Region

End Class
