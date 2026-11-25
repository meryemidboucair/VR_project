using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class Edge : MonoBehaviour
{
    [Header("Noeuds")]
    public Transform nodeA;
    public Transform nodeB;

    [Header("Connexion")]
    public float maxDistance = 2.0f;
    public bool ignoreZ = false;

    [Header("Apparence")]
    public float width = 0.03f;
    public Color lineColor = Color.white;

    private LineRenderer line;

    void OnEnable()
    {
        Setup();
    }

    void OnValidate()
    {
        Setup();
    }

    void Setup()
    {
        if (line == null) line = GetComponent<LineRenderer>();
        if (line == null) return;

        line.positionCount = 2;
        line.useWorldSpace = true;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = lineColor;
        line.endColor = lineColor;

        if (line.sharedMaterial == null)
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    void LateUpdate()
    {
        if (line == null) Setup();
        if (line == null) return;

        if (nodeA == null || nodeB == null)
        {
            line.enabled = false;
            return;
        }

        Vector3 a = nodeA.position;
        Vector3 b = nodeB.position;

        if (ignoreZ)
        {
            a.z = 0f;
            b.z = 0f;
        }

        bool connected = (a - b).sqrMagnitude <= maxDistance * maxDistance;
        line.enabled = connected;

        if (connected)
        {
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }
    }
}