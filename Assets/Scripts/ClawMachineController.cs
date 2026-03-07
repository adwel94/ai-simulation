using UnityEngine;

public class ClawMachineController : MonoBehaviour
{
    public float moveSpeed = 4.0f;

    public float minX = -2f;
    public float maxX = 2f;
    public float minZ = -2f;
    public float maxZ = 2f;

    // AI control
    [HideInInspector] public bool isAIControlled = false;
    Vector2 aiMoveInput;

    ArticulationBody artBody;

    void Awake()
    {
        artBody = GetComponent<ArticulationBody>();
    }

    public void SetAIMoveDirection(float x, float z)
    {
        aiMoveInput = new Vector2(x, z);
    }

    public void StopAIMovement()
    {
        aiMoveInput = Vector2.zero;
    }

    void Update()
    {
        float inputX, inputZ;

        if (isAIControlled)
        {
            inputX = aiMoveInput.x;
            inputZ = aiMoveInput.y;
        }
        else
        {
            inputX = Input.GetAxis("Horizontal");
            inputZ = Input.GetAxis("Vertical");
        }

        if (Mathf.Abs(inputX) > 0.01f || Mathf.Abs(inputZ) > 0.01f)
        {
            Vector3 pos = transform.position;
            pos.x += inputX * moveSpeed * Time.deltaTime;
            pos.z += inputZ * moveSpeed * Time.deltaTime;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.z = Mathf.Clamp(pos.z, minZ, maxZ);

            if (artBody != null)
                artBody.TeleportRoot(pos, transform.rotation);
            else
                transform.position = pos;
        }
    }
}
