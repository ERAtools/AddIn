Imports ESRI.ArcGIS.Geodatabase
Imports ESRI.ArcGIS.Geometry
Imports System.Runtime.InteropServices

''' <summary>
''' The Distance analysis identifies features that intersect the user-specified analysis feature and buffer, and reports the distance of the layer�s feature from the analysis feature. This analysis will not run correctly if the user does not enter a buffer distance greater than zero. 
''' </summary>
<System.Runtime.InteropServices.ComVisible(False)> _
Public Class DistanceAnalysis
    Inherits ERAAnalysis

    ''' <summary>
    ''' Writes the XML results.
    ''' </summary>
    Public Overrides Sub WriteXmlResults()
        Dim pRowShape As ESRI.ArcGIS.Geometry.IGeometry

        Try
            Logging.Log.Debug("DistanceAnalysis.WriteXmlResults : " & Me.Name)

            'Initialize Counter and get first record
            Dim iRow As Integer = 1
            Dim pRow As ESRI.ArcGIS.Geodatabase.IFeature

            'If this is a polygon analysis feature, need to do a selection with a shape that represents
            'a union of the analysis feature and the buffer
            Dim FilterShape As IGeometry
            If AnalysisShape.Dimension = esriGeometryDimension.esriGeometry2Dimension Then
                FilterShape = Me.AnalysisPlusBufferShape
            Else
                'if analysis feature is point or line and a buffer is supplied, just use the buffer
                If BufferShape Is Nothing Then
                    FilterShape = AnalysisShape
                Else
                    FilterShape = BufferShape
                End If
            End If


            Dim pFeatureCursor As ESRI.ArcGIS.Geodatabase.IFeatureCursor = OpenFeatureCursor(FilterShape)

            pRow = pFeatureCursor.NextFeature
            If pRow Is Nothing Then
                WriteNoRecordsNode()
            Else
                'Temporarily add the extra fields
                Dim el As New EraField(Me)
                el.Name = "Location"
                el.FieldAlias = "Location"
                Me.m_ColFields.Add(el)
                Dim ed As New EraField(Me)
                ed.Name = "Distance"
                ed.FieldAlias = "Distance"
                Me.m_ColFields.Add(ed)
                'Add FIELDS and FIELD elements
                WriteFieldsNode()
                Me.m_ColFields.Remove(el)
                Me.m_ColFields.Remove(ed)


                'Add RECORD elements and <field name> elements
                While pRow IsNot Nothing
                    If HasInnerJoins Then
                        For Each childtable As EraTable In m_ColTables
                            If Not childtable.HasMatchingRecords(pRow) Then
                                pRow = pFeatureCursor.NextFeature
                                Continue While
                            End If
                        Next
                    End If
                    Dim RecordNode As Xml.XmlElement = m_OutputXmlDoc.CreateElement("RECORD")
                    RecordNode.SetAttribute("rec", iRow)

                    Dim ValuesNode As Xml.XmlElement = m_OutputXmlDoc.CreateElement("VALUES")
                    For Each NewField As EraField In m_ColFields
                        NewField.XmlRecordResults(ValuesNode, CType(pRow, IRow))
                    Next NewField
                    RecordNode.AppendChild(ValuesNode)

                    pRowShape = pRow.Shape

                    WriteDistanceNode(ValuesNode, pRowShape)

                    RecordNode.AppendChild(ValuesNode)
                    m_OutputXmlElement.AppendChild(RecordNode)

                    'add nested <TABLE> tag for any nested, one-to-many tables
                    For Each ChildTable As EraTable In Me.m_ColTables
                        ChildTable.WriteXmlTableResults(RecordNode, CType(pRow, IRow))
                    Next

                    'Increment counter and get next Record from QueryResults
                    pRow = pFeatureCursor.NextFeature
                    iRow += 1
                End While

                'Sort the Records
                If m_SortFieldsNode IsNot Nothing AndAlso m_SortFieldsNode.ChildNodes.Count > 0 Then
                    Dim sortList As New Generic.List(Of Xml.XmlElement)
                    For Each recordNode As Xml.XmlElement In m_OutputXmlElement.SelectNodes("RECORD")
                        sortList.Add(recordNode)
                    Next
                    For Each recordNode As Xml.XmlElement In sortList
                        m_OutputXmlElement.RemoveChild(recordNode)
                    Next
                    sortList.Sort(New RecordComparer(m_SortFieldsNode))
                    For Each recordNode As Xml.XmlElement In sortList
                        m_OutputXmlElement.AppendChild(recordNode)
                    Next
                End If
            End If 'pRow is nothing
            Marshal.ReleaseComObject(pFeatureCursor)
        Catch Ex As Exception
            AddError(Ex, "DistanceAnalysis.WriteXmlResults")
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Writes the distance node. Calculates the distance from the analysis feature to each selected feature in the layer. Based on the geometry type and whether the analysis shape and selected shape intersect, may also calculate the distance to the boundary of the analysis shape.
    ''' </summary>
    ''' <param name="RecordNode">The record node to which the distance node will be appended.</param>
    ''' <param name="pRowShape">Pointer to the geometry of a selected feature.</param>
    Private Sub WriteDistanceNode(ByVal RecordNode As Xml.XmlNode, ByVal pRowShape As ESRI.ArcGIS.Geometry.IGeometry)
        Try
            Dim dDistance As Double
            Dim strLocation As String = "Outside (within buffer)"

            'Dim Attrib As Xml.XmlAttribute

            dDistance = CalculateDistance(AnalysisShape, pRowShape)
            'strDistance = Format(dDistance, "#,##0") & " meters from analysis feature."

            If dDistance = 0 Then
                'The shapes intersect
                'If pBufferShape Is Nothing Then
                If AnalysisShape.Dimension = esriGeometryDimension.esriGeometry2Dimension Then
                    'get distance to boundary of feature shape
                    Dim pPolygon As IPolygon = AnalysisShape
                    Dim pPolyLine As ICurve = New Polyline
                    pPolygon.GetSubcurve(0, pPolygon.Length, False, pPolyLine)

                    dDistance = CalculateDistance(pPolyLine, pRowShape)
                    If dDistance = 0 Then
                        strLocation = "Intersecting edge of analysis feature"
                    Else
                        strLocation = "Inside analysis feature"
                    End If
                Else
                    strLocation = "Intersects analysis feature"
                End If
            End If

            Dim LocationNode As Xml.XmlNode = RecordNode.OwnerDocument.CreateElement(Xml.XmlConvert.EncodeLocalName(Name & "_Location"))
            LocationNode.InnerText = strLocation
            RecordNode.AppendChild(LocationNode)

            Dim DistanceNode As Xml.XmlNode = RecordNode.OwnerDocument.CreateElement(Xml.XmlConvert.EncodeLocalName(Name & "_Distance"))
            DistanceNode.InnerText = Format(dDistance, "#,###")
            'Attrib = RecordNode.OwnerDocument.CreateAttribute("order")
            'Attrib.Value = dDistance
            'DistanceNode.Attributes.SetNamedItem(Attrib)
            RecordNode.AppendChild(DistanceNode)
        Catch ex As Exception
            AddError(ex, "DistanceAnalysis.WriteDistanceNode")
            Throw
        End Try
    End Sub

    ''' <summary>
    ''' Calculates the distance between two shapes
    ''' </summary>
    ''' <param name="ShapeOne">One of the shapes being compared.</param>
    ''' <param name="ShapeTwo">Another of the shapes being compared..</param>
    ''' <returns>The distance between the two shapes, in meters.</returns>
    Private Function CalculateDistance(ByVal ShapeOne As ESRI.ArcGIS.Geometry.IGeometry, ByVal ShapeTwo As ESRI.ArcGIS.Geometry.IGeometry) As Double
        Try
            Dim pClone As ESRI.ArcGIS.esriSystem.IClone = ShapeOne
            Dim s1 As IGeometry = pClone.Clone
            pClone = ShapeTwo
            Dim s2 As IGeometry = pClone.Clone

            Dim ConversionFactor As Double = 1D
            If TypeOf (s1.SpatialReference) Is ProjectedCoordinateSystem Then
                Dim pcs As IProjectedCoordinateSystem = s1.SpatialReference
                Dim u As ILinearUnit = pcs.CoordinateUnit
                ConversionFactor = u.MetersPerUnit
            ElseIf TypeOf (s1.SpatialReference) Is GeographicCoordinateSystem Then
                ProjectShapeToAlbers(s1)
                ProjectShapeToAlbers(s2)
            Else
                'must be unknown, assume meters
            End If

            Dim pProxOp As ESRI.ArcGIS.Geometry.IProximityOperator
            pProxOp = s1
            'If s1.IsEmpty OrElse s2.IsEmpty Then
            '    Return Nothing
            'End If
            Return pProxOp.ReturnDistance(s2) * ConversionFactor
        Catch ex As Exception
            AddError(ex, "DistanceAnalysis.CalculateDistance")
            Throw
        End Try
    End Function
End Class
