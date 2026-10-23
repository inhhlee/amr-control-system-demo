# 같은 AMR의 경유점 순차 이동 · 장애물 우회 · MQTT 수신 지도

AMR 한 대가 **S (0,0) → W1 (2,3) → W2 (4,3) → G (4,5)** 순서로 이동한다. 장애물이 생기면 현재 위치에서 **현재 순서의 목표까지** 다익스트라 최단 경로를 다시 찾는다. 우회 중 뒤 순서의 경유점을 먼저 지나도 해당 경유점을 완료로 처리하지 않는다.

실제 이동 좌표를 MQTT로 보내고, 같은 지도 서버 안의 기존 관제 **구독·메시지 검사·AMR별 큐**가 받은 좌표로 AMR과 이동 흔적을 표시한다. 화면의 경유 진행·다음 목표·경로는 시뮬레이터 계산값이며 현재 AMR 위치는 수신값이다.

## 코드 역할

이동·경유점·장애물 우회·이동 타이머·MQTT 송신은 [`src/Amr.Simulator/Navigation`](../../src/Amr.Simulator/Navigation)이 담당한다. 이 도구는 그 Simulator를 호출하는 웹 호스트다.

| 위치 | 역할 |
| --- | --- |
| `Program.cs` | HTTP 요청과 화면 제공, 같은 AMR의 출발·취소·장애물 변경 전달 |
| `Control/GridMapSession.cs` | Simulator 실행 요청, 관제 수신 상태·흔적·로그, 최종 정지 좌표 수신 확인 |
| `Control/GridControlConnection.cs` | 수신 Client의 구독·해제. 발행 Client는 Simulator가 별도로 소유 |
| `Control/GridMapCoordinates.cs` | 받은 X/Y 좌표를 지도 칸으로 변환·범위 검사 |
| `Control/GridMissionSettings.cs` | 웹 포트를 포함한 시연 설정 로드와 시작 시점 설정 복사 |
| `MqttMapReceiver.cs`, `MqttMapProjection.cs` | 기존 통신 라이브러리를 통한 관제 수신과 수신 모델 보관 |
| `wwwroot` | HTML·CSS·`grid.js`로 지도·버튼·수신 좌표 표시 |

현재 화면에서 사용하지 않는 이전 `MapSession.cs`·`app.js`는 제거했다. 이전 실행 증거는 그대로 보존한다.

<a id="grid-navigation"></a>

## 실행과 사용

이미 지도 서버가 실행 중이면 해당 서버를 먼저 종료한 뒤 빌드한다. Broker는 프로젝트의 기존 설정을 사용하며 현재 관리 위치는 `docker/compose.yaml`, 접속 주소는 `127.0.0.1:1884`다.

```powershell
dotnet build tools/Amr.Simulator.Map
dotnet run --no-build --project tools/Amr.Simulator.Map
```

