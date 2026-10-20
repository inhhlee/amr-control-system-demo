# COM03-C3 응답 차단 시험 도구

`ResponseGateProxy.cs`는 한 Client 세션만 받는 루프백 TCP 프록시다. `Start-ResponseGateProxy.ps1`이 설치된 PowerShell의 .NET으로 소스를 메모리에서 컴파일한다. 별도 SDK 프로젝트·패키지·서비스 설치는 없다. 실제 제품의 MQTTnet 콘솔을 그대로 연결한다.

## 준비와 자동 실행

Windows, PowerShell 7.5 이상, NetTCPIP 모듈, SDK `global.json` 기준 설치, 실행 중인 Docker Linux Engine과 설치된 `eclipse-mosquitto:2.1.2-alpine` 이미지가 필요하다. 저장소 루트에서 실행한다.

```powershell
pwsh -NoProfile -File tests/manual/COM03-C/Verify-ResponseGate.ps1 -RunId <새-실행ID>
```

실행 ID는 영문·숫자·`-`·`_` 1~80자다. 기존 ID는 덮어쓰지 않는다. 도구는 기존 C1·C2의 숨김 격리 콘솔 제어와 Broker 준비 방식을 참고해 작성했다. 기존 실행 파일·프로젝트 내 프록시를 검색했으나 재사용할 도구를 찾지 못해 최소 TCP 전달기를 추가했다. C:/practice의 대형 도구는 복사하지 않았다.

1. 콘솔을 빌드하고 기존 컨테이너 상태와 소스·바이너리 해시를 기록한다.
2. `amr-c3a-<실행ID>` Broker를 새 루프백 임시 포트에 만든다. 실행별 설정만 읽기 전용 mount하고 persistence를 사용하지 않는다.
3. 별도 루프백 임시 포트의 프록시와 새 콘솔을 실행한다. 콘솔의 Connected·Subscribed·Client ID와 양방향 전달을 확인한다.
4. Broker→Client 응답만 차단한다. 실제 응답 바이트 폐기와 Client→Broker 전달 지속, 프록시 양쪽·제품의 Established 소켓을 확인한다.
5. 응답 전달을 재개하고 생성한 콘솔에만 CTRL_C_EVENT를 보낸다. 구독 해제·해제·Dispose·종료 0을 확인한다. Client EOF로 프록시도 종료한다.
6. 시험 컨테이너를 정리하고 기존 컨테이너 상태 보존을 확인한다. 재시작 시험은 새 실행 ID로 같은 명령을 실행한다.

9초는 도구가 응답 폐기를 관찰하는 상한이다. 제품의 무응답 감지 제한이 아니다. C3a는 응답 폐기를 확인한 뒤 전달을 재개하고 정상 종료한다. 무응답 감지 시간·허용 오차·비정상 종료 판정은 C3b에서 별도로 수행한다.

## C3b 무응답 감지 시간 측정

같은 도구에 `-MeasureTimeout`을 주면 응답을 재개하거나 Ctrl+C를 보내지 않는다. 제품이 스스로 단절을 기록하고 정리·종료 1로 끝나야 한다. `-BlockDelaySeconds`는 접속·구독 확인 뒤 차단 요청까지의 도구 대기(0~2초)이며 제품 정책을 변경하지 않는다.

```powershell
pwsh -NoProfile -File tests/manual/COM03-C/Verify-ResponseGate.ps1 -RunId <새-측정ID-1> -MeasureTimeout -BlockDelaySeconds 0
pwsh -NoProfile -File tests/manual/COM03-C/Verify-ResponseGate.ps1 -RunId <새-측정ID-2> -MeasureTimeout -BlockDelaySeconds 2
```

