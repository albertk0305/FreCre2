// 여러 시스템이 공유하는 공통 데이터 타입.
// Stage 2에서는 ControlMode만 정의한다. GameState, DifficultySnapshot, GameResult는 Stage 4 이후 추가한다.

/// <summary>플레이어 조작 방식.</summary>
public enum ControlMode
{
    /// <summary>키보드 입력 (WASD/방향키 + Space).</summary>
    PC = 0,

    /// <summary>가상 조이스틱 및 점프 버튼 (Stage 10에서 구현).</summary>
    Mobile = 1,
}
