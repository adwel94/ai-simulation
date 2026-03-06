using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ActionSequencer : MonoBehaviour
{
    [Header("References")]
    public RobotController robotController;
    public PincherController pincherController;
    public SceneContextProvider sceneContext;

    [Header("IK Joint References")]
    public Transform baseJoint;      // Joint 0
    public Transform shoulderJoint;  // Joint 1
    public Transform elbowJoint;     // Joint 2
    public Transform wrist1Joint;    // Joint 3

    [Header("Timing")]
    public float jointMoveTimeout = 5.0f;
    public float angleThreshold = 3.0f;
    public float aboveOffset = 0.1f;

    // IK geometry (auto-computed)
    float upperArmLength;
    float forearmLength;
    Vector3 basePosition;
    float shoulderHeight;

    bool isExecuting = false;
    Coroutine currentSequence;

    public bool IsExecuting => isExecuting;

    // Events
    public event Action<string> OnStatusChanged;
    public event Action OnSequenceComplete;
    public event Action<string> OnSequenceError;

    void Awake()
    {
        // Auto-find references if not set in Inspector
        if (robotController == null) robotController = FindObjectOfType<RobotController>();
        if (pincherController == null) pincherController = FindObjectOfType<PincherController>();
        if (sceneContext == null) sceneContext = FindObjectOfType<SceneContextProvider>();
    }

    void Start()
    {
        ComputeLinkLengths();
    }

    void ComputeLinkLengths()
    {
        if (baseJoint == null || shoulderJoint == null || elbowJoint == null || wrist1Joint == null)
        {
            Debug.LogWarning("[ActionSequencer] Joint references not set. Using defaults.");
            basePosition = new Vector3(0, 0.762f, 0);
            shoulderHeight = 0.1f;
            upperArmLength = 0.244f;
            forearmLength = 0.213f;
            return;
        }

        basePosition = baseJoint.position;
        shoulderHeight = shoulderJoint.position.y - basePosition.y;
        upperArmLength = Vector3.Distance(shoulderJoint.position, elbowJoint.position);
        forearmLength = Vector3.Distance(elbowJoint.position, wrist1Joint.position);

        Debug.Log($"[ActionSequencer] IK Geometry - Base: {basePosition}, ShoulderH: {shoulderHeight:F3}, L1: {upperArmLength:F3}, L2: {forearmLength:F3}");
    }

    public void ExecuteActions(List<RobotAction> actions)
    {
        if (isExecuting)
        {
            OnSequenceError?.Invoke("이미 실행 중입니다.");
            return;
        }
        currentSequence = StartCoroutine(ExecuteSequence(actions));
    }

    public void CancelExecution()
    {
        if (currentSequence != null)
        {
            StopCoroutine(currentSequence);
            robotController.StopAllJointRotations();
            pincherController.gripState = GripState.Fixed;
            isExecuting = false;
            OnStatusChanged?.Invoke("취소됨");
        }
    }

    IEnumerator ExecuteSequence(List<RobotAction> actions)
    {
        isExecuting = true;

        for (int i = 0; i < actions.Count; i++)
        {
            RobotAction action = actions[i];
            string status = $"[{i + 1}/{actions.Count}] {GetActionDescription(action)}";
            OnStatusChanged?.Invoke(status);
            Debug.Log($"[ActionSequencer] {status}");

            switch (action.type)
            {
                case "move_above":
                    yield return ExecuteMoveTo(action, aboveOffset);
                    break;
                case "move_to":
                    yield return ExecuteMoveTo(action, 0f);
                    break;
                case "move_home":
                    yield return ExecuteMoveHome();
                    break;
                case "gripper":
                    yield return ExecuteGripper(action);
                    break;
                case "wait":
                    yield return new WaitForSeconds(action.seconds > 0 ? action.seconds : 0.5f);
                    break;
                default:
                    Debug.LogWarning($"[ActionSequencer] Unknown action: {action.type}");
                    break;
            }
        }

        isExecuting = false;
        OnStatusChanged?.Invoke("완료!");
        OnSequenceComplete?.Invoke();
    }

    IEnumerator ExecuteMoveTo(RobotAction action, float heightOffset)
    {
        if (string.IsNullOrEmpty(action.target))
        {
            OnSequenceError?.Invoke("이동 대상이 지정되지 않았습니다.");
            yield break;
        }

        GameObject ball = sceneContext.FindBallByName(action.target);
        if (ball == null)
        {
            OnSequenceError?.Invoke($"대상을 찾을 수 없습니다: {action.target}");
            yield break;
        }

        Vector3 targetPos = ball.transform.position;
        targetPos.y += heightOffset;

        float[] angles = SolveIK(targetPos);
        if (angles == null)
        {
            OnSequenceError?.Invoke($"도달할 수 없는 위치입니다: {targetPos}");
            yield break;
        }

        yield return MoveToAngles(angles);
    }

    IEnumerator ExecuteMoveHome()
    {
        float[] homeAngles = { 0f, 0f, 0f, 0f, 0f, 0f };
        yield return MoveToAngles(homeAngles);
    }

    IEnumerator MoveToAngles(float[] targetAngles)
    {
        // Stop manual input rotations first
        robotController.StopAllJointRotations();

        // Set all joint targets
        int count = Mathf.Min(targetAngles.Length, robotController.JointCount);
        for (int i = 0; i < count; i++)
        {
            robotController.SetJointAngle(i, targetAngles[i]);
        }

        // Wait until joints reach targets or timeout
        float elapsed = 0f;
        while (elapsed < jointMoveTimeout)
        {
            bool allReached = true;
            for (int i = 0; i < count; i++)
            {
                float current = robotController.GetJointAngle(i);
                if (Mathf.Abs(Mathf.DeltaAngle(current, targetAngles[i])) > angleThreshold)
                {
                    allReached = false;
                    break;
                }
            }
            if (allReached) yield break;

            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }

        Debug.LogWarning("[ActionSequencer] Joint move timed out");
    }

    IEnumerator ExecuteGripper(RobotAction action)
    {
        if (action.state == "open")
        {
            pincherController.gripState = GripState.Opening;
            float elapsed = 0f;
            while (pincherController.CurrentGrip() > 0.05f && elapsed < 3f)
            {
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            pincherController.gripState = GripState.Fixed;
        }
        else if (action.state == "close")
        {
            pincherController.gripState = GripState.Closing;
            float elapsed = 0f;
            float lastGrip = 0f;
            int stableFrames = 0;

            while (elapsed < 3f)
            {
                float currentGrip = pincherController.CurrentGrip();

                // Fully closed or grip stabilized (blocked by ball)
                if (currentGrip > 0.95f)
                    break;
                if (Mathf.Abs(currentGrip - lastGrip) < 0.001f)
                    stableFrames++;
                else
                    stableFrames = 0;

                if (stableFrames > 20)
                    break;

                lastGrip = currentGrip;
                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            pincherController.gripState = GripState.Fixed;
        }
    }

    // Simple 2D Geometric IK
    float[] SolveIK(Vector3 worldTarget)
    {
        float[] angles = new float[6];

        // 1. Base rotation (around Y axis)
        float dx = worldTarget.x - basePosition.x;
        float dz = worldTarget.z - basePosition.z;
        angles[0] = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;

        // 2. Project into arm's vertical plane
        float r = Mathf.Sqrt(dx * dx + dz * dz);
        float h = worldTarget.y - (basePosition.y + shoulderHeight);

        // 3. Two-link planar IK (shoulder + elbow)
        float L1 = upperArmLength;
        float L2 = forearmLength;
        float distSq = r * r + h * h;
        float dist = Mathf.Sqrt(distSq);

        // Reachability check
        if (dist > L1 + L2 || dist < Mathf.Abs(L1 - L2))
        {
            Debug.LogWarning($"[ActionSequencer] IK unreachable: dist={dist:F3}, L1+L2={L1 + L2:F3}");
            return null;
        }

        float cosElbow = (distSq - L1 * L1 - L2 * L2) / (2f * L1 * L2);
        cosElbow = Mathf.Clamp(cosElbow, -1f, 1f);
        float elbowAngle = Mathf.Acos(cosElbow);

        float shoulderAngle = Mathf.Atan2(h, r) - Mathf.Atan2(L2 * Mathf.Sin(elbowAngle), L1 + L2 * Mathf.Cos(elbowAngle));

        angles[1] = shoulderAngle * Mathf.Rad2Deg;
        angles[2] = elbowAngle * Mathf.Rad2Deg;

        // 4. Wrist angles to maintain downward gripper orientation
        angles[3] = -90f - angles[1] - angles[2];
        angles[4] = 0f;
        angles[5] = 0f;

        Debug.Log($"[ActionSequencer] IK Solution - Base:{angles[0]:F1} Shoulder:{angles[1]:F1} Elbow:{angles[2]:F1} Wrist1:{angles[3]:F1}");

        return angles;
    }

    string GetActionDescription(RobotAction action)
    {
        switch (action.type)
        {
            case "move_above": return $"{action.target} 위로 이동";
            case "move_to": return $"{action.target} 위치로 이동";
            case "move_home": return "홈 포지션으로 복귀";
            case "gripper": return action.state == "open" ? "그리퍼 열기" : "그리퍼 닫기";
            case "wait": return $"{action.seconds}초 대기";
            default: return action.type;
        }
    }
}
