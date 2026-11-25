
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class AnimalGraphBuilder : MonoBehaviour
{
    [Serializable]
    public class AnimalNode
    {
        public string name;
        public string category;
        public string info;
        public string type;
    }

    [Serializable]
    public class AnimalLink
    {
        public string from;
        public string to;
        public string label;
    }

    [Serializable]
    public class CategoryColor
    {
        public string category;
        public Color color = Color.white;
    }

    [Serializable]
    class GraphData
    {
        public List<AnimalNode> nodes = new List<AnimalNode>();
        public List<AnimalLink> links = new List<AnimalLink>();
    }

    [Header("Prefabs")]
    public GameObject nodePrefab;
    public GameObject edgePrefab;

    [Header("Données")]
    public TextAsset jsonData;

    [Header("Disposition")]
    public float categoryRadius = 4f;
    public float animalRadius = 2.5f;
    public float categoryScale = 1.5f;
    public float animalScale = 0.7f;

    [Header("Apparence")]
    public bool colorByCategory = true;
    public Color defaultColor = Color.white;
    public List<CategoryColor> categoryColors = new List<CategoryColor>();

    [Header("Liens")]
    public float edgeWidth = 0.025f;
    public Color edgeColor = Color.white;

    [Header("Construction")]
    public bool buildOnStart = false;

    private Transform container;
    private GraphData data;

    private Dictionary<string, GameObject> categoryObjects =
        new Dictionary<string, GameObject>();

    private Dictionary<string, List<AnimalNode>> animalsByCategory =
        new Dictionary<string, List<AnimalNode>>();

    private Dictionary<string, List<GameObject>> visibleAnimals =
        new Dictionary<string, List<GameObject>>();

    private Dictionary<string, List<GameObject>> visibleEdges =
        new Dictionary<string, List<GameObject>>();

    void Start()
    {
        if (buildOnStart)
            Build();
    }

    [ContextMenu("Construire le graphe")]
    public void Build()
    {
        if (nodePrefab == null || jsonData == null)
        {
            Debug.LogError("Assigne Node Prefab et Json Data.");
            return;
        }

        data = JsonUtility.FromJson<GraphData>(jsonData.text);

        if (data == null || data.nodes == null)
        {
            Debug.LogError("JSON invalide.");
            return;
        }

        Clear();

        container = transform.Find("GraphContent");

        if (container == null)
        {
            GameObject c = new GameObject("GraphContent");
            c.transform.SetParent(transform, false);
            container = c.transform;
        }

        categoryObjects.Clear();
        animalsByCategory.Clear();
        visibleAnimals.Clear();
        visibleEdges.Clear();

        List<AnimalNode> categories = new List<AnimalNode>();

        // Séparation des catégories et des animaux
        foreach (AnimalNode n in data.nodes)
        {
            if (n.type == "category")
            {
                categories.Add(n);
                animalsByCategory[n.name] = new List<AnimalNode>();
            }
        }

        foreach (AnimalNode n in data.nodes)
        {
            if (n.type != "category")
            {
                if (!animalsByCategory.ContainsKey(n.category))
                    animalsByCategory[n.category] = new List<AnimalNode>();

                animalsByCategory[n.category].Add(n);
            }
        }

        // Création des catégories uniquement
        for (int i = 0; i < categories.Count; i++)
        {
            float angle = i * Mathf.PI * 2f / categories.Count;

            Vector3 position = new Vector3(
                Mathf.Cos(angle) * categoryRadius,
                Mathf.Sin(angle) * categoryRadius,
                0
            );

            CreateCategory(categories[i], position);
        }

        Debug.Log("Graphe prêt : " + categories.Count + " catégories.");
    }

    void CreateCategory(AnimalNode n, Vector3 position)
    {
        GameObject go = CreateNode(n, position, categoryScale);

        categoryObjects[n.name] = go;
        visibleAnimals[n.name] = new List<GameObject>();
        visibleEdges[n.name] = new List<GameObject>();

        AnimalCategoryClick click = go.GetComponent<AnimalCategoryClick>();

        if (click == null)
            click = go.AddComponent<AnimalCategoryClick>();

        click.graphBuilder = this;
        click.categoryName = n.name;

        if (go.GetComponent<Collider>() == null)
            go.AddComponent<SphereCollider>();
    }

    GameObject CreateNode(AnimalNode n, Vector3 position, float scale)
    {
        GameObject go = Instantiate(nodePrefab, container);

        go.name = "Node_" + n.name;
        go.transform.localPosition = position;
        go.transform.localScale = Vector3.one * scale;

        AnimalNodeData d = go.GetComponent<AnimalNodeData>();

        if (d == null)
            d = go.AddComponent<AnimalNodeData>();

        d.speciesName = n.name;
        d.category = n.category;
        d.info = n.info;
        d.isCategory = n.type == "category";
        d.isExpanded = false;

        TMP_Text label = go.GetComponentInChildren<TMP_Text>();

        if (label != null)
            label.text = n.name;

        if (colorByCategory)
            ApplyColor(go, GetColor(n.category));

        return go;
    }

    public void ToggleCategory(string categoryName)
    {
        if (!categoryObjects.ContainsKey(categoryName))
            return;

        AnimalNodeData d =
            categoryObjects[categoryName].GetComponent<AnimalNodeData>();

        if (d.isExpanded)
            CollapseCategory(categoryName);
        else
            ExpandCategory(categoryName);

        d.isExpanded = !d.isExpanded;
    }

    void ExpandCategory(string categoryName)
    {
        if (!animalsByCategory.ContainsKey(categoryName))
            return;

        List<AnimalNode> animals = animalsByCategory[categoryName];

        Transform center = categoryObjects[categoryName].transform;

        for (int i = 0; i < animals.Count; i++)
        {
            float angle = i * Mathf.PI * 2f / animals.Count;

            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * animalRadius,
                Mathf.Sin(angle) * animalRadius,
                0
            );

            Vector3 position = center.localPosition + offset;

            GameObject animal = CreateNode(
                animals[i],
                position,
                animalScale
            );

            visibleAnimals[categoryName].Add(animal);

            CreateEdge(
                center,
                animal.transform,
                categoryName
            );
        }

        Debug.Log(categoryName + " ouvert.");
    }

    void CollapseCategory(string categoryName)
    {
        foreach (GameObject animal in visibleAnimals[categoryName])
            if (animal != null)
                Destroy(animal);

        foreach (GameObject edge in visibleEdges[categoryName])
            if (edge != null)
                Destroy(edge);

        visibleAnimals[categoryName].Clear();
        visibleEdges[categoryName].Clear();

        Debug.Log(categoryName + " fermé.");
    }

    void CreateEdge(Transform a, Transform b, string categoryName)
    {
        GameObject go;

        if (edgePrefab != null)
        {
            go = Instantiate(edgePrefab, container);
        }
        else
        {
            go = new GameObject("Edge");
            go.transform.SetParent(container, false);
            go.AddComponent<LineRenderer>();
        }

        Edge edge = go.GetComponent<Edge>();

        if (edge == null)
            edge = go.AddComponent<Edge>();

        edge.nodeA = a;
        edge.nodeB = b;
        edge.width = edgeWidth;
        edge.lineColor = edgeColor;
        edge.maxDistance = float.MaxValue * 0.5f;

        visibleEdges[categoryName].Add(go);
    }

    void ApplyColor(GameObject go, Color color)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (r is LineRenderer || r is TrailRenderer)
                continue;

            if (r.GetComponent<TMP_Text>() != null)
                continue;

            r.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
        }

        foreach (Light l in go.GetComponentsInChildren<Light>())
            l.color = color;
    }

    Color GetColor(string category)
    {
        foreach (CategoryColor c in categoryColors)
        {
            if (string.Equals(
                c.category,
                category,
                StringComparison.OrdinalIgnoreCase))
                return c.color;
        }

        return defaultColor;
    }

    [ContextMenu("Effacer le graphe")]
    public void Clear()
    {
        Transform old = transform.Find("GraphContent");

        if (old != null)
        {
            if (Application.isPlaying)
                Destroy(old.gameObject);
            else
                DestroyImmediate(old.gameObject);
        }
    }
}