각 실행은 장애 전에 `criteria.json`을 저장한다. 적용 패키지 5.2.0.1603의 nuspec repository commit은 `14463d1ef9f1f119514185c1832905b91bff827d`다. [해당 MQTTnet 소스](https://github.com/dotnet/MQTTnet/blob/14463d1ef9f1f119514185c1832905b91bff827d/Source/MQTTnet/MqttClient.cs#L935)에서 KeepAlive 검사 250ms·PING 응답 대기 K/2를 확인했다. 현재 K=5초, 무업무메시지 조건으로 예상 시간을 산정한다.

| 기준 | 사전 적용 범위 |
| --- | --- |
| 마지막 Client→Broker 전달에서 감지까지 | 명목 7.5초, 조기 허용 100ms·검사 간격 250ms·스케줄링/콜백 여유 1초를 포함해 7.4~8.75초 |
| 첫 실제 응답 폐기에서 감지까지 | 명목 2.5초, 허용 2.4~3.5초 |
| block ACK에서 감지까지 | 위 7.4~8.75초에서 `block ACK - 마지막 송신`의 실제 위상 차이를 뺀 범위. 위상은 3초 이하여야 함 |
| 보조 검사 | 폐기→Client EOF 단조 시간 2.4~3.5초, 측정 벽시계/Stopwatch 경과 차이 100ms 이내 |
| 종료·도구 상한 | 장애 관찰 watchdog 15초·감지 후 프로세스 종료 관찰 5초. 상한 초과 시 실패·생성 자원 정리 |

장애 시작은 명령 파일 생성 시각 대신 프록시 `ControlApplied(block)` ACK다. 마지막 송신은 프록시의 직전 Client→Broker 전달이며 라이브러리 내부 Send 시각과의 작은 차이를 허용 오차에 포함한다. 감지는 제품 ConnectionLost의 UTC 시각이다. 하네스는 명령 요청 전 Stopwatch를 시작해 감지 로그를 관찰한 구간도 보존한다. TCP 폐기→EOF에는 같은 프록시 Stopwatch를 사용한다. 제품의 내부 시계는 변경하지 않는다.

`measurement.json`에 위상·사전 허용 범위·실제 시간·종료·시계 비교를 남긴다. 차단 중 TCP 유지·반대 방향 전달을 확인하고, 첫 EOF가 Client 쪽에서 온 경우와 Broker가 여전히 실행 중인 경우만 통과로 판정한다. 라이브러리가 소켓을 닫은 뒤 ConnectionLost 콜백을 전달하므로 EOF가 콜백보다 조금 먼저 기록될 수 있다. 제품 감지 전에 프록시를 종료하는 시험으로 대체하지 않는다.

두 위상 시험과 기존 C3a 모드 회귀 결과는 [C3b 검증결과](../../evidence/COM-03/20261020-c3b-noresponse-02/README.md)에 있다. 이 수치는 현재 버전·설정·루프백 실습의 허용 범위이며 보편적인 KeepAlive 배수·최초 접속 3초 제한·운영 SLA로 적용하지 않는다.

## 프록시 직접 제어

별도로 준비한 **격리 시험 Broker**의 포트와 새 빈 증거 폴더를 지정한다. 포트는 `proxy.ready.json`에 기록된다. 첫 Client를 받은 뒤 listener는 닫히므로 새 세션에는 새 프록시 프로세스가 필요하다.

```powershell
# 별도 PowerShell에서 시작. 아래 값은 실제 준비한 격리 자원으로 바꾼다.
pwsh -NoProfile -File tools/COM-03/Start-ResponseGateProxy.ps1 -BrokerPort <격리-Broker포트> -RunDirectory <새-빈-증거폴더>

# 제어용 PowerShell: 각 명령은 증가하는 정수 + block/resume/stop이다.
$c3Directory = '<위-증거폴더>'
'1 block' | Set-Content -LiteralPath "$c3Directory/proxy.control.tmp" -Encoding ascii
Move-Item -LiteralPath "$c3Directory/proxy.control.tmp" -Destination "$c3Directory/proxy.control" -Force
# proxy.jsonl의 ControlApplied sequence=1을 확인한 뒤 차단 상태로 판단한다.
'2 resume' | Set-Content -LiteralPath "$c3Directory/proxy.control.tmp" -Encoding ascii
Move-Item -LiteralPath "$c3Directory/proxy.control.tmp" -Destination "$c3Directory/proxy.control" -Force
'3 stop' | Set-Content -LiteralPath "$c3Directory/proxy.control.tmp" -Encoding ascii
Move-Item -LiteralPath "$c3Directory/proxy.control.tmp" -Destination "$c3Directory/proxy.control" -Force
```

`block`은 TCP 소켓을 닫지 않고 응답 바이트를 읽어 폐기한다. `resume`은 이후 응답부터 전달하며 폐기한 응답을 재생하지 않는다. 명령 ACK와 응답 쓰기는 같은 SemaphoreSlim으로 직렬화하여 block ACK 이후 응답 전달이 끼어들지 않게 한다. `stop`은 쓰기 대기와 무관하게 취소를 요청하고 실제 작업 종료·소켓 반환을 기다린다. 접속 전 대기도 취소된다. Broker 접속 제한은 5초이며 제품의 접속·KeepAlive 제한과 별개다.

각 방향 4KB 버퍼와 비동기 작업 하나를 사용한다. MQTT 패킷을 해석하거나 Payload를 기록하지 않고 UTC·단조 경과 시간·방향·바이트 수·명령 ACK만 기록한다. TCP read 단위는 MQTT 패킷 경계가 아니므로 폐기 로그 하나를 패킷 하나로 일반화하지 않는다. 이번 시험에서는 같은 Client ID의 Broker PINGRESP 로그와 함께 해석한다.

일반 네트워크 장애 프레임워크·운영 프록시가 아니다. 세션 재접속·동시 Client·응답 보관·인증·운영 처리량 보장은 없다. 범용 프록시는 다양한 장애 주입에 유리하지만 설치·설정·진단 범위가 커지므로 이번 한 방향 차단 실습에서는 도입하지 않았다. 여러 세션·지연/속도 제한이 필요해지면 도구 범위를 다시 정한다.

## 증거와 경계

PR #31 수정 후 `block`/`resume`의 gate 대기는 25ms마다 최신 명령을 재확인한다. 아직 ACK하지 못한 명령이 있어도 후속 `stop`을 읽을 수 있다. ACK와 응답 쓰기의 직렬화는 유지한다. C1/C2/C3의 로그·stop·rm은 독립 시도하며 오류를 보존한다. [수정·실제 회귀 결과](../../evidence/COM-03/20261020-pr31-p2-fix-01/README.md).

도구 오류 경로는 `pwsh -NoProfile -File tests/manual/COM03-C/Verify-ToolRecovery.ps1 -RunId <새-ID>`로 재검증한다. 실제 루프백 TCP 프록시 3건과 Docker 호출 대역을 쓰는 정리 27건이며 제품 자동시험이나 실제 MQTT를 대신하지 않는다.

실행별 `evidence/COM-03/<실행ID>/`에 proxy.jsonl·Broker/콘솔 로그·차단 중 소켓·환경·소스/바이너리 해시·정리 결과를 남긴다. 실패한 실행도 보존한다. 테스트 자식 프로세스에서 Seq를 비활성화하며 실제 Seq·MQTT Explorer GUI 검증은 포함하지 않는다.

최종 실제 결과: [20261020-c3a-proxy-03](../../evidence/COM-03/20261020-c3a-proxy-03/README.md). 접속 전 stop: [20261020-c3a-stop-01](../../evidence/COM-03/20261020-c3a-stop-01/README.md).
