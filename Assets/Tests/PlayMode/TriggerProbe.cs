using UnityEngine;

/// <summary>테스트 전용: Player에서 Missile과의 Trigger 콜백이 실제로 발생하는지 센다.</summary>
public class TriggerProbe : MonoBehaviour
{
    public int EnterCount;
    public int StayCount;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<Missile>() != null) EnterCount++;
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.GetComponentInParent<Missile>() != null) StayCount++;
    }
}
