﻿Imports System.Configuration
Imports System.Data.SqlClient

Namespace Data

  ''' <summary>
  ''' Provides an automated way to reuse open
  ''' database connections within the context
  ''' of a single data portal operation.
  ''' </summary>
  ''' <typeparam name="C">
  ''' Type of database connection object to use.
  ''' </typeparam>
  ''' <remarks>
  ''' This type stores the open database connection
  ''' in <see cref="Csla.ApplicationContext.LocalContext" />
  ''' and uses reference counting through
  ''' <see cref="IDisposable" /> to keep the connection
  ''' open for reuse by child objects, and to automatically
  ''' dispose the connection when the last consumer
  ''' has called Dispose."
  ''' </remarks>
  Public Class ConnectionManager(Of C As {IDbConnection, New})

    Implements IDisposable

    Private Shared _lock As New Object
    Private _connection As C
    Private _connectionString As String

    ''' <summary>
    ''' Gets the ConnectionManager object for the specified
    ''' connectionString.
    ''' </summary>
    ''' <param name="databaseName">
    ''' The database name.
    ''' </param>
    ''' <param name="getConnectionString">
    ''' True to get the connection string from
    ''' the config file. False to treat the
    ''' database name as the connection string.
    ''' </param>
    ''' <returns>ConnectionManager object for the connection.</returns>
    Public Shared Function GetManager(ByVal databaseName As String, ByVal getConnectionString As Boolean) As ConnectionManager(Of C)

      If getConnectionString Then
        Return GetManager(ConfigurationManager.ConnectionStrings(databaseName).ConnectionString)

      Else
        Return GetManager(databaseName)
      End If

    End Function

    ''' <summary>
    ''' Gets the ConnectionManager object for the specified
    ''' connectionString.
    ''' </summary>
    ''' <param name="connectionString">
    ''' The database connection string.
    ''' </param>
    ''' <returns>ConnectionManager object for the connection.</returns>
    Public Shared Function GetManager(ByVal connectionString As String) As ConnectionManager(Of C)

      SyncLock _lock
        Dim mgr As ConnectionManager(Of C)
        If ApplicationContext.LocalContext.Contains("__db:" & connectionString) Then
          mgr = CType(ApplicationContext.LocalContext("__db:" & connectionString), ConnectionManager(Of C))

        Else
          mgr = New ConnectionManager(Of C)(connectionString)
          ApplicationContext.LocalContext("__db:" & connectionString) = mgr
        End If
        mgr.AddRef()
        Return mgr
      End SyncLock

    End Function

    Private Sub New(ByVal connectionString As String)

      _connectionString = connectionString

      ' open connection
      _connection = New C
      _connection.ConnectionString = connectionString
      _connection.Open()

    End Sub

    ''' <summary>
    ''' Gets the open database connection object.
    ''' </summary>
    Public ReadOnly Property Connection() As C
      Get
        Return _connection
      End Get
    End Property

#Region " Reference counting "

    Private _refCount As Integer

    Private Sub AddRef()
      _refCount += 1
    End Sub

    Private Sub DeRef()

      SyncLock _lock
        _refCount -= 1
        If _refCount = 0 Then
          _connection.Dispose()
          ApplicationContext.LocalContext.Remove("__db:" & _connectionString)
        End If
      End SyncLock

    End Sub

#End Region

#Region " IDisposable "

    ''' <summary>
    ''' Dispose object, dereferencing or
    ''' disposing the connection it is
    ''' managing.
    ''' </summary>
    Public Sub Dispose() Implements IDisposable.Dispose
      DeRef()
    End Sub

#End Region

  End Class

End Namespace
