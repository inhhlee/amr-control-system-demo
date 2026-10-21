# 과제2 — 사용자가 직접 실행해 전달한 결과

근거는 이 대화에서 사용자가 PowerShell 실행 후 붙여 넣은 콘솔 출력이다. 아래는 해당 출력의 핵심 줄을 그대로 발췌한 것이다. Codex의 별도 실행 기록과 합치지 않았으며, 원본 로그 파일을 자동 수집한 기록은 아니다. 실행한 정확한 시각과 `$LASTEXITCODE` 값은 전달받지 않아 미확인으로 둔다.

사용자 실행 명령:

```powershell
dotnet run --no-build --project .\src\Amr.Simulator -- --config .\config\simulator.single.json
```

사용자 제공 로그 발췌:

```text
ConfigurationLoaded AmrId=AMR_001 InitialX=-1 InitialY=0 SpeedMps=1 UpdateIntervalMs=100 WaypointCount=2
Waypoint Index=0 X=1 Y=0
Waypoint Index=1 X=1 Y=2
AmrInitialized AmrId=AMR_001 X=-1 Y=0 State=IDLE CurrentSpeedMps=0
InitializationCompleted
SimulationStarted AmrId=AMR_001 X=-1 Y=0 State=IDLE CurrentSpeedMps=0 ElapsedSeconds=0.000
AmrMoved AmrId=AMR_001 X=-0.8973667999999999 Y=0 State=BUSY CurrentSpeedMps=1 ElapsedSeconds=0.103
WaypointReached AmrId=AMR_001 Index=0 X=1 Y=0 ObservedElapsedSeconds=2.055
AmrMoved AmrId=AMR_001 X=1 Y=0.05488590000000006 State=BUSY CurrentSpeedMps=1 ElapsedSeconds=2.055
WaypointReached AmrId=AMR_001 Index=1 X=1 Y=2 ObservedElapsedSeconds=4.064
SimulationCompleted AmrId=AMR_001 X=1 Y=2 State=IDLE CurrentSpeedMps=0 ElapsedSeconds=4.064
```

확인 결과: (-1, 0)에서 출발해 이동 중 BUSY·현재 속도 1을 표시했고, (1, 0)을 통과한 뒤 4.064초에 (1, 2)·IDLE·현재 속도 0으로 완료했다. 경유점의 2.055초는 통과를 관찰한 갱신 시각이다. 100ms는 설정한 대기 간격이며 실행 시각이 매번 정확히 100ms씩 증가한다는 의미는 아니다.

보고 문안:

> 직접 프로그램을 실행해 설정한 경로대로 이동하는 것을 확인했습니다. 목적지에 도착한 뒤 상태가 대기로 바뀌고 속도가 0이 됐습니다. 다음에는 이동 정보를 통신 프로그램으로 보내도록 연결하겠습니다.

- [경로 설정](../../../config/simulator.single.json)
- [이동 계산 코드](../../../src/Amr.Simulator/Simulation/SimulatedAmr.cs)
- [실행 루프 코드](../../../src/Amr.Simulator/Simulation/SimulationRunner.cs)
- [테스트 코드](../../../tests/Amr.Simulator.Tests/RunnerTests.cs)
- [따라 하는 시연 순서](../../../tests/manual/AMR01/README.md)

위 코드 링크는 현재 작업 파일이며 사용자 실행 순간의 별도 해시를 확보한 것은 아니다. Codex가 앞서 실행한 자동시험·실제 콘솔 결과는 [별도 검증결과](../../AMR/20261021-runner-01/README.md)에 있다. 그 실행의 4.020초와 종료 코드 0을 이번 사용자 실행의 값으로 대신 표시하지 않는다.
