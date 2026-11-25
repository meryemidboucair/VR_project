
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class AnimalCategoryClick : MonoBehaviour
{
    public AnimalGraphBuilder graphBuilder;
    public string categoryName;

    public void ToggleCategory()
    {
        if (graphBuilder != null)
            graphBuilder.ToggleCategory(categoryName);
    }

    private void OnMouseDown()
    {
        ToggleCategory();
    }
}
