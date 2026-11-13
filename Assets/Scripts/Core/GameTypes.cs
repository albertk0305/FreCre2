// 여러 시스템이 공유하는 공통 데이터 타입.
// Stage 2: ControlMode, Stage 4: GameState. DifficultySnapshot, GameResult는 Stage 5 이후 추가한다.

/// <summary>게임 진행 상태. GameManager가 소유하고 변경한다.</summary>
public enum GameState
{
    /// <summary>게임 시작 전 준비.</summary>
    Ready = 0,

    /// <summary>게임 진행 중.</summary>
    Playing = 1,

    /// <summary>게임 종료.</summary>
    GameOver = 2,
}

/// <summary>플레이어 조작 방식.</summary>
public enum ControlMode
{
    /// <summary>키보드 입력 (WASD/방향키 + Space).</summary>
    PC = 0,

    /// <summary>가상 조이스틱 및 점프 버튼 (Stage 10에서 구현).</summary>
    Mobile = 1,
}
