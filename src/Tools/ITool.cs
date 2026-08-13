// File: Tools/ITool.cs
namespace SystemOptimizer.Tools
{
    /// <summary>
    /// An optional feature that lives outside Core.
    ///
    /// The point of this interface is the direction of the dependency. Core must never
    /// name a Tool: it asks the registry a question ("should automatic maintenance hold
    /// off?") and a Tool answers. That is what lets a Tool be added without touching Core
    /// and removed without leaving a hole - delete its folder and its one line in
    /// ToolRegistry, and nothing else in the product refers to it.
    ///
    /// Kept deliberately small. A Tool that needs more from Core than this is a sign the
    /// boundary is in the wrong place, not that the interface needs more methods.
    /// </summary>
    public interface ITool
    {
        /// <summary>Short name, as the user would recognise it.</summary>
        string Name { get; }

        /// <summary>Whether the user has this Tool switched on at all.</summary>
        bool IsActive { get; }

        /// <summary>
        /// Why automatic maintenance should not run right now, phrased for a person
        /// ("Photoshop is running"), or null to raise no objection.
        ///
        /// This governs AUTOMATIC work only. Anything the user explicitly asks for goes
        /// ahead regardless - a Tool may decide the app should keep quiet, never that it
        /// should ignore a direct instruction.
        /// </summary>
        string HoldOffReason();
    }
}
