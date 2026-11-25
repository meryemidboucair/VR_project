
using UnityEngine;

public class AnimalNodeData : MonoBehaviour
{
    public string speciesName;
    public string category;

    [TextArea]
    public string info;

    public bool isCategory;
    public bool isExpanded;
}
