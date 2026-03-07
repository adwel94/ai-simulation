using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum BigHandState { Fixed = 0, MovingUp = 1, MovingDown = -1 };

public class GripperDemoController : MonoBehaviour
{

    public BigHandState moveState = BigHandState.Fixed;
    public float speed = 1.0f;
    public float descendDistance = 0.4f;
    float upperLimit;
    float bottomLimit;

    void Start()
    {
        upperLimit = GetComponent<ArticulationBody>().jointPosition[0];
        bottomLimit = upperLimit + descendDistance;
        Debug.Log($"[Gripper] Start: upperLimit={upperLimit}, bottomLimit={bottomLimit}");
    }

    private void FixedUpdate()
    {
        ArticulationBody articulation = GetComponent<ArticulationBody>();
        float currentPos = articulation.jointPosition[0];

        if (moveState != BigHandState.Fixed)
        {
            float targetPosition = currentPos + -(float)moveState * Time.fixedDeltaTime * speed;

            if (moveState == BigHandState.MovingDown && targetPosition >= bottomLimit)
            {
                targetPosition = bottomLimit;
                moveState = BigHandState.Fixed;
                ZeroJointVelocity(articulation);
            }
            else if (moveState == BigHandState.MovingUp && targetPosition <= upperLimit)
            {
                targetPosition = upperLimit;
                moveState = BigHandState.Fixed;
                ZeroJointVelocity(articulation);
            }

            var drive = articulation.xDrive;
            drive.target = targetPosition;
            articulation.xDrive = drive;
        }
    }

    void ZeroJointVelocity(ArticulationBody articulation)
    {
        var velocities = new List<float>();
        articulation.GetJointVelocities(velocities);
        for (int i = 0; i < velocities.Count; i++) velocities[i] = 0f;
        articulation.SetJointVelocities(velocities);
    }
}
