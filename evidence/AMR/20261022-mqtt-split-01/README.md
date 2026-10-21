# #38 MQTT 분리·보고서 대조·실제 회귀 — 20261022-mqtt-split-01

## 1. 목표와 기준

2026-10-22 사용자는 「AMR 개발 보고서」 v1.2를 현재 근거와 대조하고 맞으면 개발 순서대로 진행하도록 요청했다. PDF 5쪽을 추출·렌더링해 전체 확인했다. 첫 실행본 PR #35~37 develop 반영, 보존 MQTT/지도 구현·기존 시험, MQTT 지도 API와 최종 화면의 미검증 구분, 격자 비교 미착수는 실제 원격·기존 증거와 일치했다. 교육 자료의 제출·기술 지시는 개발 요청으로 적용하지 않았다.

보고서와 담당 계획의 첫 결과물인 [Issue #38](https://github.com/inhhlee/amr-control-system-demo/issues/38)의 MQTT 송수신만 최신 develop 위에 분리한다. 지도·격자·명령/ACK·Core/DB 구현은 후속이다. 보고서 PDF 자체는 수정하지 않는다. `report-review.json`은 보고서 해시·5쪽 확인 범위·기존 증거 대조를 기록한다.

- 통합 기준: develop `2ccdd2511fed8a146f8338250cbaa813198c0fca` / PR #37.
- 보존 기준: `a8934f0bfed803cee47d7c2e13775ba648e0862a`, `codex/preserve-simulator-mqtt-map-20261021`.
- 작업 브랜치: feature/38-simulator-mqtt. 기존 Issue/PR 중복 없음 확인 후 #38을 생성했다.
- 적용 원본: 과제2 과제계획 v0.14 초안 `mqtt-map-next-deliverable`, 기능명세 v0.12 초안 AMR-05 일부, 공통데이터명세 v0.9 초안의 기존 메시지 계약, 결정사항 v0.12 초안. 공유 URL 없는 원본은 문서명·버전·항목으로 식별한다.
- 이번 제품 코드는 보존 구현의 Telemetry 5개·Snapshot·Application·Runner·csproj 총9개를 재사용한다. MqttSimulationTests와 Ctrl+C 도구도 재사용한다. 원본 Git blob·원시 SHA256·현재 대응은 `provenance.json`, 이번 소스/DLL은 `integrity.json`을 따른다.

## 2. 환경·보존·분리 범위

Windows 10.0.26200 / SDK10.0.401 / .NET10.0.12 / net10.0, xUnit2.9.3·Test.Sdk17.14.1·adapter3.1.4. Docker Server24.0.6, 기존 로컬 eclipse-mosquitto:2.1.2-alpine 이미지 ID는 실제 실행의 environment.json에 있다. 새 패키지·이미지·서비스를 설치하지 않았다. 이번 빌드는 --no-restore로 기존 복원 결과를 사용했다.

기존 README의 #33 재확인 문단과 미추적 검증 자료를 포함한72개 파일을 원시 바이트로 백업하고 보존 stash `79c77cb45a3cd1d6b6a5e7c214842cbd659545bb`를 남겼다. 로컬 develop을 origin/develop으로 fast-forward하고 `git flow feature start 38-simulator-mqtt`로 새 작업 브랜치를 만들었다. 현재 설치 Git Flow는 AVH1.12.3이며 CONTRIBUTING의 next2.0.0과 버전이 다르다. 이미 초기화된 feature/·develop 설정의 기본 feature start를 사용했고 공유 설정·기존 브랜치 이력을 바꾸지 않았다.

기존 README는 새 develop 본문을 유지한 채 사용자가 추가한 문단만 동일하게 복원했고 원래 전체 파일은 백업/stash에 보존한다. 기존 미추적 증거71개는 바이트 동일하게 복원한다. 이 기존 문단·증거는 #38의 커밋 대상에서 제외한다. #34 분리 검증·기존 통신·솔루션은 그대로 유지하고 지도/보고 도구·무관한 통신 수동 시험 변경은 가져오지 않았다.

제품 변경은 기존 MQTT 접속·송신·스냅샷과 Application/Runner 연결이다. 실제 검증 도구는 고정된 이전 PC의 Docker 경로를 PATH/명시 DockerPath로 바꾸고 현재 실행 안내를 작성했다. 처음 PATH 조회가 docker.exe/docker 두 결과를 합쳐 잘못된 경로를 만들었던 도구 오류를 수정해 첫 결과를 선택했다. 제품 코드 수정과 구분한다.

## 3. 입력·예상·실제 결과

| 구분 | 입력·예상 | 이번 실제 결과·판정 | 근거 |
| --- | --- | --- | --- |
| 전체 솔루션 빌드 | 현재5프로젝트, 기존 패키지로 빌드 | 종료0·경고0·오류0, 통과 | sandbox-build.log/json |
| 제한 환경 VSTest | Simulator testhost 연결 후 실행 | 90초 연결 시간 초과·중단, 통과 아님 | sandbox-simulator.log/json/trx |
| 표준 Simulator 자동시험 | 같은 빌드로 정상 환경에서151건 | 151통과·실패0·skip0·종료0 | final-simulator.trx/log/json |
| 표준 통신 자동시험 | 참조한 기존 통신의 관련 전체768건 | 768통과·실패0·skip0·종료0 | final-communication.trx/log/json |
| 실제 검증 첫 실행 | Docker 실행 파일 찾기 | 경로 결과2개 결합 오류로 Broker 생성 전 중단 | live-run-01.log |
| 기본 실제 이동 | (-1,0)→(1,0)→(1,2), 속도1, 위치100ms, 송신200/1000ms | Telemetry21·Heartbeat4, 최종(1,2)·IDLE·속도0·종료0 | [실제7건](../20261022-mqtt-split-live-02/verification.json)·default.stdout.log·receiver.stdout.log |
| 설정 변경 | (5,3)→(5,4)→(7,4), 속도2 | Telemetry9·Heartbeat2, 최종(7,4)·IDLE·0·종료0 | 같은 실행의 changed 입력·로그 |
| 처음부터 도착 | (5,3)→(5,3) | Telemetry2·Heartbeat1, 이동 없이 IDLE·0·종료0 | already-arrived 입력·로그 |
| Retain 없음 | 새 구독자가 과거 메시지를 받지 않음 | 구독 대기 시간 초과27·메시지 없음, 예상대로 통과 | retained.stderr.log·verification.json |
| 독립 송신·취소 | Int64 최대 위치 갱신 대기 중 송신, 소유 프로세스에 Windows Ctrl+C 이벤트 | Heartbeat3 확인 후 취소 로그·실제 프로세스 종료0 | cancel 입력·로그·cancel-signal.json |
| 연결 후 단절 | 소유 Broker 정지 | Simulator/관제 모두 종료1, ConnectionLost·거짓 완료 없음 | broker-loss 로그·verification.json |
| 최초 접속 실패 | Broker 없는 동일 주소로 새 Simulator | 이동 시작 없이 종료1 | connect-failure 로그·verification.json |
| 종료·기존 환경 보존 | 소유 Broker/프로세스 정리, 기존 컨테이너 ID·상태 유지 | OwnedBrokerRemoved/OwnedProcessesExited/ExistingContainersPreserved 모두 true | verification.json·containers-before/after.jsonl |

실제 정상3개 설정의 수신32개 Telemetry 위치와 Heartbeat7개를 송신 Payload·이동 로그에 대조했다. 39는 자동시험 개수가 아니다. 실제 시나리오7개와 자동151/768을 합산하지 않는다. Simulator151은 기존135와 MQTT16을 포함한다. 이전 보존151/실제7과 이번 실행도 합산하지 않는다.

빌드는 2026-10-22 01:34:09~01:34:29 KST, 최종 Simulator 시험01:36:54~01:36:58, 실제 MQTT01:37:10~01:37:36이다. 정확한 명령/시각·이미지·포트·코드/설정은 각 json을 따른다. 실제 기본/변경/처음부터 도착의 최종 수신 값을 확인했으며 Published 반환 자체를 Broker 수신 보장으로 해석하지 않았다.

## 4. 완료조건·설계 확인

- [x] 최신 develop 위에 필요한 MQTT 변경만 분리하고 기존 첫 실행본·별도 변경을 보존한다. — provenance·integrity·기존 소스/검증 자료 대조.
- [x] 정상/변경/처음부터 도착의 실제 ID·위치·상태·현재 속도를 관제 수신과 대조한다. — 실제3설정·32Telemetry·7Heartbeat.
- [x] 위치 갱신과 독립 송신, 취소·실제 작업 종료·소유 자원 정리를 확인한다. — 대역16건·실제 긴 갱신 대기·Ctrl+C·정리 결과.
- [x] 접속 실패와 연결 후 단절을 구분하고 이동 시작/실패 이유/종료 코드를 검증한다. — 실제2시나리오·자동 경계 시험.
- [x] 최종 IDLE·속도0과 QoS0·Retain=false·관련 회귀를 확인한다. — 최종 관제수신·Retain 검사·151/768건.
- [x] 보존 근거와 이번 실행·버전·예상/실제·미검증·실행 안내를 연결한다. — 현재 README·manifest·TRX·실제 원본·MQTT-DEMO.

이 체크는 구현·시험 완료조건이며 사용자 승인 리뷰·develop 병합·Issue 종료와 구분한다.

한 소유자가 이동과 송신의 두 작업을 시작하고 끝날 때 모두 기다린다. 송신은 불변 최신 스냅샷 하나만 읽고, 이동 모델 변경은 Runner 한 곳에서 순차 수행한다. 취소 요청은 실제 작업 종료와 다르므로 finally에서 취소 후 합류하고 이벤트를 해제·연결/Client를 정리한다. 접속 실패·연결 후 단절·사용자 취소·최종 발행 완료를 구분하고 최초 종료 원인을 유지한다.

매 이동을 큐에 모두 담는 대안은 중간 위치 보존에 유리하지만 버퍼 상한·밀림·폐기·종료 정책이 추가로 필요하다. 현재 한 대의 최신 위치 시연에서는 스냅샷 하나와 독립 송신 주기를 재사용했다. 성능/메모리 벤치마크는 하지 않았다. 모든 중간 위치의 보존·대규모 AMR·재전송 보장이 요구되면 재검토한다. 프로세스 내부 동기화로 MQTT의 전달 순서·중복·유실까지 보장하지 않는다.

예상 PR 질문:

1. **기존 근거만으로 분리 성공을 판단했나?** 아니다. 보존 소스9개와 시험 코드를 대응시키고 이 PC의 최신 develop 기준으로 전체 빌드·151/768건·실제7시나리오를 새로 실행했다. 이전 원본은 보존 브랜치에 남긴다.
2. **Telemetry 주기는 이동 주기인가?** 아니다. 이동100ms·송신200/1000ms는 별도 시험값이고 매우 긴 이동 대기에서도 송신·취소를 검증했다. 운영 주기·Core 버퍼/Timeout 수치를 확정하지 않았다.
3. **도착·송신 완료를 관제 수신 완료로 볼 수 있나?** QoS0 Published는 미확인이다. 이번에는 수신 Payload와 최종 좌표·IDLE·0을 실제 대조했다. ACK·DB·지도 화면은 이번 완료 범위가 아니다.

## 5. 남은 범위·재현

재현 명령은 이 폴더 Verify.ps1과 [MQTT-DEMO](../../../tests/manual/AMR01/MQTT-DEMO.md)를 따른다. 제한 환경의 testhost/명명 파이프 접근 문제와 실제 제품 실패를 구분한다. 이번 실제 도구는 Docker 접근 가능한 환경에서 실행했다. 이미지 자동 다운로드·기존 컨테이너 재시작/중단은 하지 않았다.

사용자 키보드 직접 입력·실제 MQTT Explorer/Seq 화면·MQTT 수신 지도 최종 브라우저 대조·격자 탐색·다중 AMR·Alarm·명령 최종 ACK·Core/DB·운영 부하는 이번 미검증이다. 다음 단계는 이 MQTT 결과의 리뷰/반영 후 별도 수신 지도 작업이다. 지도와 격자 코드를 이 Issue에 추가하지 않는다.

현재 소스·입력·DLL과 근거 목록의 SHA256은 integrity.json·artifact-sha256.json에 연결한다. 과거 HEAD+미커밋 표시를 변경하지 않고 이번 게시 커밋과의 대응을 별도로 남긴다. 일일업무는 C:\process-plan의 기존 2026-10-22 파일에 요약과 링크를 추가한다.
