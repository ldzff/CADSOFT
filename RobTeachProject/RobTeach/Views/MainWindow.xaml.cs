using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
// Explicitly using System.Windows.Shapes.Shape to avoid ambiguity
// using System.Windows.Shapes; // This line can be removed if all Shape usages are qualified
using RobTeach.Views; // Added for DirectionIndicator
using Microsoft.Win32;
using RobTeach.Services;
using RobTeach.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using IxMilia.Dxf; // Required for DxfFile
using IxMilia.Dxf.Entities;
// using System.Diagnostics; // No longer needed after removing Trace calls
// using netDxf.Header; // No longer needed with IxMilia.Dxf
using System.IO;
// using System.Windows.Threading; // Was for optional Dispatcher.Invoke, not currently used.
// using System.Text.RegularExpressions; // Was for optional IP validation, not currently used.

namespace RobTeach.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml. This is the main window of the RobTeach application,
    /// handling UI events, displaying CAD data, managing configurations, and initiating Modbus communication.
    /// </summary>
    public partial class MainWindow : Window
    {
        // Services used by the MainWindow
        private readonly CadService _cadService = new CadService();
        private readonly ConfigurationService _configService = new ConfigurationService();
        private readonly ModbusService _modbusService = new ModbusService();

        // Current state variables
        private DxfFile? _currentDxfDocument; // Holds the currently loaded DXF document object.
        private string? _currentDxfFilePath;      // Path to the currently loaded DXF file.
        private string? _currentLoadedConfigPath; // Path to the last successfully loaded configuration file.
        private Models.Configuration _currentConfiguration; // The active configuration, either loaded or built from selections.

        // Collections for managing DXF entities and their WPF shape representations
        private readonly List<DxfEntity> _selectedDxfEntities = new List<DxfEntity>(); // Stores original DXF entities selected by the user.
        // Qualified System.Windows.Shapes.Shape for dictionary key
        private readonly Dictionary<System.Windows.Shapes.Shape, DxfEntity> _wpfShapeToDxfEntityMap = new Dictionary<System.Windows.Shapes.Shape, DxfEntity>(); // Changed to DxfEntity
        private readonly Dictionary<string, DxfEntity> _dxfEntityHandleMap = new Dictionary<string, DxfEntity>(); // Maps DXF entity handles to entities for quick lookup when loading configs.
        private readonly List<System.Windows.Shapes.Polyline> _trajectoryPreviewPolylines = new List<System.Windows.Shapes.Polyline>(); // Keeps track of trajectory preview polylines for easy removal.
        private DirectionIndicator _directionIndicator; // Field for the direction indicator arrow

        // Fields for CAD Canvas Zoom/Pan functionality
        private ScaleTransform _scaleTransform;         // Handles scaling (zoom) of the canvas content.
        private TranslateTransform _translateTransform; // Handles translation (pan) of the canvas content.
        private TransformGroup _transformGroup;         // Combines scale and translate transforms.
        private System.Windows.Point _panStartPoint;    // Qualified: Stores the starting point of a mouse pan operation.
        private bool _isPanning;                        // Flag indicating if a pan operation is currently in progress.
        private Rect _dxfBoundingBox = Rect.Empty;      // Stores the calculated bounding box of the entire loaded DXF document.

        // Styling constants for visual feedback
        private static readonly Brush DefaultStrokeBrush = Brushes.DarkSlateGray; // Default color for CAD shapes.
        private static readonly Brush SelectedStrokeBrush = Brushes.DodgerBlue;   // Color for selected CAD shapes.
        private const double DefaultStrokeThickness = 2;                          // Default stroke thickness.
        private const double SelectedStrokeThickness = 3.5;                       // Thickness for selected shapes and trajectories.
        private const string TrajectoryPreviewTag = "TrajectoryPreview";          // Tag for identifying trajectory polylines on canvas (not actively used for removal yet).
        private const double TrajectoryPointResolutionAngle = 15.0; // Default resolution for discretizing arcs/circles.


        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// Sets up default values, initializes transformation objects for the canvas,
        /// and attaches necessary mouse event handlers for canvas interaction.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            if (CadCanvas.Background == null) CadCanvas.Background = Brushes.LightGray; // Ensure canvas has a background for hit testing.

            // Initialize product name with a timestamp to ensure uniqueness for new configurations.
            ProductNameTextBox.Text = $"Product_{DateTime.Now:yyyyMMddHHmmss}";
            _currentConfiguration = new Models.Configuration();
            _currentConfiguration.ProductName = ProductNameTextBox.Text;

            // Setup transformations for the CAD canvas
            _scaleTransform = new ScaleTransform(1, 1);
            _translateTransform = new TranslateTransform(0, 0);
            _transformGroup = new TransformGroup();
            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);
            CadCanvas.RenderTransform = _transformGroup;

            // Attach mouse event handlers for canvas zoom and pan
            CadCanvas.MouseWheel += CadCanvas_MouseWheel;
            CadCanvas.MouseDown += CadCanvas_MouseDown; // For initiating pan
            CadCanvas.MouseMove += CadCanvas_MouseMove; // For active panning
            CadCanvas.MouseUp += CadCanvas_MouseUp;     // For ending pan

            // Attach event handlers for nozzle checkboxes
            // UpperNozzleOnCheckBox.Checked += UpperNozzleOnCheckBox_Changed; // Removed
            // UpperNozzleOnCheckBox.Unchecked += UpperNozzleOnCheckBox_Changed; // Removed
            // LowerNozzleOnCheckBox.Checked += LowerNozzleOnCheckBox_Changed; // Removed
            // LowerNozzleOnCheckBox.Unchecked += LowerNozzleOnCheckBox_Changed; // Removed

            // Set initial state for dependent checkboxes
            // UpperNozzleOnCheckBox_Changed(null, null); // Removed
            // LowerNozzleOnCheckBox_Changed(null, null); // Removed

            // Initialize Spray Pass Management
            _selectedDxfEntities.Clear();
            _wpfShapeToDxfEntityMap.Clear(); // Assuming this map is for temporary DXF display, not persistent selection state

            if (_currentConfiguration.SprayPasses == null || !_currentConfiguration.SprayPasses.Any())
            {
                _currentConfiguration.SprayPasses = new List<SprayPass> { new SprayPass { PassName = "Default Pass 1" } };
                _currentConfiguration.CurrentPassIndex = 0;
            }
            else if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count)
            {
                _currentConfiguration.CurrentPassIndex = 0;
            }

            SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
            if (_currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < SprayPassesListBox.Items.Count)
            {
                SprayPassesListBox.SelectedIndex = _currentConfiguration.CurrentPassIndex;
            }

            // Attach new event handlers
            AddPassButton.Click += AddPassButton_Click;
            RemovePassButton.Click += RemovePassButton_Click;
            RenamePassButton.Click += RenamePassButton_Click;
            SprayPassesListBox.SelectionChanged += SprayPassesListBox_SelectionChanged;

            CurrentPassTrajectoriesListBox.SelectionChanged += CurrentPassTrajectoriesListBox_SelectionChanged;
            MoveTrajectoryUpButton.Click += MoveTrajectoryUpButton_Click;
            MoveTrajectoryDownButton.Click += MoveTrajectoryDownButton_Click;

            // Event handlers for the new six checkboxes
            TrajectoryUpperNozzleEnabledCheckBox.Checked += TrajectoryUpperNozzleEnabledCheckBox_Changed;
            TrajectoryUpperNozzleEnabledCheckBox.Unchecked += TrajectoryUpperNozzleEnabledCheckBox_Changed;
            TrajectoryUpperNozzleGasOnCheckBox.Checked += TrajectoryUpperNozzleGasOnCheckBox_Changed;
            TrajectoryUpperNozzleGasOnCheckBox.Unchecked += TrajectoryUpperNozzleGasOnCheckBox_Changed;
            TrajectoryUpperNozzleLiquidOnCheckBox.Checked += TrajectoryUpperNozzleLiquidOnCheckBox_Changed;
            TrajectoryUpperNozzleLiquidOnCheckBox.Unchecked += TrajectoryUpperNozzleLiquidOnCheckBox_Changed;

            TrajectoryLowerNozzleEnabledCheckBox.Checked += TrajectoryLowerNozzleEnabledCheckBox_Changed;
            TrajectoryLowerNozzleEnabledCheckBox.Unchecked += TrajectoryLowerNozzleEnabledCheckBox_Changed;
            TrajectoryLowerNozzleGasOnCheckBox.Checked += TrajectoryLowerNozzleGasOnCheckBox_Changed;
            TrajectoryLowerNozzleGasOnCheckBox.Unchecked += TrajectoryLowerNozzleGasOnCheckBox_Changed;
            TrajectoryLowerNozzleLiquidOnCheckBox.Checked += TrajectoryLowerNozzleLiquidOnCheckBox_Changed;
            TrajectoryLowerNozzleLiquidOnCheckBox.Unchecked += TrajectoryLowerNozzleLiquidOnCheckBox_Changed;

            // Event handler for TrajectoryIsReversedCheckBox
            TrajectoryIsReversedCheckBox.Checked += TrajectoryIsReversedCheckBox_Changed;
            TrajectoryIsReversedCheckBox.Unchecked += TrajectoryIsReversedCheckBox_Changed;

            RefreshCurrentPassTrajectoriesListBox();
            UpdateSelectedTrajectoryDetailUI(); // Initial call (renamed)
            RefreshCadCanvasHighlights(); // Initial call for canvas highlights
        }

        // Removed UpperNozzleOnCheckBox_Changed and LowerNozzleOnCheckBox_Changed

        private void RefreshCadCanvasHighlights()
        {
            if (_currentConfiguration == null || CadCanvas == null) return; // Basic safety check

            // Determine entities in the current pass
            HashSet<DxfEntity> entitiesInCurrentPass = new HashSet<DxfEntity>();
            if (_currentConfiguration.CurrentPassIndex >= 0 &&
                _currentConfiguration.CurrentPassIndex < _currentConfiguration.SprayPasses.Count)
            {
                var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                if (currentPass != null && currentPass.Trajectories != null)
                {
                    foreach (var trajectory in currentPass.Trajectories)
                    {
                        if (trajectory.OriginalDxfEntity != null) // Assuming Trajectory stores the original DxfEntity
                        {
                            entitiesInCurrentPass.Add(trajectory.OriginalDxfEntity);
                        }
                    }
                }
            }

            // Update all shapes on canvas
            foreach (var wpfShape in _wpfShapeToDxfEntityMap.Keys)
            {
                if (_wpfShapeToDxfEntityMap.TryGetValue(wpfShape, out DxfEntity associatedEntity))
                {
                    if (entitiesInCurrentPass.Contains(associatedEntity))
                    {
                        wpfShape.Stroke = SelectedStrokeBrush;
                        wpfShape.StrokeThickness = SelectedStrokeThickness;
                    }
                    else
                    {
                        wpfShape.Stroke = DefaultStrokeBrush;
                        wpfShape.StrokeThickness = DefaultStrokeThickness;
                    }
                }
                else // Should not happen if map is correct
                {
                    wpfShape.Stroke = DefaultStrokeBrush;
                    wpfShape.StrokeThickness = DefaultStrokeThickness;
                }
            }

            // Also, ensure that trajectory preview polylines are handled if they are separate
            // For now, this method focuses on the shapes mapped from _wpfShapeToDxfEntityMap
        }


        private void AddPassButton_Click(object sender, RoutedEventArgs e)
        {
            int passCount = _currentConfiguration.SprayPasses.Count;
            var newPass = new SprayPass { PassName = $"Pass {passCount + 1}" };
            _currentConfiguration.SprayPasses.Add(newPass);

            // Refresh ListBox - simple way for now
            SprayPassesListBox.ItemsSource = null;
            SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
            SprayPassesListBox.SelectedItem = newPass;
            UpdateDirectionIndicator(); // New pass selected, current trajectory selection changes
        }

        private void RemovePassButton_Click(object sender, RoutedEventArgs e)
        {
            if (SprayPassesListBox.SelectedItem is SprayPass selectedPass)
            {
                if (_currentConfiguration.SprayPasses.Count <= 1)
                {
                    MessageBox.Show("Cannot remove the last spray pass.", "Action Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _currentConfiguration.SprayPasses.Remove(selectedPass);

                // Refresh ListBox and select a new item
                SprayPassesListBox.ItemsSource = null;
                SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
                if (_currentConfiguration.SprayPasses.Any())
                {
                    _currentConfiguration.CurrentPassIndex = 0;
                    SprayPassesListBox.SelectedIndex = 0;
                }
                else
                {
                    _currentConfiguration.CurrentPassIndex = -1;
                    // Potentially add a new default pass here if needed
                }
                RefreshCurrentPassTrajectoriesListBox(); // Update trajectory list for new selected pass
                UpdateDirectionIndicator(); // Pass removed, current trajectory selection changes
            }
        }

        private void RenamePassButton_Click(object sender, RoutedEventArgs e)
        {
            if (SprayPassesListBox.SelectedItem is SprayPass selectedPass)
            {
                // Simple rename for now, no input dialog
                selectedPass.PassName += "_Renamed";

                // Refresh ListBox
                SprayPassesListBox.ItemsSource = null;
                SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
                SprayPassesListBox.SelectedItem = selectedPass;
            }
        }

        private void SprayPassesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SprayPassesListBox.SelectedIndex >= 0)
            {
                _currentConfiguration.CurrentPassIndex = SprayPassesListBox.SelectedIndex;
            }
            else if (!_currentConfiguration.SprayPasses.Any()) // All passes removed
            {
                 _currentConfiguration.CurrentPassIndex = -1;
            }
            // If selection cleared due to item removal, index might be -1 but a pass might still be selected by default.
            // The RemovePassButton_Click should handle setting a valid CurrentPassIndex.

            RefreshCurrentPassTrajectoriesListBox();
            UpdateSelectedTrajectoryDetailUI(); // Renamed
            RefreshCadCanvasHighlights();
            UpdateDirectionIndicator(); // Spray pass selection changed
        }

        private void RefreshCurrentPassTrajectoriesListBox()
        {
            CurrentPassTrajectoriesListBox.ItemsSource = null; // Clear existing items/binding
            if (_currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < _currentConfiguration.SprayPasses.Count)
            {
                var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                // Assuming Trajectory.ToString() is overridden for display or DisplayMemberPath is set in XAML if needed
            }
            // else CurrentPassTrajectoriesListBox remains empty
            UpdateSelectedTrajectoryDetailUI(); // Renamed: Update nozzle UI as selected trajectory might change
        }

        private void CurrentPassTrajectoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedTrajectoryDetailUI(); // Renamed
            UpdateDirectionIndicator(); // Add call to update direction indicator
        }

        private void UpdateDirectionIndicator()
        {
            const double fixedArrowLineLength = 15.0; // Fixed visual length for the arrow's line segment

            // Initial Setup
            if (_directionIndicator == null)
            {
                _directionIndicator = new DirectionIndicator
                {
                    // Set appearance properties that don't change per trajectory
                    Color = SelectedStrokeBrush, // Changed to use the existing field for selection color
                    ArrowheadSize = 8,           // Remains 8
                    StrokeThickness = 1.5        // Remains 1.5
                };
            }

            // Always remove if present, to handle deselection or invalid states correctly
            if (CadCanvas.Children.Contains(_directionIndicator))
            {
                CadCanvas.Children.Remove(_directionIndicator);
            }

            // Get Selected Trajectory
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                if (selectedTrajectory.Points == null || !selectedTrajectory.Points.Any())
                {
                    return;
                }

                List<System.Windows.Point> points = selectedTrajectory.Points;
                Point calculatedArrowStartPoint = new Point(); // Use local variables for calculation
                Point calculatedArrowEndPoint = new Point();   // Use local variables for calculation
                Point primitiveCenter = new Point();
                bool addIndicator = false;

                // Calculate primitiveCenter first
                switch (selectedTrajectory.PrimitiveType)
                {
                    case "Line":
                        if (points.Count >= 2)
                        {
                            Point lineStartWpf = points[0];
                            Point lineEndWpf = points[points.Count - 1];
                            primitiveCenter = new Point((lineStartWpf.X + lineEndWpf.X) / 2, (lineStartWpf.Y + lineEndWpf.Y) / 2);
                        }
                        else
                        {
                            // addIndicator remains false, so arrow won't be drawn if this path is taken and not overridden
                        }
                        break;
                    case "Arc":
                        primitiveCenter = new Point(selectedTrajectory.ArcCenter.X, selectedTrajectory.ArcCenter.Y);
                        break;
                    case "Circle":
                        primitiveCenter = new Point(selectedTrajectory.CircleCenter.X, selectedTrajectory.CircleCenter.Y);
                        break;
                    default:
                        // addIndicator remains false
                        break;
                }

                // This switch calculates the actual arrow points using primitiveCenter
                // addIndicator is determined here based on successful calculation for arrow points
                switch (selectedTrajectory.PrimitiveType)
                {
                    case "Line":
                        if (points.Count >= 2)
                        {
                            Point visualStartPoint = points[0]; // Already respects IsReversed
                            Point visualEndPoint = points[points.Count - 1]; // Already respects IsReversed
                            Vector direction = visualEndPoint - visualStartPoint;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                calculatedArrowStartPoint = primitiveCenter;
                                calculatedArrowEndPoint = primitiveCenter + direction * fixedArrowLineLength;
                                addIndicator = true;
                            } else { addIndicator = false; }
                        }
                        else { addIndicator = false; }
                        break;
                    case "Arc":
                        if (points.Count >= 2)
                        {
                            Point p0 = points[0]; // Start of the visual arc segment
                            Point p1 = points[1]; // Next point on the visual arc segment
                            Vector direction = p1 - p0;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                calculatedArrowStartPoint = primitiveCenter;
                                calculatedArrowEndPoint = primitiveCenter + direction * fixedArrowLineLength;
                                addIndicator = true;
                            } else { addIndicator = false; }
                        }
                        else { addIndicator = false; }
                        break;
                    case "Circle":
                        if (points.Count >= 2)
                        {
                            Point p0 = points[0]; // Start of the visual circle segment
                            Point p1 = points[1]; // Next point on the visual circle segment
                            Vector direction = p1 - p0;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                calculatedArrowStartPoint = primitiveCenter;
                                calculatedArrowEndPoint = primitiveCenter + direction * fixedArrowLineLength;
                                addIndicator = true;
                            } else { addIndicator = false; }
                        }
                        else { addIndicator = false; }
                        break;
                    default:
                        addIndicator = false;
                        break;
                }

                bool canAddIndicator = addIndicator && calculatedArrowStartPoint != calculatedArrowEndPoint;

                if (canAddIndicator)
                {
                    _directionIndicator.StartPoint = calculatedArrowStartPoint;
                    _directionIndicator.EndPoint = calculatedArrowEndPoint;
                    System.Windows.Controls.Panel.SetZIndex(_directionIndicator, 99);
                    CadCanvas.Children.Add(_directionIndicator);
                }
            }
        }

        private void MoveTrajectoryUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count) return;
            var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
            var selectedIndex = CurrentPassTrajectoriesListBox.SelectedIndex;

            if (selectedIndex > 0 && currentPass.Trajectories.Count > selectedIndex)
            {
                var itemToMove = currentPass.Trajectories[selectedIndex];
                currentPass.Trajectories.RemoveAt(selectedIndex);
                currentPass.Trajectories.Insert(selectedIndex - 1, itemToMove);

                CurrentPassTrajectoriesListBox.ItemsSource = null;
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                CurrentPassTrajectoriesListBox.SelectedIndex = selectedIndex - 1;
                RefreshCadCanvasHighlights();
                UpdateDirectionIndicator();
            }
        }

        private void MoveTrajectoryDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count) return;
            var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
            var selectedIndex = CurrentPassTrajectoriesListBox.SelectedIndex;

            if (selectedIndex >= 0 && selectedIndex < currentPass.Trajectories.Count - 1)
            {
                var itemToMove = currentPass.Trajectories[selectedIndex];
                currentPass.Trajectories.RemoveAt(selectedIndex);
                currentPass.Trajectories.Insert(selectedIndex + 1, itemToMove);

                CurrentPassTrajectoriesListBox.ItemsSource = null;
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                CurrentPassTrajectoriesListBox.SelectedIndex = selectedIndex + 1;
                RefreshCadCanvasHighlights();
                UpdateDirectionIndicator();
            }
        }

        private void UpdateSelectedTrajectoryDetailUI()
        {
            // Nozzle settings part
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                // Enable all nozzle checkboxes
                TrajectoryUpperNozzleEnabledCheckBox.IsEnabled = true;
                TrajectoryLowerNozzleEnabledCheckBox.IsEnabled = true;
                // Gas/Liquid enabled state will be set by their respective 'Enabled' checkbox change handlers

                // Set IsChecked status for nozzle settings from selected trajectory
                TrajectoryUpperNozzleEnabledCheckBox.IsChecked = selectedTrajectory.UpperNozzleEnabled;
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = selectedTrajectory.UpperNozzleGasOn;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = selectedTrajectory.UpperNozzleLiquidOn;
                TrajectoryLowerNozzleEnabledCheckBox.IsChecked = selectedTrajectory.LowerNozzleEnabled;
                TrajectoryLowerNozzleGasOnCheckBox.IsChecked = selectedTrajectory.LowerNozzleGasOn;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = selectedTrajectory.LowerNozzleLiquidOn;

                // Call handlers to correctly set IsEnabled for Gas/Liquid checkboxes
                TrajectoryUpperNozzleEnabledCheckBox_Changed(null, null);
                TrajectoryLowerNozzleEnabledCheckBox_Changed(null, null);

                // Geometry settings part
                TrajectoryIsReversedCheckBox.IsChecked = selectedTrajectory.IsReversed;

                // Visibility of IsReversed checkbox (only for Line/Arc)
                if (selectedTrajectory.PrimitiveType == "Line" || selectedTrajectory.PrimitiveType == "Arc")
                {
                    TrajectoryIsReversedCheckBox.Visibility = Visibility.Visible;
                }
                else
                {
                    TrajectoryIsReversedCheckBox.Visibility = Visibility.Collapsed;
                }

                // Visibility and content of Z-coordinate panels
                LineHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Line" ? Visibility.Visible : Visibility.Collapsed;
                ArcHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Arc" ? Visibility.Visible : Visibility.Collapsed;
                CircleHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Circle" ? Visibility.Visible : Visibility.Collapsed;

                if (selectedTrajectory.PrimitiveType == "Line")
                {
                    LineStartZTextBox.Text = selectedTrajectory.LineStartPoint.Z.ToString("F3");
                    LineEndZTextBox.Text = selectedTrajectory.LineEndPoint.Z.ToString("F3");
                }
                else if (selectedTrajectory.PrimitiveType == "Arc")
                {
                    ArcCenterZTextBox.Text = selectedTrajectory.ArcCenter.Z.ToString("F3");
                }
                else if (selectedTrajectory.PrimitiveType == "Circle")
                {
                    CircleCenterZTextBox.Text = selectedTrajectory.CircleCenter.Z.ToString("F3");
                }
            }
            else
            {
                // Disable and uncheck all nozzle checkboxes
                TrajectoryUpperNozzleEnabledCheckBox.IsEnabled = false;
                TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleEnabledCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsEnabled = false;

                TrajectoryUpperNozzleEnabledCheckBox.IsChecked = false;
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = false;
                TrajectoryLowerNozzleEnabledCheckBox.IsChecked = false;
                TrajectoryLowerNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = false;

                // Collapse geometry UI elements
                TrajectoryIsReversedCheckBox.Visibility = Visibility.Collapsed;
                TrajectoryIsReversedCheckBox.IsChecked = false;
                LineHeightControlsPanel.Visibility = Visibility.Collapsed;
                ArcHeightControlsPanel.Visibility = Visibility.Collapsed;
                CircleHeightControlsPanel.Visibility = Visibility.Collapsed;
                LineStartZTextBox.Text = string.Empty;
                LineEndZTextBox.Text = string.Empty;
                ArcCenterZTextBox.Text = string.Empty;
                CircleCenterZTextBox.Text = string.Empty;
            }
        }

        private void TrajectoryIsReversedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.IsReversed = TrajectoryIsReversedCheckBox.IsChecked ?? false;
                PopulateTrajectoryPoints(selectedTrajectory);
                CurrentPassTrajectoriesListBox.Items.Refresh();
                RefreshCadCanvasHighlights();
                UpdateDirectionIndicator();
            }
        }

        private void PopulateTrajectoryPoints(Trajectory trajectory)
        {
            if (trajectory == null) return;

            trajectory.Points.Clear();

            switch (trajectory.PrimitiveType)
            {
                case "Line":
                    trajectory.Points.AddRange(_cadService.ConvertLineTrajectoryToPoints(trajectory));
                    break;
                case "Arc":
                    trajectory.Points.AddRange(_cadService.ConvertArcTrajectoryToPoints(trajectory, TrajectoryPointResolutionAngle));
                    break;
                case "Circle":
                    trajectory.Points.AddRange(_cadService.ConvertCircleTrajectoryToPoints(trajectory, TrajectoryPointResolutionAngle));
                    break;
                default:
                    // For other types or if PrimitiveType is not set, Points will remain empty or could be populated from OriginalDxfEntity if needed
                    // For now, we rely on the specific Convert<Primitive>TrajectoryToPoints methods.
                    // If OriginalDxfEntity exists and is of a known DxfEntityType, could fall back to old methods:
                    if (trajectory.OriginalDxfEntity != null) {
                        switch (trajectory.OriginalDxfEntity) {
                            case DxfLine line: trajectory.Points.AddRange(_cadService.ConvertLineToPoints(line)); break;
                            case DxfArc arc: trajectory.Points.AddRange(_cadService.ConvertArcToPoints(arc, TrajectoryPointResolutionAngle)); break;
                            case DxfCircle circle: trajectory.Points.AddRange(_cadService.ConvertCircleToPoints(circle, TrajectoryPointResolutionAngle)); break;
                        }
                    }
                    break;
            }
        }

        private void TrajectoryUpperNozzleEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isEnabled = TrajectoryUpperNozzleEnabledCheckBox.IsChecked ?? false;
            TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = isEnabled;
            TrajectoryUpperNozzleLiquidOnCheckBox.IsEnabled = isEnabled;

            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.UpperNozzleEnabled = isEnabled;
                if (!isEnabled)
                {
                    TrajectoryUpperNozzleGasOnCheckBox.IsChecked = false;
                    TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = false;
                    selectedTrajectory.UpperNozzleGasOn = false;
                    selectedTrajectory.UpperNozzleLiquidOn = false;
                }
            }
            else if (!isEnabled)
            {
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = false;
            }
        }

        private void TrajectoryUpperNozzleGasOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.UpperNozzleGasOn = TrajectoryUpperNozzleGasOnCheckBox.IsChecked ?? false;
            }
        }

        private void TrajectoryUpperNozzleLiquidOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.UpperNozzleLiquidOn = TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked ?? false;
            }
        }

        private void TrajectoryLowerNozzleEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isEnabled = TrajectoryLowerNozzleEnabledCheckBox.IsChecked ?? false;
            TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = isEnabled;
            TrajectoryLowerNozzleLiquidOnCheckBox.IsEnabled = isEnabled;

            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.LowerNozzleEnabled = isEnabled;
                if (!isEnabled)
                {
                    TrajectoryLowerNozzleGasOnCheckBox.IsChecked = false;
                    TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = false;
                    selectedTrajectory.LowerNozzleGasOn = false;
                    selectedTrajectory.LowerNozzleLiquidOn = false;
                }
            }
            else if (!isEnabled)
            {
                 TrajectoryLowerNozzleGasOnCheckBox.IsChecked = false;
                 TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = false;
            }
        }

        private void TrajectoryLowerNozzleGasOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.LowerNozzleGasOn = TrajectoryLowerNozzleGasOnCheckBox.IsChecked ?? false;
            }
        }

        private void TrajectoryLowerNozzleLiquidOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.LowerNozzleLiquidOn = TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked ?? false;
            }
        }

        /// <summary>
        /// Handles the Closing event of the window. Ensures Modbus connection is disconnected.
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _modbusService.Disconnect();
        }

        /// <summary>
        /// Handles the Click event of the "Load DXF" button.
        /// Prompts the user to select a DXF file, loads it using <see cref="CadService"/>,
        /// processes its entities for display, and fits the view to the loaded drawing.
        /// </summary>
        private void LoadDxfButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Filter = "DXF files (*.dxf)|*.dxf|All files (*.*)|*.*", Title = "Load DXF File" };
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/";
            openFileDialog.InitialDirectory = initialDir;

            try {
                if (openFileDialog.ShowDialog() == true) {
                    _currentDxfFilePath = openFileDialog.FileName;
                    StatusTextBlock.Text = $"Loading DXF: {Path.GetFileName(_currentDxfFilePath)}...";

                    CadCanvas.Children.Clear();
                    _wpfShapeToDxfEntityMap.Clear(); _selectedDxfEntities.Clear();
                    _trajectoryPreviewPolylines.Clear(); _dxfEntityHandleMap.Clear();
                    _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                    _currentLoadedConfigPath = null;
                    _currentDxfDocument = null;
                    _dxfBoundingBox = Rect.Empty;
                    UpdateTrajectoryPreview();
                    UpdateDirectionIndicator();

                    _currentDxfDocument = _cadService.LoadDxf(_currentDxfFilePath);

                    if (_currentDxfDocument == null) {
                        StatusTextBlock.Text = "Failed to load DXF document (null document returned).";
                        MessageBox.Show("The DXF document could not be loaded. The file might be empty or an unknown error occurred.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Note: IxMilia.Dxf doesn't expose Handle property directly
                    // We'll skip handle mapping for now

                    List<System.Windows.Shapes.Shape> wpfShapes = _cadService.GetWpfShapesFromDxf(_currentDxfDocument);
                    int shapeIndex = 0;

                    foreach(var entity in _currentDxfDocument.Entities)
                    {
                        if (shapeIndex < wpfShapes.Count && wpfShapes[shapeIndex] != null) {
                            var wpfShape = wpfShapes[shapeIndex];
                            wpfShape.Stroke = DefaultStrokeBrush;
                            wpfShape.StrokeThickness = DefaultStrokeThickness;
                            wpfShape.MouseLeftButtonDown += OnCadEntityClicked;
                            _wpfShapeToDxfEntityMap[wpfShape] = entity;
                            CadCanvas.Children.Add(wpfShape);
                            shapeIndex++;
                        }
                    }

                    _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument);
                    PerformFitToView();
                    StatusTextBlock.Text = $"Loaded: {Path.GetFileName(_currentDxfFilePath)}. Click shapes to select.";
                    UpdateDirectionIndicator();
                } else { StatusTextBlock.Text = "DXF loading cancelled."; }
            }
            catch (FileNotFoundException fnfEx) {
                StatusTextBlock.Text = "Error: DXF file not found.";
                MessageBox.Show($"DXF file not found:\n{fnfEx.Message}", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
            }
            // Removed specific catch for netDxf.DxfVersionNotSupportedException. General Exception will handle DXF-specific errors.
            catch (Exception ex) {
                StatusTextBlock.Text = "Error loading or processing DXF file.";
                MessageBox.Show($"An error occurred while loading or processing the DXF file:\n{ex.Message}\n\nEnsure the file is a valid DXF format.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
                CadCanvas.Children.Clear();
                _selectedDxfEntities.Clear(); _wpfShapeToDxfEntityMap.Clear(); _dxfEntityHandleMap.Clear();
                _trajectoryPreviewPolylines?.Clear();
                _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                UpdateTrajectoryPreview();
                UpdateDirectionIndicator();
            }
        }

        /// <summary>
        /// Handles the click event on a CAD entity shape, toggling its selection state.
        /// </summary>
        private void OnCadEntityClicked(object sender, MouseButtonEventArgs e)
        {
            Trajectory trajectoryToSelect = null;

            // Detailed check for the main condition
            bool isShape = sender is System.Windows.Shapes.Shape;
                    for (int i = 0; i < selectedTrajectory.Points.Count; i++)
                    {
                        Trace.WriteLine($"  Point[{i}]: ({selectedTrajectory.Points[i].X:F3}, {selectedTrajectory.Points[i].Y:F3})");
                    }
                }
                else
                {
                    Trace.WriteLine($"UpdateDirectionIndicator: Trajectory '{selectedTrajectory.PrimitiveType}' selected but Points list is null or empty.");
                    Trace.Flush(); // Added flush before early return
                    return; // Cannot proceed without points
                }

                List<System.Windows.Point> points = selectedTrajectory.Points; // Keep using points from selectedTrajectory for arrow calculation
                Point arrowStartPoint = new Point();
                Point arrowEndPoint = new Point();
                // addIndicator is reset here for arrow calculation logic specifically
                addIndicator = false;

                // This switch calculates the actual arrow points
                switch (selectedTrajectory.PrimitiveType)
                {
                    case "Line":
                        if (points.Count >= 2)
                        {
                            Point actualEndPoint = points[points.Count - 1];
                            Point actualStartPoint = points[0]; // Use the actual start of the line for overall direction
                            Vector direction = actualEndPoint - actualStartPoint;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                arrowStartPoint = actualEndPoint - direction * fixedArrowLineLength;
                                arrowEndPoint = actualEndPoint;
                                addIndicator = true;
                                Trace.WriteLine($"  Line Arrow Calc: ArrowStart=({arrowStartPoint.X:F3}, {arrowStartPoint.Y:F3}), ArrowEnd=({arrowEndPoint.X:F3}, {arrowEndPoint.Y:F3})");
                            }
                        }
                        break;
                    case "Arc":
                        if (points.Count >= 2)
                        {
                            Point actualEndPoint = points[points.Count - 1];
                            Point pointBeforeEnd = points[points.Count - 2];
                            Vector direction = actualEndPoint - pointBeforeEnd;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                arrowStartPoint = actualEndPoint - direction * fixedArrowLineLength;
                                arrowEndPoint = actualEndPoint;
                                addIndicator = true;
                                Trace.WriteLine($"  Arc Arrow Calc: ArrowStart=({arrowStartPoint.X:F3}, {arrowStartPoint.Y:F3}), ArrowEnd=({arrowEndPoint.X:F3}, {arrowEndPoint.Y:F3})");
                            }
                        }
                        break;
                    case "Circle": // Indicates initial direction
                        if (points.Count >= 2)
                        {
                            Point p0 = points[0];
                            Point p1 = points[1];
                            Vector direction = p1 - p0;

                            if (direction.Length > 0)
                            {
                                direction.Normalize();
                                arrowStartPoint = p0;
                                arrowEndPoint = p0 + direction * fixedArrowLineLength;
                                addIndicator = true;
                                Trace.WriteLine($"  Circle Arrow Calc: ArrowStart=({arrowStartPoint.X:F3}, {arrowStartPoint.Y:F3}), ArrowEnd=({arrowEndPoint.X:F3}, {arrowEndPoint.Y:F3})");
                            }
                        }
                        break;
                    default:
                        // Unknown primitive type, do not show indicator
                        break;
                }

                bool canAddIndicator = addIndicator && arrowStartPoint != arrowEndPoint; // Check calculated points before assigning to indicator
                Trace.WriteLine($"UpdateDirectionIndicator: Condition to add indicator met: {canAddIndicator}");

                if (canAddIndicator)
                {
                    _directionIndicator.StartPoint = arrowStartPoint;
                    _directionIndicator.EndPoint = arrowEndPoint;
                    Trace.WriteLine($"  Final Arrow Points: Start=({_directionIndicator.StartPoint.X:F3}, {_directionIndicator.StartPoint.Y:F3}), End=({_directionIndicator.EndPoint.X:F3}, {_directionIndicator.EndPoint.Y:F3})");
                    System.Windows.Controls.Panel.SetZIndex(_directionIndicator, 99); // Set a high Z-index
                    CadCanvas.Children.Add(_directionIndicator);
                }
            }
            // If no valid trajectory is selected, it has no points, or type is unhandled, indicator is already removed/not added.
            Trace.Flush(); // Ensure all trace messages are written
        }

        private void MoveTrajectoryUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count) return;
            var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
            var selectedIndex = CurrentPassTrajectoriesListBox.SelectedIndex;

            if (selectedIndex > 0 && currentPass.Trajectories.Count > selectedIndex)
            {
                var itemToMove = currentPass.Trajectories[selectedIndex];
                currentPass.Trajectories.RemoveAt(selectedIndex);
                currentPass.Trajectories.Insert(selectedIndex - 1, itemToMove);

                CurrentPassTrajectoriesListBox.ItemsSource = null; // Refresh
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                CurrentPassTrajectoriesListBox.SelectedIndex = selectedIndex - 1;
                RefreshCadCanvasHighlights(); // Visual update after reorder
                UpdateDirectionIndicator(); // Selection might change or visual needs refresh
            }
        }

        private void MoveTrajectoryDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count) return;
            var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];
            var selectedIndex = CurrentPassTrajectoriesListBox.SelectedIndex;

            if (selectedIndex >= 0 && selectedIndex < currentPass.Trajectories.Count - 1)
            {
                var itemToMove = currentPass.Trajectories[selectedIndex];
                currentPass.Trajectories.RemoveAt(selectedIndex);
                currentPass.Trajectories.Insert(selectedIndex + 1, itemToMove);

                CurrentPassTrajectoriesListBox.ItemsSource = null; // Refresh
                CurrentPassTrajectoriesListBox.ItemsSource = currentPass.Trajectories;
                CurrentPassTrajectoriesListBox.SelectedIndex = selectedIndex + 1;
                RefreshCadCanvasHighlights(); // Visual update after reorder
                UpdateDirectionIndicator(); // Selection might change or visual needs refresh
            }
        }

        private void UpdateSelectedTrajectoryDetailUI() // Renamed method
        {
            // Nozzle settings part
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                // Enable all nozzle checkboxes
                TrajectoryUpperNozzleEnabledCheckBox.IsEnabled = true;
                TrajectoryLowerNozzleEnabledCheckBox.IsEnabled = true;
                // Gas/Liquid enabled state will be set by their respective 'Enabled' checkbox change handlers

                // Set IsChecked status for nozzle settings from selected trajectory
                TrajectoryUpperNozzleEnabledCheckBox.IsChecked = selectedTrajectory.UpperNozzleEnabled;
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = selectedTrajectory.UpperNozzleGasOn;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = selectedTrajectory.UpperNozzleLiquidOn;
                TrajectoryLowerNozzleEnabledCheckBox.IsChecked = selectedTrajectory.LowerNozzleEnabled;
                TrajectoryLowerNozzleGasOnCheckBox.IsChecked = selectedTrajectory.LowerNozzleGasOn;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = selectedTrajectory.LowerNozzleLiquidOn;

                // Call handlers to correctly set IsEnabled for Gas/Liquid checkboxes
                TrajectoryUpperNozzleEnabledCheckBox_Changed(null, null);
                TrajectoryLowerNozzleEnabledCheckBox_Changed(null, null);

                // Geometry settings part
                TrajectoryIsReversedCheckBox.IsChecked = selectedTrajectory.IsReversed;

                // Visibility of IsReversed checkbox (only for Line/Arc)
                if (selectedTrajectory.PrimitiveType == "Line" || selectedTrajectory.PrimitiveType == "Arc")
                {
                    TrajectoryIsReversedCheckBox.Visibility = Visibility.Visible;
                }
                else
                {
                    TrajectoryIsReversedCheckBox.Visibility = Visibility.Collapsed;
                }

                // Visibility and content of Z-coordinate panels
                LineHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Line" ? Visibility.Visible : Visibility.Collapsed;
                ArcHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Arc" ? Visibility.Visible : Visibility.Collapsed;
                CircleHeightControlsPanel.Visibility = selectedTrajectory.PrimitiveType == "Circle" ? Visibility.Visible : Visibility.Collapsed;

                if (selectedTrajectory.PrimitiveType == "Line")
                {
                    LineStartZTextBox.Text = selectedTrajectory.LineStartPoint.Z.ToString("F3");
                    LineEndZTextBox.Text = selectedTrajectory.LineEndPoint.Z.ToString("F3");
                }
                else if (selectedTrajectory.PrimitiveType == "Arc")
                {
                    ArcCenterZTextBox.Text = selectedTrajectory.ArcCenter.Z.ToString("F3");
                }
                else if (selectedTrajectory.PrimitiveType == "Circle")
                {
                    CircleCenterZTextBox.Text = selectedTrajectory.CircleCenter.Z.ToString("F3");
                }
            }
            else // No trajectory selected
            {
                // Disable and uncheck all nozzle checkboxes
                TrajectoryUpperNozzleEnabledCheckBox.IsEnabled = false;
                TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleEnabledCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = false;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsEnabled = false;

                TrajectoryUpperNozzleEnabledCheckBox.IsChecked = false;
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = false;
                TrajectoryLowerNozzleEnabledCheckBox.IsChecked = false;
                TrajectoryLowerNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = false;

                // Collapse geometry UI elements
                TrajectoryIsReversedCheckBox.Visibility = Visibility.Collapsed;
                TrajectoryIsReversedCheckBox.IsChecked = false; // Uncheck
                LineHeightControlsPanel.Visibility = Visibility.Collapsed;
                ArcHeightControlsPanel.Visibility = Visibility.Collapsed;
                CircleHeightControlsPanel.Visibility = Visibility.Collapsed;
                LineStartZTextBox.Text = string.Empty;
                LineEndZTextBox.Text = string.Empty;
                ArcCenterZTextBox.Text = string.Empty;
                CircleCenterZTextBox.Text = string.Empty;
            }
        }

        private void TrajectoryIsReversedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.IsReversed = TrajectoryIsReversedCheckBox.IsChecked ?? false;
                PopulateTrajectoryPoints(selectedTrajectory); // Regenerate points
                CurrentPassTrajectoriesListBox.Items.Refresh(); // Update display if ToString() changed or for other bound properties
                RefreshCadCanvasHighlights(); // May be needed if visual representation on canvas depends on points/direction
                UpdateDirectionIndicator(); // Update arrow when direction changes
            }
        }

        private void PopulateTrajectoryPoints(Trajectory trajectory)
        {
            if (trajectory == null) return;

            trajectory.Points.Clear();

            switch (trajectory.PrimitiveType)
            {
                case "Line":
                    trajectory.Points.AddRange(_cadService.ConvertLineTrajectoryToPoints(trajectory));
                    break;
                case "Arc":
                    trajectory.Points.AddRange(_cadService.ConvertArcTrajectoryToPoints(trajectory, TrajectoryPointResolutionAngle));
                    break;
                case "Circle":
                    trajectory.Points.AddRange(_cadService.ConvertCircleTrajectoryToPoints(trajectory, TrajectoryPointResolutionAngle));
                    break;
                default:
                    // For other types or if PrimitiveType is not set, Points will remain empty or could be populated from OriginalDxfEntity if needed
                    // For now, we rely on the specific Convert<Primitive>TrajectoryToPoints methods.
                    // If OriginalDxfEntity exists and is of a known DxfEntityType, could fall back to old methods:
                    if (trajectory.OriginalDxfEntity != null) {
                        switch (trajectory.OriginalDxfEntity) {
                            case DxfLine line: trajectory.Points.AddRange(_cadService.ConvertLineToPoints(line)); break;
                            case DxfArc arc: trajectory.Points.AddRange(_cadService.ConvertArcToPoints(arc, TrajectoryPointResolutionAngle)); break;
                            case DxfCircle circle: trajectory.Points.AddRange(_cadService.ConvertCircleToPoints(circle, TrajectoryPointResolutionAngle)); break;
                        }
                    }
                    break;
            }
        }

        private void TrajectoryUpperNozzleEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isEnabled = TrajectoryUpperNozzleEnabledCheckBox.IsChecked ?? false;
            TrajectoryUpperNozzleGasOnCheckBox.IsEnabled = isEnabled;
            TrajectoryUpperNozzleLiquidOnCheckBox.IsEnabled = isEnabled;

            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.UpperNozzleEnabled = isEnabled;
                if (!isEnabled)
                {
                    TrajectoryUpperNozzleGasOnCheckBox.IsChecked = false; // Also uncheck
                    TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = false; // Also uncheck
                    selectedTrajectory.UpperNozzleGasOn = false;
                    selectedTrajectory.UpperNozzleLiquidOn = false;
                }
            }
            else if (!isEnabled) // No trajectory selected, ensure UI is consistent
            {
                TrajectoryUpperNozzleGasOnCheckBox.IsChecked = false;
                TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked = false;
            }
        }

        private void TrajectoryUpperNozzleGasOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.UpperNozzleGasOn = TrajectoryUpperNozzleGasOnCheckBox.IsChecked ?? false;
            }
        }

        private void TrajectoryUpperNozzleLiquidOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.UpperNozzleLiquidOn = TrajectoryUpperNozzleLiquidOnCheckBox.IsChecked ?? false;
            }
        }

        private void TrajectoryLowerNozzleEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isEnabled = TrajectoryLowerNozzleEnabledCheckBox.IsChecked ?? false;
            TrajectoryLowerNozzleGasOnCheckBox.IsEnabled = isEnabled;
            TrajectoryLowerNozzleLiquidOnCheckBox.IsEnabled = isEnabled;

            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.LowerNozzleEnabled = isEnabled;
                if (!isEnabled)
                {
                    TrajectoryLowerNozzleGasOnCheckBox.IsChecked = false; // Also uncheck
                    TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = false; // Also uncheck
                    selectedTrajectory.LowerNozzleGasOn = false;
                    selectedTrajectory.LowerNozzleLiquidOn = false;
                }
            }
            else if (!isEnabled) // No trajectory selected, ensure UI is consistent
            {
                 TrajectoryLowerNozzleGasOnCheckBox.IsChecked = false;
                 TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked = false;
            }
        }

        private void TrajectoryLowerNozzleGasOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.LowerNozzleGasOn = TrajectoryLowerNozzleGasOnCheckBox.IsChecked ?? false;
            }
        }

        private void TrajectoryLowerNozzleLiquidOnCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentPassTrajectoriesListBox.SelectedItem is Trajectory selectedTrajectory)
            {
                selectedTrajectory.LowerNozzleLiquidOn = TrajectoryLowerNozzleLiquidOnCheckBox.IsChecked ?? false;
            }
        }

        /// <summary>
        /// Handles the Closing event of the window. Ensures Modbus connection is disconnected.
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _modbusService.Disconnect(); // Clean up Modbus connection.
        }

        /// <summary>
        /// Handles the Click event of the "Load DXF" button.
        /// Prompts the user to select a DXF file, loads it using <see cref="CadService"/>,
        /// processes its entities for display, and fits the view to the loaded drawing.
        /// </summary>
        private void LoadDxfButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Filter = "DXF files (*.dxf)|*.dxf|All files (*.*)|*.*", Title = "Load DXF File" };
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            if (!Directory.Exists(initialDir)) initialDir = "/app/RobTeachProject/RobTeach/";
            openFileDialog.InitialDirectory = initialDir;

            try {
                if (openFileDialog.ShowDialog() == true) {
                    _currentDxfFilePath = openFileDialog.FileName;
                    StatusTextBlock.Text = $"Loading DXF: {Path.GetFileName(_currentDxfFilePath)}...";

                    CadCanvas.Children.Clear();
                    _wpfShapeToDxfEntityMap.Clear(); _selectedDxfEntities.Clear();
                    _trajectoryPreviewPolylines.Clear(); _dxfEntityHandleMap.Clear();
                    _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                    _currentLoadedConfigPath = null;
                    _currentDxfDocument = null;
                    _dxfBoundingBox = Rect.Empty;
                    UpdateTrajectoryPreview();
                    UpdateDirectionIndicator(); // Clear old indicator early

                    _currentDxfDocument = _cadService.LoadDxf(_currentDxfFilePath);

                    if (_currentDxfDocument == null) {
                        StatusTextBlock.Text = "Failed to load DXF document (null document returned).";
                        MessageBox.Show("The DXF document could not be loaded. The file might be empty or an unknown error occurred.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Note: IxMilia.Dxf doesn't expose Handle property directly
                    // We'll skip handle mapping for now

                    List<System.Windows.Shapes.Shape> wpfShapes = _cadService.GetWpfShapesFromDxf(_currentDxfDocument);
                    int shapeIndex = 0;
                    
                    foreach(var entity in _currentDxfDocument.Entities)
                    {
                        if (shapeIndex < wpfShapes.Count && wpfShapes[shapeIndex] != null) {
                            var wpfShape = wpfShapes[shapeIndex];
                            wpfShape.Stroke = DefaultStrokeBrush; 
                            wpfShape.StrokeThickness = DefaultStrokeThickness;
                            wpfShape.MouseLeftButtonDown += OnCadEntityClicked;
                            _wpfShapeToDxfEntityMap[wpfShape] = entity;
                            CadCanvas.Children.Add(wpfShape);
                            shapeIndex++; 
                        }
                    }

                    _dxfBoundingBox = GetDxfBoundingBox(_currentDxfDocument);
                    PerformFitToView();
                    StatusTextBlock.Text = $"Loaded: {Path.GetFileName(_currentDxfFilePath)}. Click shapes to select.";
                    UpdateDirectionIndicator(); // Update after loading and potential default selections
                } else { StatusTextBlock.Text = "DXF loading cancelled."; }
            }
            catch (FileNotFoundException fnfEx) {
                StatusTextBlock.Text = "Error: DXF file not found.";
                MessageBox.Show($"DXF file not found:\n{fnfEx.Message}", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
            }
            // Removed specific catch for netDxf.DxfVersionNotSupportedException. General Exception will handle DXF-specific errors.
            catch (Exception ex) {
                StatusTextBlock.Text = "Error loading or processing DXF file.";
                MessageBox.Show($"An error occurred while loading or processing the DXF file:\n{ex.Message}\n\nEnsure the file is a valid DXF format.", "Error Loading DXF", MessageBoxButton.OK, MessageBoxImage.Error);
                _currentDxfDocument = null;
                CadCanvas.Children.Clear();
                _selectedDxfEntities.Clear(); _wpfShapeToDxfEntityMap.Clear(); _dxfEntityHandleMap.Clear();
                _trajectoryPreviewPolylines?.Clear();
                _currentConfiguration = new Models.Configuration { ProductName = ProductNameTextBox.Text };
                UpdateTrajectoryPreview();
                UpdateDirectionIndicator(); // Clear indicator on error too
            }
        }

        /// <summary>
        /// Handles the click event on a CAD entity shape, toggling its selection state.
        /// </summary>
        private void OnCadEntityClicked(object sender, MouseButtonEventArgs e)
        {
            Trace.WriteLine("++++ OnCadEntityClicked Fired ++++");
            Trace.Flush();
            Trajectory trajectoryToSelect = null;

            // Detailed check for the main condition
            bool isShape = sender is System.Windows.Shapes.Shape;
            bool keyExists = false;
            if (isShape)
            {
                keyExists = _wpfShapeToDxfEntityMap.ContainsKey((System.Windows.Shapes.Shape)sender);
            }

            if (isShape && keyExists)
            {
                System.Windows.Shapes.Shape clickedShape = (System.Windows.Shapes.Shape)sender;
                var dxfEntity = _wpfShapeToDxfEntityMap[clickedShape];
                
                if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count)
                {
                    MessageBox.Show("Please select or create a spray pass first.", "No Active Pass", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var currentPass = _currentConfiguration.SprayPasses[_currentConfiguration.CurrentPassIndex];

                var existingTrajectory = currentPass.Trajectories.FirstOrDefault(t => t.OriginalDxfEntity == dxfEntity);

                if (existingTrajectory != null)
                {
                    currentPass.Trajectories.Remove(existingTrajectory);
                }
                else
                {
                    var newTrajectory = new Trajectory
                    {
                        OriginalDxfEntity = dxfEntity,
                        EntityType = dxfEntity.GetType().Name,
                        IsReversed = false
                    };

                    switch (dxfEntity)
                    {
                        case DxfLine line:
                            newTrajectory.PrimitiveType = "Line";
                            double p1DistSq = line.P1.X * line.P1.X + line.P1.Y * line.P1.Y + line.P1.Z * line.P1.Z;
                            double p2DistSq = line.P2.X * line.P2.X + line.P2.Y * line.P2.Y + line.P2.Z * line.P2.Z;
                            if (p1DistSq <= p2DistSq)
                            {
                                newTrajectory.LineStartPoint = line.P1;
                                newTrajectory.LineEndPoint = line.P2;
                            }
                            else
                            {
                                newTrajectory.LineStartPoint = line.P2;
                                newTrajectory.LineEndPoint = line.P1;
                            }
                            break;
                        case DxfArc arc:
                            newTrajectory.PrimitiveType = "Arc";
                            newTrajectory.ArcCenter = arc.Center;
                            newTrajectory.ArcRadius = arc.Radius;
                            newTrajectory.ArcStartAngle = arc.StartAngle;
                            newTrajectory.ArcEndAngle = arc.EndAngle;
                            newTrajectory.ArcNormal = arc.Normal;
                            break;
                        case DxfCircle circle:
                            newTrajectory.PrimitiveType = "Circle";
                            newTrajectory.CircleCenter = circle.Center;
                            newTrajectory.CircleRadius = circle.Radius;
                            newTrajectory.CircleNormal = circle.Normal;
                            break;
                        default:
                            newTrajectory.PrimitiveType = dxfEntity.GetType().Name;
                            break;
                    }
                    PopulateTrajectoryPoints(newTrajectory);
                    currentPass.Trajectories.Add(newTrajectory);
                    trajectoryToSelect = newTrajectory;
                }

                RefreshCurrentPassTrajectoriesListBox();

                if (trajectoryToSelect != null)
                {
                    CurrentPassTrajectoriesListBox.SelectedItem = trajectoryToSelect;
                }

                RefreshCadCanvasHighlights();
                StatusTextBlock.Text = $"Selected {currentPass.Trajectories.Count} trajectories in {currentPass.PassName}.";

                UpdateDirectionIndicator();
            }
        }

        /// <summary>
        /// Updates the trajectory preview by drawing polylines for selected entities.
        /// </summary>
        private void UpdateTrajectoryPreview()
        {
            // Clear existing trajectory previews
            foreach (var polyline in _trajectoryPreviewPolylines)
            {
                CadCanvas.Children.Remove(polyline);
            }
            _trajectoryPreviewPolylines.Clear();

            // Generate preview for selected entities
            foreach (var entity in _selectedDxfEntities)
            {
                List<System.Windows.Point> points = new List<System.Windows.Point>();
                
                switch (entity)
                {
                    case DxfLine line:
                        points = _cadService.ConvertLineToPoints(line);
                        break;
                    case DxfArc arc:
                        points = _cadService.ConvertArcToPoints(arc, TrajectoryPointResolutionAngle);
                        break;
                    case DxfCircle circle:
                        points = _cadService.ConvertCircleToPoints(circle, TrajectoryPointResolutionAngle);
                        break;
                }

                if (points.Count > 1)
                {
                    var polyline = new System.Windows.Shapes.Polyline
                    {
                        Points = new System.Windows.Media.PointCollection(points),
                        Stroke = Brushes.Red,
                        StrokeThickness = SelectedStrokeThickness,
                        StrokeDashArray = new System.Windows.Media.DoubleCollection { 5, 3 },
                        Tag = TrajectoryPreviewTag
                    };
                    
                    _trajectoryPreviewPolylines.Add(polyline);
                    CadCanvas.Children.Add(polyline);
                }
            }
        }

        /// <summary>
        /// Creates a configuration object from the current application state.
        /// </summary>
        private Models.Configuration CreateConfigurationFromCurrentState(bool forSaving = false)
        {
            // This method now primarily ensures the ProductName is up-to-date in _currentConfiguration.
            // The actual SprayPasses and Trajectories are modified directly by UI interactions.
            _currentConfiguration.ProductName = ProductNameTextBox.Text;

            // The old logic of creating a single trajectory from _selectedDxfEntities is removed.
            // That list might be empty or used differently now.
            // If there's a need to explicitly "commit" selections from a temporary list to the current pass,
            // that logic would go here or be part of the selection process itself.

            // For now, we assume _currentConfiguration is the source of truth and is being updated live.
            return _currentConfiguration;
        }
        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog {
                Filter = "Config files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Save Configuration File",
                FileName = $"{ProductNameTextBox.Text}.json" // Suggest filename based on product name
            };
            // Set initial directory (similar to LoadDxfButton_Click)
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RobTeachProject", "RobTeach", "Configurations"));
            if (!Directory.Exists(initialDir)) initialDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configurations"); // Fallback
            if (!Directory.Exists(initialDir)) Directory.CreateDirectory(initialDir); // Create if doesn't exist
            saveFileDialog.InitialDirectory = initialDir;

            if (saveFileDialog.ShowDialog() == true)
            {
                _currentConfiguration = CreateConfigurationFromCurrentState(true); // Ensure latest state
                _currentConfiguration.ProductName = ProductNameTextBox.Text; // Update product name just before saving

                try
                {
                    _configService.SaveConfiguration(_currentConfiguration, saveFileDialog.FileName);
                    StatusTextBlock.Text = $"Configuration saved to {Path.GetFileName(saveFileDialog.FileName)}";
                    _currentLoadedConfigPath = saveFileDialog.FileName; // Update current loaded path
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error saving configuration.";
                    MessageBox.Show($"Failed to save configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                StatusTextBlock.Text = "Save configuration cancelled.";
            }
        }
        private void LoadConfigButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog {
                Filter = "Config files (*.json)|*.json|All files (*.*)|*.*",
                Title = "Load Configuration File"
            };
            // Set initial directory (similar to LoadDxfButton_Click for consistency)
            string initialDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "RobTeachProject", "RobTeach", "Configurations"));
            if (!Directory.Exists(initialDir)) initialDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configurations"); // Fallback
            openFileDialog.InitialDirectory = initialDir;

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    _currentConfiguration = _configService.LoadConfiguration(openFileDialog.FileName);
                    ProductNameTextBox.Text = _currentConfiguration.ProductName;

                    // Initialize Spray Passes from loaded configuration
                    if (_currentConfiguration.SprayPasses == null || !_currentConfiguration.SprayPasses.Any())
                    {
                        // If loaded config has no passes, create a default one.
                        _currentConfiguration.SprayPasses = new List<SprayPass> { new SprayPass { PassName = "Default Pass 1" } };
                        _currentConfiguration.CurrentPassIndex = 0;
                    }
                    else if (_currentConfiguration.CurrentPassIndex < 0 || _currentConfiguration.CurrentPassIndex >= _currentConfiguration.SprayPasses.Count)
                    {
                        // If index is invalid, default to first pass.
                        _currentConfiguration.CurrentPassIndex = _currentConfiguration.SprayPasses.Any() ? 0 : -1;
                    }

                    SprayPassesListBox.ItemsSource = null; // Refresh
                    SprayPassesListBox.ItemsSource = _currentConfiguration.SprayPasses;
                    if (_currentConfiguration.CurrentPassIndex >= 0 && _currentConfiguration.CurrentPassIndex < SprayPassesListBox.Items.Count)
                    {
                         SprayPassesListBox.SelectedIndex = _currentConfiguration.CurrentPassIndex;
                    }
                    else if (SprayPassesListBox.Items.Count > 0)
                    {
                        SprayPassesListBox.SelectedIndex = 0; // Fallback to selecting the first if index is out of sync
                        _currentConfiguration.CurrentPassIndex = 0;
                    }


                    // The old global nozzle checkbox updates are removed.
                    // UpperNozzleOnCheckBox_Changed(null, null); // Removed
                    // LowerNozzleOnCheckBox_Changed(null, null); // Removed

                    RefreshCurrentPassTrajectoriesListBox(); // Update trajectory list for the (newly) current pass
                    UpdateSelectedTrajectoryDetailUI(); // Renamed: Update nozzle UI for potentially selected trajectory
                    RefreshCadCanvasHighlights(); // Update canvas highlights for the loaded pass
                    UpdateDirectionIndicator(); // Config loaded, selection might have changed

                    // Assuming _cadService.GetWpfShapesFromDxf and entity selection logic
                    // might need to be re-run or updated if the config implies specific CAD entities.
                    // For now, just loading configuration values. Future work might involve
                    // re-selecting entities based on handles stored in config if _currentDxfDocument is still relevant.

                    StatusTextBlock.Text = $"Configuration loaded from {Path.GetFileName(openFileDialog.FileName)}";
                    _currentLoadedConfigPath = openFileDialog.FileName;
                }
                catch (Exception ex)
                {
                    StatusTextBlock.Text = "Error loading configuration.";
                    MessageBox.Show($"Failed to load configuration: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Reset to a default state if loading fails
                    _currentConfiguration = new Models.Configuration { ProductName = $"Product_{DateTime.Now:yyyyMMddHHmmss}" };
                    ProductNameTextBox.Text = _currentConfiguration.ProductName;
                    // Reset new nozzle CheckBoxes in UI (they are per-trajectory, so this just clears the UI if no trajectory selected)
                    UpdateSelectedTrajectoryDetailUI(); // Renamed: This will clear them if nothing is selected
                    // The old global nozzle checkbox resets are removed.
                    // UpperNozzleOnCheckBox_Changed(null, null); // Removed
                    // LowerNozzleOnCheckBox_Changed(null, null); // Removed
                    UpdateDirectionIndicator(); // Clear indicator if error during load
                }
            }
            else
            {
                StatusTextBlock.Text = "Load configuration cancelled.";
            }
        }
        private void ModbusConnectButton_Click(object sender, RoutedEventArgs e) { /* ... (No change) ... */ }
        private void ModbusDisconnectButton_Click(object sender, RoutedEventArgs e) { /* ... (No change) ... */ }
        private void SendToRobotButton_Click(object sender, RoutedEventArgs e) { /* ... (No change) ... */ }

        /// <summary>
        /// Calculates the overall bounding box of the DXF document, considering header extents and all entity extents.
        /// </summary>
        /// <param name="dxfDoc">The DXF document.</param>
        /// <returns>A Rect representing the bounding box, or Rect.Empty if no valid bounds can be determined.</returns>
        private Rect GetDxfBoundingBox(DxfFile dxfDoc)
        {
            if (dxfDoc == null)
            {
                return Rect.Empty;
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool hasValidBounds = false;

            // Calculate bounds directly from entities
            if (dxfDoc.Entities != null && dxfDoc.Entities.Any())
            {
                foreach (var entity in dxfDoc.Entities)
                {
                    if (entity == null) continue;

                    try
                    {
                        // Calculate entity bounds directly
                        var bounds = CalculateEntityBoundsSimple(entity);
                        if (bounds.HasValue)
                        {
                            var (eMinX, eMinY, eMaxX, eMaxY) = bounds.Value;
                            minX = Math.Min(minX, eMinX);
                            minY = Math.Min(minY, eMinY);
                            maxX = Math.Max(maxX, eMaxX);
                            maxY = Math.Max(maxY, eMaxY);
                            hasValidBounds = true;
                        }
                    }
                    catch
                    {
                        // Skip entities that can't be processed
                        continue;
                    }
                }
            }

            if (!hasValidBounds)
            {
                return Rect.Empty;
            }

            return new System.Windows.Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private void FitToViewButton_Click(object sender, RoutedEventArgs e) { /* ... (No change) ... */ }
        private void PerformFitToView() { /* ... (No change) ... */ }
        private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e) { /* ... (No change) ... */ }
        private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e) { /* ... (No change) ... */ }
        private void CadCanvas_MouseMove(object sender, MouseEventArgs e) { /* ... (No change) ... */ }
        private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e) { /* ... (No change) ... */ }
        /// <summary>
        /// Calculates the bounding rectangle for a given DXF entity.
        /// </summary>
        private (double minX, double minY, double maxX, double maxY)? CalculateEntityBoundsSimple(DxfEntity entity)
        {
            try
            {
                switch (entity)
                {
                    case DxfLine line:
                        var minX = Math.Min(line.P1.X, line.P2.X);
                        var maxX = Math.Max(line.P1.X, line.P2.X);
                        var minY = Math.Min(line.P1.Y, line.P2.Y);
                        var maxY = Math.Max(line.P1.Y, line.P2.Y);
                        return (minX, minY, maxX, maxY);

                    case DxfArc arc:
                        var centerX = arc.Center.X;
                        var centerY = arc.Center.Y;
                        var radius = arc.Radius;
                        return (centerX - radius, centerY - radius, centerX + radius, centerY + radius);

                    case DxfCircle circle:
                        var cX = circle.Center.X;
                        var cY = circle.Center.Y;
                        var r = circle.Radius;
                        return (cX - r, cY - r, cX + r, cY + r);

                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }



        private void HandleError(Exception ex, string action) { /* ... (No change) ... */ }
    }
}
