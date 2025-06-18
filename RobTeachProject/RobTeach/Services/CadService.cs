using IxMilia.Dxf;
using IxMilia.Dxf.Entities;
// using netDxf.Tables;
// using netDxf.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Linq;
using RobTeach.Models; // Added for Trajectory type
// using System.Windows.Shapes;

namespace RobTeach.Services
{
    /// <summary>
    /// Provides services for loading CAD (DXF) files and converting DXF entities
    /// into WPF shapes and trajectory points using IxMilia.Dxf library.
    /// </summary>
    public class CadService
    {
        /// <summary>
        /// Loads a DXF document from the specified file path with enhanced error handling and version compatibility.
        /// </summary>
        public DxfFile LoadDxf(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath), "File path cannot be null or empty.");
            }
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("DXF file not found.", filePath);
            }
            
            try
            {
                // IxMilia.Dxf is more forgiving with DXF formats
                DxfFile dxf = DxfFile.Load(filePath);
                return dxf;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading or parsing DXF file: {ex.Message}", ex);
            }
        }
        
        /// <summary>
        /// Converts entities from a <see cref="DxfFile"/> into a list of WPF <see cref="System.Windows.Shapes.Shape"/> objects for display.
        /// Supports Lines, Arcs, and Circles.
        /// </summary>
        public List<System.Windows.Shapes.Shape> GetWpfShapesFromDxf(DxfFile dxfFile)
        {
            var wpfShapes = new List<System.Windows.Shapes.Shape>();
            if (dxfFile == null) return wpfShapes;

            // Process all entities
            foreach (var entity in dxfFile.Entities)
            {
                switch (entity)
                {
                    case DxfLine dxfLine:
                        var wpfLine = new System.Windows.Shapes.Line
                        {
                            X1 = dxfLine.P1.X, Y1 = dxfLine.P1.Y,
                            X2 = dxfLine.P2.X, Y2 = dxfLine.P2.Y,
                            IsHitTestVisible = true
                        };
                        wpfShapes.Add(wpfLine);
                        break;

                    case DxfArc dxfArc:
                        var arcPath = CreateArcPath(dxfArc);
                        if (arcPath != null)
                        {
                            wpfShapes.Add(arcPath);
                        }
                        break;

                    case DxfCircle dxfCircle:
                        var ellipseGeometry = new EllipseGeometry(
                            new System.Windows.Point(dxfCircle.Center.X, dxfCircle.Center.Y),
                            dxfCircle.Radius,
                            dxfCircle.Radius
                        );
                        var circlePath = new System.Windows.Shapes.Path
                        {
                            Data = ellipseGeometry,
                            Fill = Brushes.Transparent,
                            IsHitTestVisible = true
                        };
                        wpfShapes.Add(circlePath);
                        break;
                }
            }

            return wpfShapes;
        }

        /// <summary>
        /// Creates a WPF Path for a DXF Arc.
        /// </summary>
        private System.Windows.Shapes.Path? CreateArcPath(DxfArc dxfArc)
        {
            try
            {
                double startAngleRad = dxfArc.StartAngle * Math.PI / 180.0;
                double endAngleRad = dxfArc.EndAngle * Math.PI / 180.0;
                
                double arcStartX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(startAngleRad);
                double arcStartY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(startAngleRad);
                var pathStartPoint = new System.Windows.Point(arcStartX, arcStartY);

                double arcEndX = dxfArc.Center.X + dxfArc.Radius * Math.Cos(endAngleRad);
                double arcEndY = dxfArc.Center.Y + dxfArc.Radius * Math.Sin(endAngleRad);
                var arcSegmentEndPoint = new System.Windows.Point(arcEndX, arcEndY);

                double sweepAngleDegrees = dxfArc.EndAngle - dxfArc.StartAngle;
                if (sweepAngleDegrees < 0) sweepAngleDegrees += 360;
                
                bool isLargeArc = sweepAngleDegrees > 180.0;
                SweepDirection sweepDirection = SweepDirection.Counterclockwise;

                ArcSegment arcSegment = new ArcSegment
                {
                    Point = arcSegmentEndPoint,
                    Size = new System.Windows.Size(dxfArc.Radius, dxfArc.Radius),
                    IsLargeArc = isLargeArc,
                    SweepDirection = sweepDirection,
                    RotationAngle = 0,
                    IsStroked = true
                };

                PathFigure pathFigure = new PathFigure
                {
                    StartPoint = pathStartPoint,
                    IsClosed = false
                };
                pathFigure.Segments.Add(arcSegment);

                PathGeometry pathGeometry = new PathGeometry();
                pathGeometry.Figures.Add(pathFigure);

                return new System.Windows.Shapes.Path
                {
                    Data = pathGeometry,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = true
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a DXF Line entity into a list of two System.Windows.Point objects.
        /// </summary>
        public List<System.Windows.Point> ConvertLineToPoints(DxfLine line)
        {
            var points = new List<System.Windows.Point>();
            if (line == null) return points;
            points.Add(new System.Windows.Point(line.P1.X, line.P1.Y));
            points.Add(new System.Windows.Point(line.P2.X, line.P2.Y));
            return points;
        }

        /// <summary>
        /// Converts a DXF Arc entity into a list of discretized System.Windows.Point objects.
        /// </summary>
        public List<System.Windows.Point> ConvertArcToPoints(DxfArc arc, double resolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            if (arc == null || resolutionDegrees <= 0) return points;

            double startAngle = arc.StartAngle;
            double endAngle = arc.EndAngle;
            double radius = arc.Radius;
            System.Windows.Point center = new System.Windows.Point(arc.Center.X, arc.Center.Y);

            // Normalize angles
            if (endAngle < startAngle) endAngle += 360;

            double currentAngle = startAngle;
            while (currentAngle <= endAngle)
            {
                double radAngle = currentAngle * Math.PI / 180.0;
                double x = center.X + radius * Math.Cos(radAngle);
                double y = center.Y + radius * Math.Sin(radAngle);
                points.Add(new System.Windows.Point(x, y));
                
                currentAngle += resolutionDegrees;
            }

            // Ensure end point is included
            if (Math.Abs(currentAngle - resolutionDegrees - endAngle) > 0.001)
            {
                double endRadAngle = endAngle * Math.PI / 180.0;
                double endX = center.X + radius * Math.Cos(endRadAngle);
                double endY = center.Y + radius * Math.Sin(endRadAngle);
                points.Add(new System.Windows.Point(endX, endY));
            }

            return points;
        }

        /// <summary>
        /// Converts a DXF Circle entity to a list of points representing its perimeter.
        /// </summary>
        public List<System.Windows.Point> ConvertCircleToPoints(DxfCircle circle, double resolutionDegrees)
        {
            List<System.Windows.Point> points = new List<System.Windows.Point>();
            if (circle == null || resolutionDegrees <= 0) return points;

            for (double angle = 0; angle < 360.0; angle += resolutionDegrees)
            {
                double radAngle = angle * Math.PI / 180.0;
                double x = circle.Center.X + circle.Radius * Math.Cos(radAngle);
                double y = circle.Center.Y + circle.Radius * Math.Sin(radAngle);
                points.Add(new System.Windows.Point(x, y));
            }

            return points;
        }

        /* // LightWeightPolyline processing removed as per subtask
        /// <summary>
        /// Converts a DXF LwPolyline entity into a list of discretized System.Windows.Point objects.
        /// </summary>
        public List<System.Windows.Point> ConvertLwPolylineToPoints(LightWeightPolyline polyline, double arcResolutionDegrees)
        {
            var points = new List<System.Windows.Point>();
            if (polyline == null || polyline.Vertices.Count == 0) return points;
            for (int i = 0; i < polyline.Vertices.Count; i++) {
                var currentVertexInfo = polyline.Vertices[i];
                System.Windows.Point currentDxfPoint = new System.Windows.Point(currentVertexInfo.Position.X, currentVertexInfo.Position.Y);
                points.Add(currentDxfPoint);
                if (Math.Abs(currentVertexInfo.Bulge) > 0.0001) {
                    if (!polyline.IsClosed && i == polyline.Vertices.Count - 1) continue;
                    // TODO: Implement LwPolyline bulge to Arc conversion for trajectory points.
                }
            }
            if (polyline.IsClosed && points.Count > 1 && System.Windows.Point.Subtract(points.First(), points.Last()).Length > 0.001) { // Requires System.Linq for .First() and .Last()
                 points.Add(points[0]);
            } else if (polyline.Vertices.Count == 1 && !points.Any()){ // Requires System.Linq for .Any()
                 points.Add(new System.Windows.Point(polyline.Vertices[0].Position.X, polyline.Vertices[0].Position.Y));
            }
            return points;
        }
        */
    // } // This closing brace was prematurely ending the CadService class. Moved to the end.

    // New methods to generate points from Trajectory geometric parameters

    /// <summary>
    /// Converts a Line Trajectory object into a list of two System.Windows.Point objects.
    /// Respects the IsReversed flag on the Trajectory.
    /// </summary>
    public List<System.Windows.Point> ConvertLineTrajectoryToPoints(Trajectory trajectory)
    {
        var points = new List<System.Windows.Point>();
        if (trajectory == null || trajectory.PrimitiveType != "Line") return points;

        DxfPoint start = trajectory.LineStartPoint;
        DxfPoint end = trajectory.LineEndPoint;

        if (trajectory.IsReversed)
        {
            points.Add(new System.Windows.Point(end.X, end.Y));
            points.Add(new System.Windows.Point(start.X, start.Y));
        }
        else
        {
            points.Add(new System.Windows.Point(start.X, start.Y));
            points.Add(new System.Windows.Point(end.X, end.Y));
        }
        return points;
    }

    /// <summary>
    /// Converts an Arc Trajectory object into a list of discretized System.Windows.Point objects.
    /// Respects the IsReversed flag on the Trajectory.
    /// </summary>
    public List<System.Windows.Point> ConvertArcTrajectoryToPoints(Trajectory trajectory, double resolutionDegrees)
    {
        var points = new List<System.Windows.Point>();
        if (trajectory == null || trajectory.PrimitiveType != "Arc" || resolutionDegrees <= 0) return points;

        double startAngle = trajectory.ArcStartAngle;
        double endAngle = trajectory.ArcEndAngle;
        double radius = trajectory.ArcRadius;
        System.Windows.Point center = new System.Windows.Point(trajectory.ArcCenter.X, trajectory.ArcCenter.Y);
        // Note: trajectory.ArcNormal is available if needed for 3D calculations, but WPF points are 2D.

        // Normalize angles: if IsReversed, we swap, then normalize.
        // Otherwise, normalize standard way.
        if (trajectory.IsReversed)
        {
            double temp = startAngle;
            startAngle = endAngle;
            endAngle = temp;
        }

        // Standard normalization for arc traversal (always counter-clockwise for DXF interpretation)
        // If after potential reversal, endAngle is less than startAngle, adjust for CCW traversal.
        double currentSweepAngle = endAngle - startAngle;
        if (currentSweepAngle < 0)
        {
            currentSweepAngle += 360.0;
        }
        // Effective end angle for loop condition
        double effectiveEndAngle = startAngle + currentSweepAngle;


        double currentAngle = startAngle;
        // Loop with a small tolerance for floating point comparisons
        while (currentAngle <= effectiveEndAngle + (resolutionDegrees / 2.0)) // Add half step for inclusivity at end
        {
            // Ensure we don't overshoot the effectiveEndAngle significantly
            double angleToProcess = Math.Min(currentAngle, effectiveEndAngle);

            double radAngle = angleToProcess * Math.PI / 180.0;
            double x = center.X + radius * Math.Cos(radAngle);
            double y = center.Y + radius * Math.Sin(radAngle);
            points.Add(new System.Windows.Point(x, y));

            if (angleToProcess >= effectiveEndAngle - 0.00001) // Break if we processed the end angle
                break;

            currentAngle += resolutionDegrees;
            if (currentAngle > effectiveEndAngle && angleToProcess < effectiveEndAngle - 0.00001) // Ensure last point is added if step overshoots
            {
                 currentAngle = effectiveEndAngle; // Force next iteration to be the end angle
            }
        }

        // If IsReversed was true initially, the points were generated in reverse order.
        // No need to call points.Reverse() because the loop already traversed from "new" start to "new" end.

        return points;
    }

    /// <summary>
    /// Converts a Circle Trajectory object to a list of points representing its perimeter.
    /// IsReversed is currently not implemented for circle point generation.
    /// </summary>
    public List<System.Windows.Point> ConvertCircleTrajectoryToPoints(Trajectory trajectory, double resolutionDegrees)
    {
        List<System.Windows.Point> points = new List<System.Windows.Point>();
        if (trajectory == null || trajectory.PrimitiveType != "Circle" || resolutionDegrees <= 0) return points;

        // Note: trajectory.CircleNormal is available if needed for 3D calculations.
        // IsReversed is not currently affecting circle point generation.
        for (double angle = 0; angle < 360.0; angle += resolutionDegrees)
        {
            double radAngle = angle * Math.PI / 180.0;
            double x = trajectory.CircleCenter.X + trajectory.CircleRadius * Math.Cos(radAngle);
            double y = trajectory.CircleCenter.Y + trajectory.CircleRadius * Math.Sin(radAngle);
            points.Add(new System.Windows.Point(x, y));
        }
        // Ensure the circle is closed by adding the start point if it's not already the last point due to resolution.
        if (points.Count > 0)
        {
             if (Math.Abs(points.Last().X - (trajectory.CircleCenter.X + trajectory.CircleRadius)) > 0.001 ||
                 Math.Abs(points.Last().Y - trajectory.CircleCenter.Y) > 0.001)
                 {
                      points.Add(new System.Windows.Point(trajectory.CircleCenter.X + trajectory.CircleRadius, trajectory.CircleCenter.Y));
                 }
        }


        return points;
    }
} // This closes the CadService class

} // This closes the namespace RobTeach.Services
