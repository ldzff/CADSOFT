using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobTeach.Views
{
    public class DirectionIndicator : Shape
    {
        // DependencyProperties
        public static readonly DependencyProperty StartPointProperty =
            DependencyProperty.Register("StartPoint", typeof(Point), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(new Point(0, 0), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty EndPointProperty =
            DependencyProperty.Register("EndPoint", typeof(Point), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(new Point(0, 0), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ArrowheadSizeProperty =
            DependencyProperty.Register("ArrowheadSize", typeof(double), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ColorProperty =
            DependencyProperty.Register("Color", typeof(Brush), typeof(DirectionIndicator),
                new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure),
                new ValidateValueCallback(IsValidBrush));

        private static bool IsValidBrush(object value)
        {
            return value is Brush;
        }

        // CLR Properties
        public Point StartPoint
        {
            get { return (Point)GetValue(StartPointProperty); }
            set { SetValue(StartPointProperty, value); }
        }

        public Point EndPoint
        {
            get { return (Point)GetValue(EndPointProperty); }
            set { SetValue(EndPointProperty, value); }
        }

        public double ArrowheadSize
        {
            get { return (double)GetValue(ArrowheadSizeProperty); }
            set { SetValue(ArrowheadSizeProperty, value); }
        }

        public Brush Color
        {
            get { return (Brush)GetValue(ColorProperty); }
            set { SetValue(ColorProperty, value); }
        }

        // Constructor
        public DirectionIndicator()
        {
            // Default values are set by DependencyProperty metadata.
            // StrokeThickness is a property from the base Shape class.
            StrokeThickness = 2;
        }

        // Override DefiningGeometry
        protected override Geometry DefiningGeometry
        {
            get
            {
                if (StartPoint == EndPoint || ArrowheadSize <= 0)
                    return Geometry.Empty;

                // Set Fill and Stroke for the shape.
                // The Color property of this class is used as the source.
                this.Fill = Color;
                this.Stroke = Color;

                PathGeometry pathGeometry = new PathGeometry();

                // Figure for the main line
                PathFigure lineFigure = new PathFigure();
                lineFigure.StartPoint = StartPoint;
                lineFigure.Segments.Add(new LineSegment(EndPoint, true /* isStroked */));
                pathGeometry.Figures.Add(lineFigure);

                // Calculate direction vector of the arrow line
                Vector dir = EndPoint - StartPoint;
                if (dir.Length == 0) // Should be caught by StartPoint == EndPoint, but as a safeguard
                    return Geometry.Empty;
                dir.Normalize();

                // Angle of the arrowhead wings relative to the arrow line
                double angleInDegrees = 30.0;
                double angleInRadians = Math.PI * angleInDegrees / 180.0;

                // Calculate points for the arrowhead wings
                // ArrowheadSize determines the length of the arrowhead's "legs" or "wings"

                // Wing 1
                // Rotate direction vector by 'angleInRadians' and scale by ArrowheadSize
                Vector wingDir1 = new Vector(
                    dir.X * Math.Cos(angleInRadians) - dir.Y * Math.Sin(angleInRadians),
                    dir.X * Math.Sin(angleInRadians) + dir.Y * Math.Cos(angleInRadians)
                );
                Point wingPoint1 = EndPoint - wingDir1 * ArrowheadSize;

                // Wing 2
                // Rotate direction vector by '-angleInRadians' and scale by ArrowheadSize
                Vector wingDir2 = new Vector(
                    dir.X * Math.Cos(-angleInRadians) - dir.Y * Math.Sin(-angleInRadians),
                    dir.X * Math.Sin(-angleInRadians) + dir.Y * Math.Cos(-angleInRadians)
                );
                Point wingPoint2 = EndPoint - wingDir2 * ArrowheadSize;

                // Figure for the arrowhead (a filled triangle)
                PathFigure arrowheadFigure = new PathFigure();
                arrowheadFigure.StartPoint = EndPoint; // Tip of the arrow
                arrowheadFigure.Segments.Add(new LineSegment(wingPoint1, true /* isStroked */));
                arrowheadFigure.Segments.Add(new LineSegment(wingPoint2, true /* isStroked */));
                arrowheadFigure.IsClosed = true;  // Close the path to form a triangle
                arrowheadFigure.IsFilled = true;  // Ensure the arrowhead is filled
                pathGeometry.Figures.Add(arrowheadFigure);

                return pathGeometry;
            }
        }
    }
}
