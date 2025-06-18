using System.Collections.Generic;

namespace RobTeach.Models
{
    /// <summary>
    /// Represents a complete configuration for a product, including its name,
    /// a list of trajectories, and transformation parameters.
    /// </summary>
    public class Configuration
    {
        /// <summary>
        /// Gets or sets the name of the product associated with this configuration.
        /// </summary>
        public string ProductName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of trajectories defined for this product configuration.
        /// This might be deprecated in favor of SprayPasses.
        /// </summary>
        public List<Trajectory> Trajectories { get; set; } = new List<Trajectory>();

        /// <summary>
        /// Gets or sets the list of spray passes for this configuration. Each pass can contain multiple trajectories.
        /// </summary>
        public List<SprayPass> SprayPasses { get; set; } = new List<SprayPass>();

        /// <summary>
        /// Gets or sets the index of the currently active or selected spray pass.
        /// -1 could indicate no pass is selected, or 0 for the first pass by default.
        /// </summary>
        public int CurrentPassIndex { get; set; } = -1;

        /// <summary>
        /// Gets or sets the transformation parameters associated with this configuration.
        /// This is a placeholder for future functionality like scaling, offsetting, or rotating
        /// the entire set of trajectories.
        /// </summary>
        public Transform TransformParameters { get; set; } = new Transform();

        /// <summary>
        /// Initializes a new instance of the <see cref="Configuration"/> class.
        /// A parameterless constructor is provided for JSON deserialization and typical instantiation.
        /// Trajectories list and TransformParameters are initialized to default instances.
        /// </summary>
        public Configuration()
        {
            // Default constructor.
        }
    }
}
