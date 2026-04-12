using UnityEngine;

/// <summary>
/// Marker component on a grabbable drink bottle. ObjectGrabber detects
/// this for magnetic snap toward DrinkGlass objects. References the
/// ingredient definition for pour rate, color, and weight.
/// </summary>
public class BottleItem : MonoBehaviour
{
    [Tooltip("Which ingredient this bottle contains.")]
    [SerializeField] private DrinkIngredientDefinition _ingredient;

    [Tooltip("Transform at the bottle's pour spout (where liquid comes from).")]
    [SerializeField] private Transform _pourPoint;

    public DrinkIngredientDefinition Ingredient => _ingredient;
    public Transform PourPoint => _pourPoint != null ? _pourPoint : transform;
}
