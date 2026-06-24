namespace FastViewDX12;

/// <summary>
/// Selects the visual source behind the model without changing environment-light availability.
/// </summary>
public enum ViewerBackgroundMode
{
    /// <summary>Clear the frame to the user-selected solid color.</summary>
    SolidColor,

    /// <summary>Draw the loaded EXR environment and blend it over the solid color.</summary>
    Environment
}
