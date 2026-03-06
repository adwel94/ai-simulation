using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class SceneContextProvider : MonoBehaviour
{
    public GameObject robotRoot;         // UR3
    public PincherController gripper;    // Hand's PincherController

    void Awake()
    {
        // Auto-find references if not set in Inspector
        if (robotRoot == null)
        {
            var rc = FindObjectOfType<RobotController>();
            if (rc != null) robotRoot = rc.gameObject;
        }
        if (gripper == null) gripper = FindObjectOfType<PincherController>();
    }

    static readonly Dictionary<string, string> colorMap = new Dictionary<string, string>
    {
        { "빨강", "red" },
        { "파랑", "blue" },
        { "초록", "green" }
    };

    public string GetSceneContext()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[Scene Context]");

        if (robotRoot != null)
            sb.AppendLine($"Robot base position: ({robotRoot.transform.position.x:F3}, {robotRoot.transform.position.y:F3}, {robotRoot.transform.position.z:F3})");

        if (gripper != null)
            sb.AppendLine($"Gripper state: {(gripper.CurrentGrip() < 0.1f ? "open" : "closed")}");

        List<GameObject> balls = FindBallsInScene();
        if (balls.Count > 0)
        {
            sb.AppendLine("Objects in scene:");
            foreach (GameObject ball in balls)
            {
                Vector3 pos = ball.transform.position;
                string color = ExtractColorFromName(ball.name);
                sb.AppendLine($"- \"{ball.name}\" at ({pos.x:F3}, {pos.y:F3}, {pos.z:F3}), color: {color}");
            }
        }
        else
        {
            sb.AppendLine("No balls in scene.");
        }

        return sb.ToString();
    }

    public GameObject FindBallByName(string name)
    {
        // Try exact match first
        GameObject exact = GameObject.Find(name);
        if (exact != null) return exact;

        // Fuzzy match by Korean color name
        List<GameObject> balls = FindBallsInScene();
        string searchLower = name.ToLower();

        foreach (GameObject ball in balls)
        {
            if (ball.name.ToLower().Contains(searchLower))
                return ball;

            // Match by color keyword
            foreach (var kvp in colorMap)
            {
                if (ball.name.Contains(kvp.Key) &&
                    (searchLower.Contains(kvp.Key) || searchLower.Contains(kvp.Value)))
                    return ball;
            }
        }

        return null;
    }

    public List<GameObject> FindBallsInScene()
    {
        List<GameObject> balls = new List<GameObject>();
        Rigidbody[] rigidbodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);

        foreach (Rigidbody rb in rigidbodies)
        {
            if (rb.gameObject.name.StartsWith("Ball_"))
            {
                balls.Add(rb.gameObject);
            }
        }

        return balls;
    }

    string ExtractColorFromName(string name)
    {
        foreach (var kvp in colorMap)
        {
            if (name.Contains(kvp.Key))
                return kvp.Value;
        }
        return "unknown";
    }
}
