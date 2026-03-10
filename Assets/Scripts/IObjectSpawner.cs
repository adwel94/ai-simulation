using System.Collections.Generic;
using UnityEngine;

public interface IObjectSpawner
{
    List<GameObject> SpawnedObjects { get; }
    void SpawnRandom();
    void ClearObjects();
}
