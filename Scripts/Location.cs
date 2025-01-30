using UnityEngine;

[CreateAssetMenu(fileName = "Data", menuName = "ScriptableObjects/Location", order = 1)]
public class Location : ScriptableObject
{
    public string LocationName;

    public string LocationDescription;

    public Location[] Connections;
}