[지도 열기](http://127.0.0.1:59344/). 설정 파일에서 지도 포트를 바꾸거나 `--port 0`을 사용하면 출력된 주소를 연다. 기존 `/?mode=grid` 주소도 같은 통합 화면이다. 기본 설정은 MQTT를 사용한다. 파일의 `mqtt.enabled`를 false로 바꾸면 직접 연결 모드로 시험할 수 있다.

1. **출발**: W1 → W2 → G를 순서대로 방문한다. 기본 맵은 9회 이동한다.
2. 출발 직후 **(2,2)**를 클릭: 아래 통로로 우회하며 W2를 먼저 지나도 W1으로 올라간 뒤 W2로 다시 돌아온다. 총 15회 이동해 도착한다.
3. **W2 (4,3)**를 미리 막고 출발: W1은 먼저 방문하고, W2로 갈 수 없어 W1 위치에서 대기한다. W2 장애물을 제거하면 다시 출발해 나머지 순서를 마친다.
4. 주황색 장애물을 다시 클릭하면 제거된다. 현재 AMR 칸·출발점·최종 목적지·고정 벽은 편집할 수 없다. 중간 경유점 칸은 막을 수 있으며, 그 칸을 목표로 삼는 동안에는 건너뛰지 않고 대기한다.
5. **이동 중단**은 같은 AMR의 실행을 중단한다. **다시 출발**은 추가 장애물을 유지하고 출발점부터 경유 순서를 다시 시작한다. **원래 지도로 초기화**는 송수신 종료를 기다리고 장애물·진행·수신 위치를 비운다.

밝은 녹색 칸은 현재 목표까지의 최단 경로다. 오른쪽에 현재 목표·그 목표까지의 거리·경유 진행을 표시한다. 중간 경유점에서는 다음 경로가 열려 있으면 계속 이동하고, 모든 경유점을 방문한 최종 목적지에서 정지한다.

## 지도·이동·MQTT 설정

지도 도구는 **`config/map-demo/settings.json`**을 읽는다. 사용자가 바꾸는 지도·AMR ID·출발점·경유점·이동 간격·지도 포트·MQTT 접속과 주기를 이 폴더에 모았다. [전체 항목·수정·재시작 안내](../../config/map-demo/README.md)를 따른다. 아래는 경유점 부분만 발췌한 예다. 좌표는 0부터 시작하는 **(행, 열)**이며 `waypoints` 마지막 항목이 최종 목적지다.

```json
{
  "amrId": "AMR_001",
  "start": { "row": 0, "col": 0 },
  "waypoints": [
    { "row": 2, "col": 3 },
    { "row": 4, "col": 3 },
    { "row": 4, "col": 5 }
  ]
}
```

다른 설정은 `--config <경로>`로 지정한다. 설정 파일의 `map`은 0/1 직사각 배열, `stepIntervalMs`는 양의 정수, `webPort`는 0~65535다. `mqtt`를 쓰면 enabled·host·port·telemetryMs·heartbeatMs를 모두 명시한다. `--port`와 기존 MQTT 명령행 옵션 묶음은 파일보다 우선한다. 세 임무 필드와 각 좌표의 row·col은 필수이고 알 수 없는 필드·중복 필드·빈 경유점·소수/문자열 좌표·맵 범위 밖·고정 벽 위의 좌표는 시작 전에 거부한다. UTF-8 BOM 유무를 지원하고 잘못된 UTF-8은 거부한다. 같은 위치의 연속 경유점은 추가 이동 없이 순서대로 방문 처리한다.

서버가 시작할 때 한 번 읽은 동일한 설정 바이트를 증거 폴더 `config.json`에 복사하고 그 입력을 계속 사용한다. 실제 MQTT 적용값은 `effective-settings.json`에 남긴다. 파일 수정에는 재빌드가 필요 없고 서버를 다시 시작해야 한다. 화면 초기화는 파일을 다시 읽지 않고 시작 때 읽은 지도로 돌아온다. 실행·초기화·우회 중에도 `amrId`는 유지된다. 같은 Broker에서 같은 ID로 다른 프로그램이 동시에 위치를 발행하지 않는 단일 AMR 시연 조건을 사용한다. 기존 `config/simulator.grid.json`도 호환 입력으로 남긴다. 새 선택 항목을 생략한 이전 형식은 종전의 5×6·600ms·포트5088·MQTT 미사용 기본값을 유지한다.

기존 제품 CLI `src/Amr.Simulator`의 연속 X/Y 좌표 설정인 `config/simulator.single.json`은 그대로 유지한다. 지도 도구의 `--config`는 위 격자 임무 형식을 사용한다. 두 설정을 자동으로 변환하지 않는다.

## 이동·MQTT·지도 계약

- 기본 5×6 맵과 이동 간격600ms는 설정 파일에서 바꿀 수 있다. 화면 칸 수·장애물·수신 좌표 범위도 설정 지도 크기를 따른다. 상하좌우 이동 비용1이며 경로가 없으면 같은 목표를 유지하고 대기하며 길이 열리면 재출발한다.
- 시연용 1칸=1m, MQTT `X=열`, `Y=-행`. 기본 최종 Telemetry는 `(5,-4)`이고 지도에는 `(4,5)`로 표시한다. 이동 중 명목 속도는 `1000 / stepIntervalMs` m/s(기본1/0.6), 대기·도착은 IDLE·속도0이다. 실제 연속 이동·가감속 모델은 아니다.
- Topic `amr/{amrId}/telemetry`, `amr/{amrId}/heartbeat`, 기존 JSON 계약, MQTT5·QoS0·Retain=false. 관제 구독 완료 뒤 별도 Client로 발행한다. 위치 변경마다 보내고 대기 중에도 Telemetry·Heartbeat 주기를 유지한다.
- 첫 수신 전에는 AMR 표시가 없다. 현재 위치·이동 흔적은 수신 모델만 사용하며 계산·발행 값으로 대체하지 않는다. 최종 발행과 같은 시각·좌표·IDLE·속도0을 수신하고 연결 정리까지 끝나야 완료로 표시한다. 3초 최종 수신 대기는 도구용 확인 제한이다.
- 최초 접속 실패는 이동0·수신 위치 없음, 연결 후 단절은 이동 정지·마지막 수신 위치다. 취소 중·초기화 중에는 이전 연결이 끝날 때까지 새 실행을 거부한다. 취소 직전 수신 모델의 BUSY는 실제 로봇 STOP 확인이 아니다. 자동 재접속·재전송은 없다.
- Simulator의 잠금이 편집·이동·경유 순서를 보호하고, Map의 별도 잠금이 관제 수신·표시 상태를 보호한다. Simulator는 잠금을 해제한 뒤 Map에 이벤트를 전달하고 네트워크 I/O도 잠금 밖에서 기다린다. 단일 Simulator 실행 루프가 이동과 발행 순서를 소유하므로 발행 지연은 다음 이동도 늦출 수 있다.

화면과 서버에 경유점용·우회용 별도 AMR을 만들지 않는다. `/api/state`·`/api/grid/state`, `/api/start`·`/api/grid/start`, 두 cancel 경로도 모두 같은 세션에 연결된다. 기존 URL을 사용해 두 번째 AMR을 시작할 수 없다.

## 검증과 증거

[2026-10-23 Simulator 책임 분리 검증](../../evidence/AMR/20261023-simulator-navigation-01/README.md): Simulator 197건·Map 63건 자동 시험, 실제 MQTT 15개 검증 항목, 실제 브라우저의 이동 중 장애물 추가·15회 최단 우회·수신 좌표 표시를 확인했다.

```powershell
dotnet test tests/Amr.Simulator.Map.Tests/Amr.Simulator.Map.Tests.csproj
pwsh -NoProfile -File tests/manual/AMR01/Start-MqttMap.ps1 -RunId mission-host-new
pwsh -NoProfile -File tests/manual/AMR01/Verify-MapConfiguration.ps1 -SessionRunId mission-host-new -RunId settings-check-new
pwsh -NoProfile -File tests/manual/AMR01/Verify-GridMqttMap.ps1 -SessionRunId mission-host-new -RunId mission-check-new
pwsh -NoProfile -File tests/manual/AMR01/Stop-MqttMap.ps1 -RunId mission-host-new
```

실제 검증은 별도 서버·격리 Broker를 사용하고 마지막에 소유 Broker만 중단한다. 사용자 시연 서버와 섞지 않는다. 설정 복사본, 경유 방문 순서·실제 이동·발행 Payload·관제 수신 모델은 서버 증거 폴더의 `config.json`과 `grid-navigation.jsonl`에 남는다.

[설정 분리·최종 수신 표시 수정 검증 결과](../../evidence/AMR/20261022-editable-settings-01/README.md). [이전 통합 검증 결과](../../evidence/AMR/20261022-waypoint-unified-01/README.md)·[두 모드 MQTT 결과](../../evidence/AMR/20261022-grid-mqtt-01/README.md)와 기존 `Verify-Map*`·`Verify-MqttMap`·`Verify-GridNavigation`은 각 단계 당시 입력·API의 이력이다. 현재 통합 임무의 재검증은 위 명령을 따른다.

명령/ACK·실제 로봇·센서·제동·Core/DB·전체 관제 GUI·여러 AMR의 충돌·대규모/장시간 운용은 이번 범위가 아니다.
