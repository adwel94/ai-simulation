public class ClawAction
{
    public string type;       // move, lower, raise, grip, camera, wait, done, error
    public string reasoning;
    public string direction;  // left, right, forward, backward (for move) / left, right (for camera)
    public string state;      // open, close (for grip)
    public float duration;    // seconds
    public float angle;       // degrees (for camera orbit)
}
