using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor utility: creates a complete drink-making demo setup in the current scene.
/// Menu: Window > Iris > Setup Drink Demo Scene
///
/// Creates: 4 bottles, 2 glasses (highball + wine), 2 garnishes, DrinkPourManager,
/// all wired with placeholder SOs. Copy objects into your apartment scene.
/// </summary>
public class DrinkSceneSetup
{
    [MenuItem("Window/Iris/Setup Drink Demo Scene")]
    public static void Setup()
    {
        // ── Root container ──
        var root = new GameObject("=== DRINK DEMO ===");
        root.transform.position = Vector3.zero;

        // ── Create SOs in memory (not saved as assets — copy values manually) ──

        // Ingredients
        var ingredientA = CreateIngredient("Vodka",    new Color(0.9f, 0.9f, 0.95f, 0.6f), 0.12f, 0.2f);
        var ingredientB = CreateIngredient("Cranberry", new Color(0.8f, 0.15f, 0.2f, 0.8f), 0.15f, 0.6f);
        var ingredientC = CreateIngredient("Lime Juice", new Color(0.7f, 0.85f, 0.3f, 0.7f), 0.18f, 0.4f);
        var ingredientD = CreateIngredient("Malbec 2017", new Color(0.4f, 0.05f, 0.1f, 0.9f), 0.10f, 0.8f);

        // Glasses
        var highballDef = ScriptableObject.CreateInstance<GlassDefinition>();
        highballDef.glassName = "Highball";
        highballDef.capacity = 1f;
        highballDef.fillLineNormalized = 0.8f;
        highballDef.fillLineTolerance = 0.08f;
        highballDef.fillRateMultiplier = 1.5f;
        highballDef.worldHeight = 0.15f;
        highballDef.worldRadius = 0.03f;

        var wineDef = ScriptableObject.CreateInstance<GlassDefinition>();
        wineDef.glassName = "Wine Glass";
        wineDef.capacity = 0.7f;
        wineDef.fillLineNormalized = 0.5f;
        wineDef.fillLineTolerance = 0.1f;
        wineDef.fillRateMultiplier = 0.7f;
        wineDef.worldHeight = 0.18f;
        wineDef.worldRadius = 0.04f;

        // Garnishes
        var garnishLime = ScriptableObject.CreateInstance<DrinkGarnishDefinition>();
        garnishLime.garnishName = "Lime Wedge";
        garnishLime.bonusPoints = 5;

        var garnishCherry = ScriptableObject.CreateInstance<DrinkGarnishDefinition>();
        garnishCherry.garnishName = "Cherry";
        garnishCherry.bonusPoints = 5;

        // Recipes
        var cocktailRecipe = ScriptableObject.CreateInstance<DrinkRecipeDefinition>();
        cocktailRecipe.drinkName = "Cosmopolitan";
        cocktailRecipe.requiredGlass = highballDef;
        cocktailRecipe.ingredients = new[] { ingredientA, ingredientB, ingredientC };
        cocktailRecipe.portionNormalized = new[] { 0.3f, 0.45f, 0.25f };
        cocktailRecipe.idealFillLevel = 0.8f;
        cocktailRecipe.fillTolerance = 0.1f;
        cocktailRecipe.portionTolerance = 0.08f;
        cocktailRecipe.baseScore = 100;
        cocktailRecipe.garnishes = new[] { garnishLime };
        cocktailRecipe.liquidColor = new Color(0.8f, 0.2f, 0.3f, 0.8f);

        var wineRecipe = ScriptableObject.CreateInstance<DrinkRecipeDefinition>();
        wineRecipe.drinkName = "Malbec 2017";
        wineRecipe.requiredGlass = wineDef;
        wineRecipe.ingredients = new[] { ingredientD };
        wineRecipe.portionNormalized = new[] { 1f };
        wineRecipe.idealFillLevel = 0.5f;
        wineRecipe.fillTolerance = 0.12f;
        wineRecipe.portionTolerance = 0.15f;
        wineRecipe.baseScore = 100;
        wineRecipe.garnishes = new DrinkGarnishDefinition[0];
        wineRecipe.liquidColor = new Color(0.4f, 0.05f, 0.1f, 0.9f);

        // Save all SOs to disk so they survive scene copy/paste
        const string soPath = "Assets/ScriptableObjects/DrinkMaking";
        if (!AssetDatabase.IsValidFolder(soPath))
        {
            if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects"))
                AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
            AssetDatabase.CreateFolder("Assets/ScriptableObjects", "DrinkMaking");
        }

        SaveSO(ingredientA, soPath + "/Ingredient_Vodka.asset");
        SaveSO(ingredientB, soPath + "/Ingredient_Cranberry.asset");
        SaveSO(ingredientC, soPath + "/Ingredient_LimeJuice.asset");
        SaveSO(ingredientD, soPath + "/Ingredient_Malbec.asset");
        SaveSO(highballDef, soPath + "/Glass_Highball.asset");
        SaveSO(wineDef,     soPath + "/Glass_Wine.asset");
        SaveSO(garnishLime,   soPath + "/Garnish_Lime.asset");
        SaveSO(garnishCherry, soPath + "/Garnish_Cherry.asset");
        SaveSO(cocktailRecipe, soPath + "/Recipe_Cosmopolitan.asset");
        SaveSO(wineRecipe,     soPath + "/Recipe_Malbec.asset");
        AssetDatabase.SaveAssets();

        int placeableLayer = LayerMask.NameToLayer("Placeable");
        if (placeableLayer < 0) placeableLayer = 0;

        // ── Bottles ──
        float bottleX = -0.4f;
        CreateBottle(root.transform, "Bottle_Vodka",    ingredientA, bottleX, placeableLayer);
        CreateBottle(root.transform, "Bottle_Cranberry", ingredientB, bottleX + 0.15f, placeableLayer);
        CreateBottle(root.transform, "Bottle_Lime",     ingredientC, bottleX + 0.30f, placeableLayer);
        CreateBottle(root.transform, "Bottle_Malbec",   ingredientD, bottleX + 0.45f, placeableLayer);

        // ── Glasses ──
        CreateGlass(root.transform, "Glass_Highball", highballDef, 0.2f, 0.03f, 0.15f, placeableLayer);
        CreateGlass(root.transform, "Glass_Wine",     wineDef,     0.4f, 0.04f, 0.18f, placeableLayer);

        // ── Garnishes ──
        CreateGarnish(root.transform, "Garnish_Lime",   garnishLime,   0.6f, placeableLayer);
        CreateGarnish(root.transform, "Garnish_Cherry", garnishCherry, 0.7f, placeableLayer);

        // ── DrinkPourManager ──
        var managerGO = new GameObject("DrinkPourManager");
        managerGO.transform.SetParent(root.transform);
        var manager = managerGO.AddComponent<DrinkPourManager>();
        // Wire recipes via serialized field
        var so = new SerializedObject(manager);
        var recipesProp = so.FindProperty("_recipes");
        recipesProp.arraySize = 2;
        recipesProp.GetArrayElementAtIndex(0).objectReferenceValue = cocktailRecipe;
        recipesProp.GetArrayElementAtIndex(1).objectReferenceValue = wineRecipe;
        so.ApplyModifiedProperties();

        // ── Counter surface (placeholder) ──
        var counter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        counter.name = "Counter_Surface";
        counter.transform.SetParent(root.transform);
        counter.transform.localPosition = new Vector3(0.15f, -0.05f, 0f);
        counter.transform.localScale = new Vector3(1.5f, 0.1f, 0.4f);
        counter.GetComponent<Renderer>().material.color = new Color(0.35f, 0.3f, 0.28f);

        Selection.activeGameObject = root;
        Debug.Log("[DrinkSceneSetup] Demo scene created. SOs saved to Assets/ScriptableObjects/DrinkMaking/");
        EditorUtility.DisplayDialog("Drink Demo Created",
            "Created under '=== DRINK DEMO ===' root.\n\n" +
            "SOs saved to Assets/ScriptableObjects/DrinkMaking/\n" +
            "All references are wired — copy/paste objects\n" +
            "directly into your apartment scene.",
            "Got it");
    }

