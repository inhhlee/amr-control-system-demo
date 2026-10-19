# COM04-A Heartbeat 검사·전달 검증결과 — 20261019-final-06

- 작성자·실행자: Codex, 2026-10-19. 학습 답변은 제품 검증 실적에서 제외한다.
- 관련 Issue: [#17](https://github.com/inhhlee/amr-control-system-demo/issues/17), 선행 [PR #16](https://github.com/inhhlee/amr-control-system-demo/pull/16).
- 최종 근거: [verification.json](verification.json). 이 실행은 03/05의 증거 대조·Broker 로그 수집이며 새 제품 재시험이 아니다.

## 1. 목표와 적용 기준

유효한 Heartbeat만 정상 모델로 Core 대역에 전달하고, 오류 입력 뒤 정상 입력을 계속 처리하는 COM04-A를 확인했다. 검사기 단독·Fake 경계와 실제 MQTT·종료 시험을 구분한다. 실제 Core 업무·다른 세 유형·큐·Timeout·자동 재접속·COM01-A 후속 시험 도구 보완은 제외한다.

| 근거 | 실제 읽은 버전·관련 항목 |
| --- | --- |
| 기능명세 / 요구사항정의 | v0.7 COM-04 / v0.5 R-04 |
| 공통규칙 | v0.4 C-01·C-05·C-06·C-10 |
| 공통데이터명세 | v0.4, COM-01-Heartbeat-v0.2, 수신 계약 v3 연결 |
| 검토 및 결정사항 | v0.5 D-03·D-04 |
| 과제1 과제계획 / 전체일정 | 착수 시 v0.22 / v0.15. Issue의 초안 v0.14/v0.11·게시 준비 v0.20/v0.14와 다름. #17 범위·완료조건 유지 |
| 개발환경 / 개발 진행 절차 | v0.7 / v0.6. 실제 SDK·패키지는 본 저장소 파일·설치 환경 확인 |
| 기존 수신 계약 | `C:\practice\src\Amr.Communication\Validation\CONTRACT.md`, v3의 Heartbeat·오류 경계. 기존 제품 소스·통과 기록은 복사하지 않음 |

위 계획·명세의 원본은 `C:\process-plan`이다. 공유 URL이 없는 문서는 문서명·버전·관련 항목으로 식별한다. 원본의 기존 1883·다중 Topic·큐 설명은 C:\practice의 적용 범위이며 이번에는 #17에 따라 1884·Heartbeat 한 유형·동기 수신자 대역을 적용했다. 새 임시 운영 정책을 확정하지 않았다. `spec_revision`·`verification_index` 추적 파일은 이 저장소에 없으므로 해당 없음이다.

## 2. 실행 환경

| 항목 | 확인한 값·근거 |
| --- | --- |
| 코드 위치 | C:\amr-control-system-demo |
| 기준 / 브랜치 | `82f6bb109dd964ca486f1bec5a99ab88d1d84cdf` + 미커밋 #17 변경 / `feature/17-heartbeat-validation` |
| 기존 사용자 변경 | HeartbeatSubscription.cs의 빈 `//` 3줄 삭제. 사용자 승인으로 미커밋 보존·기능 커밋 제외. 실행 로직 변경 없음 |
| 코드 식별 | [자동시험 SHA-256](../20261019-integration-03/source-version.json), [실제 MQTT SHA-256](../20261019-mqtt-05/source-version.json). 최종 대조 시 현재 전체 소스와 두 목록 일치 |
| OS·SDK·패키지 | Windows 10.0.26200 X64, .NET SDK 10.0.401, net10.0, MQTTnet 5.2.0.1603, xUnit 2.9.3. [자동시험 환경](../20261019-integration-03/environment.json) |
| Broker | Mosquitto 2.1.2, Docker 29.7.2. Compose amr-demo, `docker/compose.yaml`과 해당 저장소 마운트. [환경](../20261019-mqtt-05/environment.json), [마운트](../20261019-mqtt-05/broker-mounts.json) |
| MQTT 조건 | 호스트 127.0.0.1:1884, 컨테이너 내부 1883, MQTT 5, 발행·구독 QoS 0, Retain=false, 새 Client ID |
| 실행 시각(KST) | 단독 빌드/시험 11:41, 연결 빌드/시험 11:47, 최종 실제 MQTT 11:55:08~11:55:12, 근거 대조 11:55:51. 각 JSON은 UTC ISO 시각 보존 |
| 실제 구성 / 대역 | 실제 Mosquitto·MQTTnet·콘솔, 정상 수신자는 콘솔 Core 대역. 자동시험은 직접 검사 및 FakeMqttClient |

커밋 시에는 사용자 변경을 제외하기 위해 빈 주석 3줄이 있는 버전만 Git 인덱스에 담는다. 작업 파일과 실행 해시는 그대로 보존하며, 게시본과 실행본의 차이는 이 주석 3줄이다. 새로운 제품 실행 결과로 표시하지 않는다.

## 3. 입력과 절차

| 시험 ID | 입력·명령 | 예상 결과 |
| --- | --- | --- |
| AUTO-VALID | 정상 ID·ts·status, Unicode ID, 분할 바이트, 256바이트 초과 입력 | 값과 주어진 수신 UTC 시각 보존 |
| AUTO-FIELDS | 필수 누락, 다른 ID, 자료형·허용값 오류, 추가/중복/대소문자 필드 | 실패 Code·Field·고정 Reason, 정상 모델 없음 |
| AUTO-JSON | 잘못된 UTF-8, JSON 문법, 최상위 배열·null·스칼라, 주석·후행 쉼표, 잘못된 Unicode escape | 거부 후 정상 입력 검사 가능, 원문·파서 예외 내용 미노출 |
| AUTO-TS | Int64 min/max·10자리·0·음수 / 범위 초과·문자열·소수·지수 | 유효 정수 그대로 보존 / 나머지 거부 |
| AUTO-RESOURCE | 65,536/65,537 bytes, UTF-8 멀티바이트, 깊이 16/17 | 크기를 먼저 검사. 16은 JSON 해석 뒤 별도 필드 오류, 17은 JSON/깊이 오류 |
| AUTO-DELIVERY | 승인 전·필터 밖·종료 후, 오류→정상, 수신자 예외→정상 | 전달 경계 유지, 정상만 콜백당 1회, 예외와 입력 거부 구분 |
| BUILD / AUTO | `dotnet build AmrControlSystem.slnx --no-restore --verbosity minimal`; `dotnet test tests/Amr.Communication.Tests/Amr.Communication.Tests.csproj --no-build --logger "trx;LogFileName=tests.trx" --results-directory evidence/COM-04/20261019-integration-03` | 빌드·전체 시험 성공 |
| MQTT-01~04 | [입력 4건](../20261019-mqtt-05/publications.json): ts 누락 → 정상 → ID 불일치 → 349바이트 정상 | 거부 2건·그 뒤 정상 2건, 필드·콜백 수신 시각 보존, 거부 원문 숨김 |
| MQTT-STOP | 만든 콘솔의 프로세스 구성원을 확인 후 Windows CTRL_C_EVENT | 구독·연결·자원 정리, 실제 프로세스 종료 0, Broker 해제 로그 |

자동시험의 정확한 입력과 개별 예상 결과는 [검사기 시험](../../../tests/Amr.Communication.Tests/HeartbeatValidatorTests.cs)과 [수신부 시험](../../../tests/Amr.Communication.Tests/HeartbeatSubscriptionTests.cs)의 이름을 [TRX](../20261019-integration-03/tests.trx)와 연결한다. 깊이 16의 중첩 데이터는 자원 제한 안에 있어도 Heartbeat의 세 스칼라 필드 계약상 정상 메시지가 아니다. `ts`는 날짜로 변환하거나 현재 값으로 덮어쓰지 않았다.

## 4. 실제 결과와 판정

| 구분 | 실제 결과 | 판정·증거 |
| --- | --- | --- |
| 빌드 | 경고·오류 0, 종료 0 | 통과 — [빌드 로그](../20261019-integration-03/build.log), [명령·시각](../20261019-integration-03/build-result.json) |
| 자동시험 | 검사기 86 + 구독/전달 21 + 수명주기 6 = 113개 통과, 실패·건너뜀 0 | 통과 — [TRX](../20261019-integration-03/tests.trx), [시험 로그](../20261019-integration-03/test.log) |
| 실제 MQTT | 4건 수신, MissingField·$.ts / IdMismatch·$.id 각 1건, 정상 수신 2건. 두 정상 모델의 ID·ts·status·수신 UTC 시각 보존 | 통과 — [콘솔](../20261019-mqtt-05/console.stdout.log), [시나리오 판정](../20261019-mqtt-05/scenario-result.json) |
| 실제 종료 | CTRL_C_EVENT 후 구독 해제→연결 해제→정리, Process.ExitCode=0 | 통과 — 같은 시나리오 판정, [Broker 해제](broker.log) |
| 코드·근거 대조 | 03/05 소스 목록과 현재 파일 해시 일치, TRX·로그 수량·처리 순서 대조 | 통과 — [대조 스크립트](verify-result.ps1), [최종 판정](verification.json). 제품 재시험과 구분 |
| 문서 검사 | 현재 README·새 검증결과의 로컬 파일 링크 108개, 누락 0 | 통과 — [문서 검사](document-checks.json). 제품 시험과 구분 |

전체 스테이징 공백 검사에서 04의 원본 failure.log와 05의 실행 당시 verify-mqtt.ps1에 끝 빈 줄이 발견됐다. 실행 이력을 보존했으며 제품 소스·시험 소스·작성 Markdown에는 공백 오류가 없다. 이 경고를 제품 시험 실패로 집계하지 않는다.

## 5. 문제와 후속 조치

| 실행 | 문제·실제 범위 | 처리·남은 영향 |
| --- | --- | --- |
| [01](../20261019-validator-01/README.md) | obj 쓰기 권한으로 첫 빌드 실패, 시험 미실행 | 승인된 샌드박스 밖 빌드를 새 02로 수행 |
| [02](../20261019-validator-02/README.md) | 단독 검사기 104개 통과, 연결 전 | 연결 변경 후 새 03에서 113개 시험 |
| [04](../20261019-mqtt-04/README.md) | 실제 동작·프로세스 종료 0 확인 후 Broker 로그 수집 핸들 오류, 컨트롤러 종료 1 | 제품 코드 변경 없이 부모 수집으로 분리, 새 05 실행 |
| [05](../20261019-mqtt-05/README.md) | 시나리오·컨트롤러 종료 0, 부모의 날짜 자동변환으로 Docker --since 실패 | 06에서 ISO UTC를 명시해 로그 수집·대조 완료. 제품 재시험 없음 |

이번 시험 도구는 COM04-A 실행 증거 안에서만 사용했다. 기존 COM01-A 도구·증거 재실행 보호 보완은 제외했다. 사용자 키보드 직접 Ctrl+C·GUI·실제 Core 업무는 미검증이다. Fake에서 수신자 예외 구분은 확인했지만 실제 Core의 장애 복구·재전달·로그 저장 장애는 검증하지 않았다. QoS 0의 운영 전달 보장·동시 종료 경합·과부하·자동 재접속도 범위 밖이다.

## 6. 종합 결과

- 정상 Heartbeat의 필드·수신 시각 보존과 콜백당 한 번 전달: 자동시험·실제 MQTT 통과.
- 불량 입력 거부·원문 없는 실패 이유 기록·정상 수신자 미호출: 자동시험 및 실제 두 오류 사례 통과.
- 거부 뒤 정상 처리 지속·정상 종료: 자동시험·실제 MQTT 통과.
- README·실행별 증거 연결 완료. Issue·PR에는 게시 시 실제 커밋과 이 근거를 연결한다. PR 게시·리뷰·병합 결과는 해당 GitHub 기록을 따른다.
- COM04-A Heartbeat 경로에 한정한다. #18(COM04-B), 실제 다중 Topic(COM01-B), COM-04 전체·과제1 전체 완료로 확대하지 않는다.
