using System.Collections.Generic;
using System.Windows; // This using statement makes System.Windows.Point available as 'Point'
using IxMilia.Dxf; // Added for DxfPoint, DxfVector
using IxMilia.Dxf.Entities; // For DxfEntity
using System.Text.Json.Serialization; // For JsonIgnore

namespace RobTeach.Models
{
    /// <summary>
    /// Represents a single trajectory derived from a CAD entity, including its points and application parameters.
    /// </summary>
    public class Trajectory
    {
        /// <summary>
        /// Gets or sets the handle of the original CAD entity from which this trajectory was derived.
        /// This helps in linking the trajectory back to its source in the DXF document.
        /// </summary>
        public string OriginalEntityHandle { get; set; } = string.Empty; // May become redundant if OriginalDxfEntity is used primarily

        /// <summary>
        /// Stores the original DXF entity object. This is not serialized to JSON.
        /// </summary>
        [JsonIgnore]
        public DxfEntity? OriginalDxfEntity { get; set; } = null;

        /// <summary>
        /// Gets or sets the type of the original CAD entity (e.g., "Line", "Arc", "LwPolyline").
        /// </summary>
        public string EntityType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of points that define the path of the trajectory.
        /// These points are typically in the coordinate system intended for the robot.
        /// Uses <see cref="System.Windows.Point"/>. This is ignored for JSON serialization
        /// as geometric parameters below should be used for reconstruction.
        /// </summary>
        [JsonIgnore]
        public List<System.Windows.Point> Points { get; set; } = new List<System.Windows.Point>(); // Explicitly qualified

        /// <summary>
        /// Gets or sets the type of the primitive (e.g., "Line", "Arc", "Circle").
        /// This helps in interpreting the geometric parameters.
        /// </summary>
        public string PrimitiveType { get; set; } = string.Empty;

        // Geometric parameters for Line
        public DxfPoint LineStartPoint { get; set; } = DxfPoint.Origin;
        public DxfPoint LineEndPoint { get; set; } = DxfPoint.Origin;

        // Geometric parameters for Arc
        public DxfPoint ArcCenter { get; set; } = DxfPoint.Origin;
        public double ArcRadius { get; set; } = 0.0;
        public double ArcStartAngle { get; set; } = 0.0;
        public double ArcEndAngle { get; set; } = 0.0;
        public DxfVector ArcNormal { get; set; } = DxfVector.ZAxis;

        // Geometric parameters for Circle
        public DxfPoint CircleCenter { get; set; } = DxfPoint.Origin;
        public double CircleRadius { get; set; } = 0.0;
        public DxfVector CircleNormal { get; set; } = DxfVector.ZAxis;

        /// <summary>
        /// Gets or sets a value indicating whether the trajectory's conventional direction should be reversed.
        /// For example, for an arc, this might mean traversing from EndAngle to StartAngle.
        /// </summary>
        public bool IsReversed { get; set; } = false;

        /// <summary>
        /// Gets or sets the nozzle number to be used for this trajectory.
        /// </summary>
        public int NozzleNumber { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether water spray is used for this trajectory.
        /// True indicates water spray; false indicates air spray (or other non-water medium).
        /// </summary>
        // public bool IsWater { get; set; } // Removed as per new nozzle control scheme

        /// <summary>
        /// Gets or sets a value indicating whether the upper nozzle is enabled for this trajectory.
        /// </summary>
        public bool UpperNozzleEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether gas is on for the upper nozzle during this trajectory.
        /// </summary>
        public bool UpperNozzleGasOn { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether liquid is on for the upper nozzle during this trajectory.
        /// </summary>
        public bool UpperNozzleLiquidOn { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the lower nozzle is enabled for this trajectory.
        /// </summary>
        public bool LowerNozzleEnabled { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether gas is on for the lower nozzle during this trajectory.
        /// </summary>
        public bool LowerNozzleGasOn { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether liquid is on for the lower nozzle during this trajectory.
        /// </summary>
        public bool LowerNozzleLiquidOn { get; set; } = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="Trajectory"/> class.
        /// A parameterless constructor is required for JSON deserialization.
        /// </summary>
        public Trajectory()
        {
            // Default constructor for JSON deserialization and typical instantiation.
            // Points list is initialized by default.
        }

        public override string ToString()
        {
            string details = string.Empty;
            switch (PrimitiveType)
            {
                case "Line":
                    details = $"Line ({LineStartPoint} -> {LineEndPoint})";
                    break;
                case "Arc":
                    details = $"Arc (Cen:{ArcCenter}, R:{ArcRadius}, A:{ArcStartAngle:F1}°-{ArcEndAngle:F1}°)";
                    break;
                case "Circle":
                    details = $"Circle (Cen:{CircleCenter}, R:{CircleRadius})";
                    break;
                default:
                    // Use EntityType if PrimitiveType is not set or recognized, then fallback to DXF entity type
                    string typeDisplay = string.IsNullOrEmpty(PrimitiveType) ? (string.IsNullOrEmpty(EntityType) ? OriginalDxfEntity?.GetType().Name ?? "Unknown" : EntityType) : PrimitiveType;
                    details = $"{typeDisplay} (Points: {Points.Count})"; // Fallback for non-primitives or if points are primary
                    break;
            }
            string reversedStatus = IsReversed ? " [R]" : "";
            string upperStatus = UpperNozzleEnabled ? $"U(G:{(UpperNozzleGasOn?"On":"Off")},L:{(UpperNozzleLiquidOn?"On":"Off")})" : "U(Off)";
            string lowerStatus = LowerNozzleEnabled ? $"L(G:{(LowerNozzleGasOn?"On":"Off")},L:{(LowerNozzleLiquidOn?"On":"Off")})" : "L(Off)";
            return $"{details}{reversedStatus} - {upperStatus}, {lowerStatus}";
        }
    }
}
