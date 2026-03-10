using System.Collections.Generic;
using UnityEngine;

public class FruitSpawner : MonoBehaviour, IObjectSpawner
{
    [Header("Prefabs")]
    [Tooltip("10개 과일/채소 프리팹 — 비어있으면 Resources에서 자동 로드")]
    public GameObject[] fruitPrefabs;

    [Header("Object Names (Korean)")]
    public string[] objectNames = {
        "사과", "바나나", "오렌지", "딸기", "수박",
        "양배추", "당근", "오이", "고추", "토마토"
    };

    [Header("Spawn Settings")]
    public int spawnCount = 3;
    public float objectScale = 0.12f;
    public float objectMass = 0.1f;
    public bool autoSpawnOnStart = true;

    [Header("Spawn Area")]
    public Vector2 spawnAreaMin = new Vector2(-1.5f, -1.5f);
    public Vector2 spawnAreaMax = new Vector2(1.5f, 1.5f);
    public float spawnHeight = 0.5f;
    public float minSpacing = 0.3f;

    readonly List<GameObject> spawnedObjects = new List<GameObject>();
    public List<GameObject> SpawnedObjects => spawnedObjects;

    // Resources paths (relative to any Resources folder)
    static readonly string[] RESOURCE_PATHS = {
        "Prefabs/Fruits/Apple",
        "Prefabs/Fruits/Banana",
        "Prefabs/Fruits/Orange",
        "Prefabs/Fruits/Strawberry",
        "Prefabs/Fruits/Watermelon",
        "Prefabs/Vegetables/Cabbage",
        "Prefabs/Vegetables/Carrot",
        "Prefabs/Vegetables/Cucumber",
        "Prefabs/Vegetables/Pepper",
        "Prefabs/Vegetables/Tomato",
    };

    // GUI state
    int selectedIndex = 0;
    bool isPlacingMode = false;
    bool guiClicked = false;
    GameObject previewObj;
    Camera mainCamera;
    Rect guiAreaRect;
    GUIStyle buttonStyle;
    GUIStyle labelStyle;

    void Start()
    {
        mainCamera = Camera.main;
        guiAreaRect = new Rect(0, 0, 320, 200);
        LoadPrefabsIfNeeded();

        if (autoSpawnOnStart)
            SpawnRandom();
    }

