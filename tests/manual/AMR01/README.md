# 과제2 시연 — 출발해서 경유점을 지나 도착하기

책임님께 이렇게 설명한다.

> 가상 AMR 한 대가 출발 위치에서 경유점을 지나 목적지까지 움직이는 시연입니다. 이동 중에는 속도 1로 움직이고, 목적지에 도착하면 속도 0으로 멈춥니다. 출발 위치와 속도, 경로는 설정 파일로 바꿀 수 있습니다.

## 1. 준비

PowerShell을 열고 아래 두 줄을 차례대로 입력한다. 빌드가 성공한 뒤 다음 단계로 간다.

```powershell
Set-Location C:\amr-control-system-demo
dotnet build .\src\Amr.Simulator --no-restore
```

## 2. 오늘 보여드릴 경로 확인

```powershell
Get-Content .\config\simulator.single.json
```

설정은 **AMR_001, 출발 (-1, 0), 속도 1m/s, 경유점 (1, 0), 목적지 (1, 2)**다. 총 4m를 이동하므로 약 4초가 걸린다. PC의 실행 상황에 따라 로그 시각은 조금 달라진다.

## 3. 한 번 실행해서 출발부터 정지까지 보여드리기

```powershell
dotnet run --no-build --project .\src\Amr.Simulator -- --config .\config\simulator.single.json
```

| 화면에서 찾을 글자 | 보여드릴 내용 | 설명할 말 |
| --- | --- | --- |
| `AmrInitialized` | `X=-1 Y=0`, `IDLE`, 속도 0 | “설정한 출발 위치에 AMR이 준비됐습니다.” |
| `AmrMoved` | 좌표 변화, `BUSY`, 속도 1 | “AMR이 움직이고 있습니다.” |
| `WaypointReached`의 `Index=0` | `X=1 Y=0` | “첫 번째 경유점을 통과했습니다.” |
| 마지막 `SimulationCompleted` | `X=1 Y=2`, `IDLE`, 속도 0 | “목적지에 도착해서 멈췄습니다.” |

`InitializationCompleted` 다음에도 기다리면 이동 로그가 이어진다. 소수점 아래 숫자는 실제 지난 시간에 따른 위치다. `WaypointReached`는 통과한 지점을 표시하며 그 순간의 위치 갱신은 다음 구간까지 진행했을 수 있다.

## 4. 정상 종료 확인

```powershell
$LASTEXITCODE
```

`0`이면 정상 종료다. 여기까지가 책임님께 보여드릴 **하나의 시나리오**다.

> 설정 읽기 → 출발 → 경유점 통과 → 목적지 도착 → 정지까지 실제 실행으로 확인했습니다. 다음에는 이 위치 정보를 관제 프로그램으로 보내는 작업을 진행할 수 있습니다.

## 추가 질문이 있을 때만 보여줄 확인

같은 코드에서 출발 위치·속도·경로를 바꾼 예시:

```powershell
dotnet run --no-build --project .\src\Amr.Simulator -- --config .\evidence\AMR\20261021-runner-console-03\changed.json
```

출발 (5, 3), 속도 2, 경유점 (5, 4), 도착 (7, 4). 약 1.5초 뒤 도착한다.

중간에 사용자가 종료하는 예시:

```powershell
dotnet run --no-build --project .\src\Amr.Simulator -- --config .\evidence\AMR\20261021-runner-console-03\cancel.json
```

`AmrMoved`가 나오면 **Ctrl+C를 한 번** 누른다. `SimulationCanceled`가 나온 뒤 `$LASTEXITCODE`가 0인지 확인한다. 취소 로그의 좌표·상태·속도는 마지막 계산값이며 목적지 도착이나 STOP 명령 수행을 뜻하지 않는다.

잘못된 설정은 아래 실행에서 이동을 시작하지 않고 `ConfigurationRejected`와 이유를 출력한다. 이 경우 `$LASTEXITCODE`는 **1이 정상적인 거부 결과**다.

```powershell
dotnet run --no-build --project .\src\Amr.Simulator -- --config .\evidence\AMR\20261021-runner-console-03\invalid.json
```

## 검증을 다시 실행할 때

```powershell
dotnet test .\tests\Amr.Simulator.Tests --no-restore
pwsh -NoProfile -File .\tests\manual\AMR01\Verify-Run.ps1 -RunId ("runner-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
```

두 번째 명령은 Windows 전용이다. 독립 프로세스로 실제 도착·설정 변경·오류 거부·이동 없는 경로·이동 중 취소·매우 긴 갱신 간격의 취소를 확인하고 원본 로그를 새 폴더에 보관한다. Ctrl+C 신호는 도구가 시작한 프로세스의 콘솔만 확인한 뒤 보낸다.

현재 완료 범위와 자동시험·실제 실행 결과는 [검증결과](../../../evidence/AMR/20261021-runner-01/README.md)에 있다. MQTT 송신·명령 수행·다중 AMR·장애·GUI는 후속 범위다.
