using System;
using System.Windows;
using System.Windows.Input;
using ESRI.ArcGIS.Client;
using ESRI.ArcGIS.Client.Tasks;

namespace DeedDrafter
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    double _defaultMapZoomFactor = 1.0;
    Configuration _xmlConfiguation;

    public MainWindow()
    {
      InitializeComponent();

      if (License.ShowWindow() != true)
      {
        Application.Current.Shutdown();
      }
      else if (!IsNet45OrNewer())
      {
        MessageBox.Show((string)Application.Current.FindResource("strDotNetVersionError"),
                        (string)Application.Current.FindResource("strTitle"));

        Application.Current.Shutdown();
      }
      else
      {
        string statusMessage;
        _xmlConfiguation = new Configuration();
        if (!_xmlConfiguation.ReadConfiguationFile("DeedDrafterConfiguration.xml", out statusMessage))
        {
          MessageBox.Show((string)Application.Current.FindResource("strConfigReadError") + "\n\n" + statusMessage, (string)Application.Current.FindResource("strTitle"));

          Application.Current.Shutdown();
        }
        else
        {
          ConfigureApplication(ref _xmlConfiguation);
          ResetGrid();
          _defaultMapZoomFactor = ParcelMap.ZoomFactor;
        }
      }
    }

    public static bool IsNet45OrNewer()
    {
      // Class "ReflectionContext" exists from .NET 4.5 onwards.
      return Type.GetType("System.Reflection.ReflectionContext", false) != null;
    }

    private void ConfigureApplication(ref Configuration xmlConfiguation)
    {
      Title = xmlConfiguation.Title;
      Width = xmlConfiguation.Width;
      Height = xmlConfiguation.Height;

      ParcelLines.MaxHeight = _xmlConfiguation.MaxGridHeight;

      // insert layer before [graphical] layers defined in xaml

      Int32 layerIndex = 0;
      String lastUnit = "";
      foreach (LayerDefinition definition in xmlConfiguation.DisplayLayers)
      {
        if (definition.Type == "dynamic")
        {
          ArcGISDynamicMapServiceLayer dynamicMS = new ArcGISDynamicMapServiceLayer();
          dynamicMS.Url = definition.Url;
          dynamicMS.ID = definition.Id;
          dynamicMS.InitializationFailed += Layer_InitializationFailed;
          ParcelMap.Layers.Insert(layerIndex++, dynamicMS);
          if ((dynamicMS.Units != null) && (dynamicMS.Units != "") && !xmlConfiguation.HasSpatialReferenceUnit)
            lastUnit = dynamicMS.Units;
        }

        if (definition.Type == "tiled")
        {
          ArcGISTiledMapServiceLayer tiledMS = new ArcGISTiledMapServiceLayer();
          tiledMS.Url = definition.Url;
          tiledMS.ID = definition.Id;
          tiledMS.InitializationFailed += Layer_InitializationFailed;
          ParcelMap.Layers.Insert(layerIndex++, tiledMS);
          if ((tiledMS.Units != null) && (tiledMS.Units != "") && !xmlConfiguation.HasSpatialReferenceUnit)
            lastUnit = tiledMS.Units;
        }

        if (definition.Type == "image")
        {
          ArcGISImageServiceLayer imageS = new ArcGISImageServiceLayer();
          imageS.Url = definition.Url;
          imageS.ID = definition.Id;
          imageS.InitializationFailed += Layer_InitializationFailed;
          ParcelMap.Layers.Insert(layerIndex++, imageS);
        }

      }

      if (!xmlConfiguation.HasSpatialReferenceUnit)
        xmlConfiguation.MapSpatialReferenceUnits = lastUnit;

      if (ParcelMap.Extent == null)
        ParcelMap.Extent = new ESRI.ArcGIS.Client.Geometry.Envelope();

      if (xmlConfiguation.IsExtentSet())
      {
        ParcelMap.Extent.XMin = xmlConfiguation.XMin;
        ParcelMap.Extent.XMax = xmlConfiguation.XMax;
        ParcelMap.Extent.YMin = xmlConfiguation.YMin;
        ParcelMap.Extent.YMax = xmlConfiguation.YMax;
      }
      else
      {
        // Map will not zoom to, etc with out some value set.
        // Ideally we would like to set the extent to the full extent
        // of the first layer, but since they layer has hot been drawn yet
        // null is returned.
        ParcelMap.Extent.XMin = 100;
        ParcelMap.Extent.XMax = 100;
        ParcelMap.Extent.YMin = 100;
        ParcelMap.Extent.YMax = 100;
      }

      // if zero, the first inserted layer is used
      if ((xmlConfiguation.SpatialReferenceWKT != null) && (xmlConfiguation.SpatialReferenceWKT != ""))
      {
        if (ParcelMap.Extent.SpatialReference == null)
          ParcelMap.Extent.SpatialReference = new ESRI.ArcGIS.Client.Geometry.SpatialReference();
        ParcelMap.Extent.SpatialReference.WKT = xmlConfiguation.SpatialReferenceWKT;
      }
      else if (xmlConfiguation.SpatialReferenceWKID != 0)
      {
        if (ParcelMap.Extent.SpatialReference == null)
          ParcelMap.Extent.SpatialReference = new ESRI.ArcGIS.Client.Geometry.SpatialReference();
        ParcelMap.Extent.SpatialReference.WKID = xmlConfiguation.SpatialReferenceWKID;
      }

      ParcelData parcelData = ParcelGridContainer.DataContext as ParcelData;
      parcelData.Configuration = xmlConfiguation;

      QueryLabel.Text = xmlConfiguation.QueryLabel;
    }

    private void Layer_InitializationFailed(object sender, System.EventArgs e)
    {
      Layer layer = sender as Layer;
      if (layer.InitializationFailure != null)
        MessageBox.Show(layer.ID + ":" + layer.InitializationFailure.ToString());
    }

    IdentifyWindow _identifyDialog = null;
    private void QueryPoint_MouseClick(object sender, ESRI.ArcGIS.Client.Map.MouseEventArgs e)
    {
      if (DPE_ParcelEntry.IsExpanded || (!PDE_Tools.IsExpanded && !PDE_Find.IsExpanded && !PDE_Share.IsExpanded))
        ParcelTool(e.MapPoint);
      else if (PDE_Tools.IsExpanded)
      {
        if (_originPoint == null)
          ParcelTool(e.MapPoint);

        // Sometimes we don't get the mouse up event? Ensure its released now.
        _srPoint = null;
      }
      else if (PDE_Find.IsExpanded)
      {
        bool create = _identifyDialog == null || !_identifyDialog.IsLoaded;
        if (create)
          _identifyDialog = new IdentifyWindow();
        double dialogWidth = _identifyDialog.Width;    // If dialog is new, capture size
        double dialogHeight = _identifyDialog.Height;  // before its shown.
        if (create)
        {
          _identifyDialog.Owner = this;
          _identifyDialog.Show();
        }
        _identifyDialog.Visibility = System.Windows.Visibility.Hidden;
        _identifyDialog.IdentifyPoint(ParcelMap, ref _xmlConfiguation, e.MapPoint);

        int offset = 10; // Hard coded offset so we don't position the window exactly where the cursor is
        double top = e.ScreenPoint.Y + this.Top +
                      SystemParameters.ResizeFrameVerticalBorderWidth +
                      SystemParameters.CaptionHeight + offset;
        double left = e.ScreenPoint.X + this.Left +
                      SystemParameters.ResizeFrameVerticalBorderWidth + offset;

        // Keep the window in the virtual screen bounds
        double screenWidth = System.Windows.SystemParameters.VirtualScreenWidth;
        double screenHeight = System.Windows.SystemParameters.VirtualScreenHeight;
        if (left + dialogWidth > screenWidth)
          left = screenWidth - dialogWidth;
        if (top + dialogHeight > screenHeight)
          top = screenHeight - dialogHeight;

        _identifyDialog.Top = top;
        _identifyDialog.Left = left;
      }
    }

    private void GeometryService_Failed(object sender, TaskFailedEventArgs args)
    {
      MessageBox.Show((string)Application.Current.FindResource("strGeometryServiceFailed") + ": " + args.Error);
      CancelScaleRotate();
    }

    private void QueryTask_Failed(object sender, TaskFailedEventArgs args)
    {
      MessageBox.Show((string)Application.Current.FindResource("strQueryServiceSupport") + "\n\n" + args.Error, (string)Application.Current.FindResource("strQueryServiceFailed"));
      CancelScaleRotate();
    }

    private void ParcelMap_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
      ScaleRotate(e.GetPosition(this));
    }

    private void ParcelMap_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
      CancelScaleRotate();

      if (IsParcelEntryMode())
        ParcelMap_KeyUp(sender, null);
    }

    private void RotationScale_Click(object sender, RoutedEventArgs e)
    {
      CancelScaleRotate();
    }

    private void Information_MouseDown(object sender, MouseButtonEventArgs e)
    {
      System.Diagnostics.Process.Start(@"http://esriurl.com/DeedDrafter");
    }
  }
}