    void LoadPrefabsIfNeeded()
    {
        if (fruitPrefabs != null && fruitPrefabs.Length > 0 && fruitPrefabs[0] != null)
            return;

        var loaded = new List<GameObject>();
        foreach (var path in RESOURCE_PATHS)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null)
                loaded.Add(prefab);
            else
                Debug.LogWarning($"<color=yellow>[FruitSpawner]</color> Resource not found: {path}");
        }
        fruitPrefabs = loaded.ToArray();
        Debug.Log($"<color=green>[FruitSpawner]</color> Loaded {fruitPrefabs.Length} prefabs from Resources");
    }

    void OnGUI()
    {
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 18;
            buttonStyle.fontStyle = FontStyle.Bold;
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.yellow;

            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.normal.textColor = Color.yellow;
        }

        // Main spawn button
        string btnText = isPlacingMode ? "배치 취소" : "과일 생성하기";
        if (GUI.Button(new Rect(20, 20, 200, 45), btnText, buttonStyle))
        {
            guiClicked = true;
            if (isPlacingMode)
                CancelPlacing();
            else
                EnterPlacingMode();
        }

        // Object selection buttons (2 rows x 5 columns)
        int cols = 5;
        int btnW = 58;
        int btnH = 30;
        int startY = 75;
        for (int i = 0; i < objectNames.Length && i < (fruitPrefabs?.Length ?? 0); i++)
        {
            int row = i / cols;
            int col = i % cols;
            Rect r = new Rect(20 + col * (btnW + 4), startY + row * (btnH + 4), btnW, btnH);

            GUIStyle style = new GUIStyle(GUI.skin.button);
            style.fontSize = 12;
            style.fontStyle = (i == selectedIndex) ? FontStyle.Bold : FontStyle.Normal;
            style.normal.textColor = (i == selectedIndex) ? Color.yellow : Color.white;

            if (GUI.Button(r, objectNames[i], style))
            {
                guiClicked = true;
                selectedIndex = i;
            }
        }

        if (isPlacingMode)
        {
            GUI.Label(new Rect(20, startY + 2 * (btnH + 4) + 5, 400, 25),
                "마우스 클릭으로 배치하세요 (ESC: 취소)", labelStyle);
        }
    }

    void Update()
    {
        if (!isPlacingMode) return;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CancelPlacing();
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            Vector3 placePos = hit.point + hit.normal * 0.05f;

            if (previewObj != null)
            {
                previewObj.transform.position = placePos;
                previewObj.SetActive(true);
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (guiClicked) { guiClicked = false; return; }
                Vector2 mousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (guiAreaRect.Contains(mousePos)) return;

                PlaceObject(placePos);
            }
        }
        else if (previewObj != null)
        {
            previewObj.SetActive(false);
        }

        guiClicked = false;
    }

    void EnterPlacingMode()
    {
        isPlacingMode = true;
        if (fruitPrefabs != null && selectedIndex < fruitPrefabs.Length && fruitPrefabs[selectedIndex] != null)
        {
            previewObj = Instantiate(fruitPrefabs[selectedIndex]);
            previewObj.transform.localScale = Vector3.one * objectScale;
            previewObj.name = "FruitPreview";
            // Remove physics from preview
            var rb = previewObj.GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);
            var col = previewObj.GetComponent<Collider>();
            if (col != null) Destroy(col);
            // Make semi-transparent
            foreach (var renderer in previewObj.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in renderer.materials)
                {
                    Color c = mat.color;
                    mat.color = new Color(c.r, c.g, c.b, 0.5f);
                }
            }
            previewObj.SetActive(false);
        }
    }

    void CancelPlacing()
    {
        isPlacingMode = false;
        if (previewObj != null) { Destroy(previewObj); previewObj = null; }
    }

    void PlaceObject(Vector3 position)
    {
        if (fruitPrefabs == null || selectedIndex >= fruitPrefabs.Length) return;
        GameObject obj = SpawnSingleObject(selectedIndex, position);
        if (obj != null) spawnedObjects.Add(obj);
        CancelPlacing();
    }

    public void ClearObjects()
    {
        foreach (var obj in spawnedObjects)
        {
            if (obj != null)
            {
                obj.SetActive(false);
                Destroy(obj);
            }
        }
        spawnedObjects.Clear();
    }

    public void SpawnRandom()
    {
        ClearObjects();
        LoadPrefabsIfNeeded();

        if (fruitPrefabs == null || fruitPrefabs.Length == 0)
        {
            Debug.LogWarning("<color=yellow>[FruitSpawner]</color> No prefabs available!");
            return;
        }

        // Pick random indices without replacement
        List<int> indices = new List<int>();
        for (int i = 0; i < fruitPrefabs.Length; i++)
            indices.Add(i);

        // Shuffle
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            int tmp = indices[i];
            indices[i] = indices[j];
            indices[j] = tmp;
        }

        int count = Mathf.Min(spawnCount, fruitPrefabs.Length);
        List<Vector3> positions = new List<Vector3>();

        for (int i = 0; i < count; i++)
        {
            int idx = indices[i];
            Vector3 pos = GetRandomSpawnPosition(positions);
            positions.Add(pos);

            GameObject obj = SpawnSingleObject(idx, pos);
            if (obj != null) spawnedObjects.Add(obj);
        }
    }

    GameObject SpawnSingleObject(int idx, Vector3 pos)
    {
        if (fruitPrefabs[idx] == null) return null;

        GameObject obj = Instantiate(fruitPrefabs[idx]);
        obj.transform.position = pos;
        obj.transform.localScale = Vector3.one * objectScale;
        obj.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        string displayName = (idx < objectNames.Length) ? objectNames[idx] : fruitPrefabs[idx].name;
        obj.name = "Fruit_" + displayName;

        // Add physics components
        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb == null) rb = obj.AddComponent<Rigidbody>();
        rb.mass = objectMass;
        rb.useGravity = true;
        rb.linearDamping = 2f;
        rb.angularDamping = 2f;

        // Ensure collider exists
        Collider col = obj.GetComponent<Collider>();
        if (col == null)
        {
            MeshCollider mc = obj.AddComponent<MeshCollider>();
            mc.convex = true;
        }

        // Physics material
        var physicMat = new PhysicsMaterial("FruitPhysics");
        physicMat.staticFriction = 0.8f;
        physicMat.dynamicFriction = 0.6f;
        physicMat.frictionCombine = PhysicsMaterialCombine.Maximum;
        obj.GetComponent<Collider>().material = physicMat;

        Debug.Log($"<color=green>[FruitSpawner]</color> Spawned {displayName} at {pos}");
        return obj;
    }

    Vector3 GetRandomSpawnPosition(List<Vector3> existing)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            float x = Random.Range(spawnAreaMin.x, spawnAreaMax.x);
            float z = Random.Range(spawnAreaMin.y, spawnAreaMax.y);
            Vector3 candidate = new Vector3(x, spawnHeight, z);

            bool tooClose = false;
            foreach (var p in existing)
            {
                if (Vector3.Distance(candidate, p) < minSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (!tooClose) return candidate;
        }
        return new Vector3(
            Random.Range(spawnAreaMin.x, spawnAreaMax.x),
            spawnHeight,
            Random.Range(spawnAreaMin.y, spawnAreaMax.y)
        );
    }
}
