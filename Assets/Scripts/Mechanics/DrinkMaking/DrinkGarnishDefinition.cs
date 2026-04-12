using UnityEngine;

/// <summary>
/// Defines a garnish that can be dropped onto a drink glass for bonus points.
/// The player physically picks up the garnish item and drops it on the glass.
/// </summary>
[CreateAssetMenu(menuName = "Iris/Drink Garnish")]
public class DrinkGarnishDefinition : ScriptableObject
{
    [Tooltip("Display name (e.g. 'Lime Wedge', 'Maraschino Cherry').")]
    public string garnishName;

    [Tooltip("Bonus points added to the drink score when this garnish is used correctly.")]
    public int bonusPoints = 5;
}
