using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// helper methods / extensions
public static class Vector3Ext
{
    public static Vector3Int forward = new Vector3Int(0, 0, 1);
    public static Vector3Int back = new Vector3Int(0, 0, -1);
    public static Vector3Int Int(this Vector3 vec)
    {
        return new Vector3Int((int)vec.x, (int)vec.y, (int)vec.z);
    }
    public static Vector3 Float(this Vector3Int vec)
    {
        return new Vector3(vec.x, vec.y, vec.z);
    }
    public static Vector3Int Floor(this Vector3 vec)
    {
        return new Vector3Int(Mathf.FloorToInt(vec.x), Mathf.FloorToInt(vec.y), Mathf.FloorToInt(vec.z));
    }
}