    private static DrinkIngredientDefinition CreateIngredient(string name, Color color, float pourRate, float weight)
    {
        var def = ScriptableObject.CreateInstance<DrinkIngredientDefinition>();
        def.ingredientName = name;
        def.liquidColor = color;
        def.pourRate = pourRate;
        def.weight = weight;
        def.fizziness = 0f;
        def.foamRateMultiplier = 1f;
        def.foamSettleRate = 0.3f;
        def.viscosity = 0.5f;
        return def;
    }

    private static void CreateBottle(Transform parent, string name, DrinkIngredientDefinition ingredient, float x, int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.localPosition = new Vector3(x, 0.1f, -0.1f);
        go.transform.localScale = new Vector3(0.03f, 0.08f, 0.03f);
        go.layer = layer;
        go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = ingredient.liquidColor };

        // Pour spout
        var spout = new GameObject("PourSpout");
        spout.transform.SetParent(go.transform);
        spout.transform.localPosition = new Vector3(0f, 1f, 0f); // top of cylinder

        // Components
        var bottle = go.AddComponent<BottleItem>();
        var bottleSO = new SerializedObject(bottle);
        bottleSO.FindProperty("_ingredient").objectReferenceValue = ingredient;
        bottleSO.FindProperty("_pourPoint").objectReferenceValue = spout.transform;
        bottleSO.ApplyModifiedProperties();

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        go.AddComponent<PlaceableObject>();
        go.AddComponent<ItemHighlight>();
    }

    private static void CreateGlass(Transform parent, string name, GlassDefinition glassDef, float x, float radius, float height, int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.localPosition = new Vector3(x, height * 0.5f, 0f);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        go.layer = layer;

        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = glassDef.glassColor };

        // DrinkGlass component
        var glass = go.AddComponent<DrinkGlass>();
        var glassSO = new SerializedObject(glass);
        glassSO.FindProperty("_glassDefinition").objectReferenceValue = glassDef;
        glassSO.ApplyModifiedProperties();

        go.AddComponent<ItemHighlight>();
    }

    private static void CreateGarnish(Transform parent, string name, DrinkGarnishDefinition garnishDef, float x, int layer)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.localPosition = new Vector3(x, 0.02f, 0.1f);
        go.transform.localScale = Vector3.one * 0.025f;
        go.layer = layer;
        Color gc = name.Contains("Lime") ? new Color(0.5f, 0.8f, 0.2f) : new Color(0.7f, 0.1f, 0.15f);
        go.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = gc };

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;

        go.AddComponent<PlaceableObject>();
        go.AddComponent<ItemHighlight>();

        // TODO: Add GarnishItem marker component when it exists
    }

    private static void SaveSO(ScriptableObject so, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        if (existing != null)
            AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(so, path);
    }
}
